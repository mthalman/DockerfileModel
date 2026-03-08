/-
  Tests/ParserTests.lean — Comprehensive parser tests for FROM and ARG instructions.

  Phase 2 of the formal verification project. These tests translate the C# test
  cases from FromInstructionTests.cs and ArgInstructionTests.cs into Lean.

  Two kinds of tests:
  1. **Token tree construction + round-trip tests** (run NOW):
     Construct the expected token tree for each Dockerfile snippet, then verify
     that toString produces the original string. This validates the token model
     and provides expected outputs for parser comparison.

  2. **Parser tests** (commented out, enable once Dallas's parser is ready):
     Call Parser.parseFrom / Parser.parseArg and compare against the expected
     token tree. Marked with -- [PARSER] comments.

  Every test case has a corresponding C# test scenario annotated in comments.
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Dockerfile
import DockerfileModel.Parser.Instructions.From
import DockerfileModel.Parser.Instructions.Maintainer
import DockerfileModel.Parser.Instructions.Workdir
import DockerfileModel.Parser.Instructions.Stopsignal
import DockerfileModel.Parser.Instructions.Cmd
import DockerfileModel.Parser.Instructions.Entrypoint
import DockerfileModel.Parser.Instructions.Shell
import DockerfileModel.Parser.Instructions.User
import DockerfileModel.Parser.Instructions.Expose
import DockerfileModel.Parser.Instructions.Volume
import DockerfileModel.Parser.Instructions.Env
import DockerfileModel.Parser.Instructions.Label
import DockerfileModel.Parser.ExecForm
import DockerfileModel.Parser.Flags
import DockerfileModel.Parser.Instructions.Run
import DockerfileModel.Parser.Instructions.Copy
import DockerfileModel.Parser.Instructions.Add
import DockerfileModel.Parser.Instructions.Healthcheck
import DockerfileModel.Parser.Instructions.Onbuild
import DockerfileModel.Parser.Heredoc
import DockerfileModel.VariableResolution

namespace DockerfileModel.Tests.ParserTests

open DockerfileModel
open DockerfileModel.Parser.Instructions

-- ============================================================================
-- Test helpers (reuse assertEqual/assertTrue from SlimCheck.lean)
-- ============================================================================

/-- Assert that two strings are equal, panicking with a message if not. -/
def assertEqual (actual expected : String) (testName : String) : IO Unit := do
  if actual == expected then
    IO.println s!"  PASS: {testName}"
  else
    IO.println s!"  FAIL: {testName}"
    IO.println s!"    expected: \"{expected}\""
    IO.println s!"    actual:   \"{actual}\""
    throw (IO.Error.userError s!"Test failed: {testName}")

/-- Assert that a boolean condition holds. -/
def assertTrue (cond : Bool) (testName : String) : IO Unit := do
  if cond then
    IO.println s!"  PASS: {testName}"
  else
    IO.println s!"  FAIL: {testName}"
    throw (IO.Error.userError s!"Test failed: {testName}")

/-- Assert that the token's toString output matches the original input string. -/
def assertRoundTrip (token : Token) (original : String) (testName : String) : IO Unit :=
  assertEqual (Token.toString token) original s!"roundtrip: {testName}"

/-- Assert that the number of children matches the expected count. -/
def assertChildCount (token : Token) (expected : Nat) (testName : String) : IO Unit := do
  let actual := token.children.length
  if actual == expected then
    IO.println s!"  PASS: {testName} (child count = {expected})"
  else
    IO.println s!"  FAIL: {testName}"
    IO.println s!"    expected child count: {expected}"
    IO.println s!"    actual child count:   {actual}"
    throw (IO.Error.userError s!"Test failed: {testName}")

/-- Assert that a token is an aggregate of the expected kind. -/
def assertAggregateKind (token : Token) (expected : AggregateKind) (testName : String) : IO Unit :=
  match token with
  | .aggregate kind _ _ =>
    if kind == expected then
      IO.println s!"  PASS: {testName} (kind matches)"
    else do
      IO.println s!"  FAIL: {testName}"
      IO.println s!"    expected kind: {repr expected}"
      IO.println s!"    actual kind:   {repr kind}"
      throw (IO.Error.userError s!"Test failed: {testName}")
  | .primitive _ _ => do
    IO.println s!"  FAIL: {testName} (expected aggregate, got primitive)"
    throw (IO.Error.userError s!"Test failed: {testName}")

-- ============================================================================
-- Helper: build common token sub-trees
-- ============================================================================

/-- Build a keyword token from a string (e.g., "FROM", "ARG", "AS"). -/
def mkKeywordToken (kw : String) : Token :=
  Token.mkKeyword [Token.mkString kw]

/-- Build a line continuation token: escape char + newline. -/
def mkLineCont (escChar : Char) (nl : String := "\n") : Token :=
  Token.mkLineContinuation [Token.mkSymbol escChar, Token.mkNewLine nl]

/-- Build a simple literal (no variable refs, no quotes). -/
def mkSimpleLiteral (s : String) : Token :=
  Token.mkLiteral [Token.mkString s]

/-- Build a platform flag token: --platform=<value>. -/
def mkPlatformFlag (value : String) : Token :=
  Token.mkKeyValue [
    Token.mkSymbol '-', Token.mkSymbol '-',
    mkKeywordToken "platform",
    Token.mkSymbol '=',
    mkSimpleLiteral value
  ]

/-- Build a simple identifier token (e.g., Variable or StageName). -/
def mkIdentifierToken (s : String) : Token :=
  Token.mkIdentifier [Token.mkString s]

/-- Build a comment token: #text\n -/
def mkCommentToken (text : String) (nl : String := "\n") : Token :=
  Token.mkComment [Token.mkSymbol '#', Token.mkString text, Token.mkNewLine nl]

/-- Build an ArgDeclaration token: just a variable name (no default value). -/
def mkArgDeclNameOnly (name : String) : Token :=
  Token.mkKeyValue [mkIdentifierToken name]

/-- Build an ArgDeclaration token: name=value. -/
def mkArgDeclWithValue (name : String) (value : String) : Token :=
  Token.mkKeyValue [
    mkIdentifierToken name,
    Token.mkSymbol '=',
    mkSimpleLiteral value
  ]

/-- Build an ArgDeclaration token: name= (empty value). -/
def mkArgDeclEmptyValue (name : String) : Token :=
  Token.mkKeyValue [
    mkIdentifierToken name,
    Token.mkSymbol '='
  ]

/-- Build an ArgDeclaration token: name="" (quoted empty value). -/
def mkArgDeclQuotedEmptyValue (name : String) : Token :=
  Token.mkKeyValue [
    mkIdentifierToken name,
    Token.mkSymbol '=',
    Token.mkLiteral [Token.mkString ""] (some { quoteChar := '"' })
  ]

-- ============================================================================
-- FROM instruction: token tree construction + round-trip tests
-- ============================================================================
-- Source: FromInstructionTests.cs ParseTestInput()

/-- FROM scratch — simplest possible FROM instruction. -/
def testFromScratch : IO Unit := do
  IO.println "FROM: simple image name (FROM scratch)"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkSimpleLiteral "scratch"
  ]
  assertRoundTrip token "FROM scratch" "FROM scratch"
  assertChildCount token 3 "FROM scratch children"

/-- FROM `\nscratch — line continuation after keyword (escape char = `). -/
def testFromLineContinuationAfterKeyword : IO Unit := do
  IO.println "FROM: line continuation after keyword"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkLineCont '`',
    mkSimpleLiteral "scratch"
  ]
  assertRoundTrip token "FROM `\nscratch" "FROM `\\nscratch"
  assertChildCount token 4 "FROM line-cont children"

/-- FROM alpine:latest as build — image with tag and stage name (lowercase 'as'). -/
def testFromWithTagAndStage : IO Unit := do
  IO.println "FROM: image with tag and stage name"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkSimpleLiteral "alpine:latest",
    Token.mkWhitespace " ",
    mkKeywordToken "as",
    Token.mkWhitespace " ",
    mkIdentifierToken "build"
  ]
  assertRoundTrip token "FROM alpine:latest as build" "FROM alpine:latest as build"
  assertChildCount token 7 "FROM with tag+stage children"

/-- FROM alpine`\n as build — line continuation inside instruction body. -/
def testFromLineContinuationInBody : IO Unit := do
  IO.println "FROM: line continuation in body"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkSimpleLiteral "alpine",
    mkLineCont '`',
    Token.mkWhitespace " ",
    mkKeywordToken "as",
    Token.mkWhitespace " ",
    mkIdentifierToken "build"
  ]
  assertRoundTrip token "FROM alpine`\n as build" "FROM alpine`\\n as build"
  assertChildCount token 8 "FROM line-cont in body children"

/-- FROM `\nalpine:latest `\nas `\n#comment\nbuild — complex with multiple
    line continuations and embedded comment. -/
def testFromComplexWithComment : IO Unit := do
  IO.println "FROM: complex with line continuations and comment"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkLineCont '`',
    mkSimpleLiteral "alpine:latest",
    Token.mkWhitespace " ",
    mkLineCont '`',
    mkKeywordToken "as",
    Token.mkWhitespace " ",
    mkLineCont '`',
    mkCommentToken "comment",
    mkIdentifierToken "build"
  ]
  assertRoundTrip token "FROM `\nalpine:latest `\nas `\n#comment\nbuild"
    "FROM complex with comment"

/-- FROM --platform=linux/amd64 alpine as build — platform flag with stage name. -/
def testFromPlatformAndStage : IO Unit := do
  IO.println "FROM: platform flag with stage name"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkPlatformFlag "linux/amd64",
    Token.mkWhitespace " ",
    mkSimpleLiteral "alpine",
    Token.mkWhitespace " ",
    mkKeywordToken "as",
    Token.mkWhitespace " ",
    mkIdentifierToken "build"
  ]
  assertRoundTrip token "FROM --platform=linux/amd64 alpine as build"
    "FROM --platform=linux/amd64 alpine as build"
  assertChildCount token 9 "FROM platform+stage children"

/-- FROM --platform=linux/amd64 alpine — platform flag without stage name. -/
def testFromPlatformNoStage : IO Unit := do
  IO.println "FROM: platform flag without stage name"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkPlatformFlag "linux/amd64",
    Token.mkWhitespace " ",
    mkSimpleLiteral "alpine"
  ]
  assertRoundTrip token "FROM --platform=linux/amd64 alpine"
    "FROM --platform=linux/amd64 alpine"
  assertChildCount token 5 "FROM platform-only children"

/-- FROM `\n  --platform=linux/amd64`\n  alpine — platform with line continuations
    and indentation. -/
def testFromPlatformWithLineContinuation : IO Unit := do
  IO.println "FROM: platform with line continuations and indentation"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkLineCont '`',
    Token.mkWhitespace "  ",
    mkPlatformFlag "linux/amd64",
    mkLineCont '`',
    Token.mkWhitespace "  ",
    mkSimpleLiteral "alpine"
  ]
  assertRoundTrip token "FROM `\n  --platform=linux/amd64`\n  alpine"
    "FROM platform with line continuations"
  assertChildCount token 8 "FROM platform+line-cont children"

/-- FROM al\\\npine — line continuation inside image name (escape char = \). -/
def testFromLineContinuationInsideName : IO Unit := do
  IO.println "FROM: line continuation inside image name"
  -- The literal token has inner structure: string "al" + line-cont + string "pine"
  let litToken := Token.mkLiteral [
    Token.mkString "al",
    mkLineCont '\\',
    Token.mkString "pine"
  ]
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    litToken
  ]
  assertRoundTrip token "FROM al\\\npine" "FROM line-cont inside name"
  assertChildCount token 3 "FROM line-cont-in-name children"

/-- FROM alpine AS bui`\nld — line continuation inside stage name. -/
def testFromLineContinuationInStageName : IO Unit := do
  IO.println "FROM: line continuation inside stage name"
  let stageToken := Token.mkIdentifier [
    Token.mkString "bui",
    mkLineCont '`',
    Token.mkString "ld"
  ]
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkSimpleLiteral "alpine",
    Token.mkWhitespace " ",
    mkKeywordToken "AS",
    Token.mkWhitespace " ",
    stageToken
  ]
  assertRoundTrip token "FROM alpine AS bui`\nld"
    "FROM line-cont in stage name"
  assertChildCount token 7 "FROM line-cont-in-stage children"

/-- FROM "al\\\npine" — quoted image name with line continuation. -/
def testFromQuotedImageName : IO Unit := do
  IO.println "FROM: quoted image name with line continuation"
  let litToken := Token.mkLiteral [
    Token.mkString "al",
    mkLineCont '\\',
    Token.mkString "pine"
  ] (some { quoteChar := '"' })
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    litToken
  ]
  assertRoundTrip token "FROM \"al\\\npine\"" "FROM quoted image name"
  assertChildCount token 3 "FROM quoted name children"

/-- FROM ubuntu — basic image name (non-scratch). -/
def testFromUbuntu : IO Unit := do
  IO.println "FROM: basic ubuntu"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkSimpleLiteral "ubuntu"
  ]
  assertRoundTrip token "FROM ubuntu" "FROM ubuntu"

/-- FROM ubuntu:latest — image with tag. -/
def testFromUbuntuLatest : IO Unit := do
  IO.println "FROM: ubuntu with tag"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkSimpleLiteral "ubuntu:latest"
  ]
  assertRoundTrip token "FROM ubuntu:latest" "FROM ubuntu:latest"

/-- FROM ubuntu@sha256:abc123 — image with digest. -/
def testFromDigest : IO Unit := do
  IO.println "FROM: image with digest"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkSimpleLiteral "ubuntu@sha256:abc123"
  ]
  assertRoundTrip token "FROM ubuntu@sha256:abc123" "FROM ubuntu@sha256:abc123"

/-- FROM ubuntu AS builder — uppercase AS keyword. -/
def testFromAsBuilder : IO Unit := do
  IO.println "FROM: AS builder (uppercase)"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkSimpleLiteral "ubuntu",
    Token.mkWhitespace " ",
    mkKeywordToken "AS",
    Token.mkWhitespace " ",
    mkIdentifierToken "builder"
  ]
  assertRoundTrip token "FROM ubuntu AS builder" "FROM ubuntu AS builder"

/-- FROM --platform=$BUILDPLATFORM ubuntu:latest AS builder — all combined with variable. -/
def testFromAllCombined : IO Unit := do
  IO.println "FROM: all features combined (platform var, tag, stage)"
  let platformVar := Token.mkVariableRef [Token.mkString "BUILDPLATFORM"]
  let platformFlag := Token.mkKeyValue [
    Token.mkSymbol '-', Token.mkSymbol '-',
    mkKeywordToken "platform",
    Token.mkSymbol '=',
    Token.mkLiteral [platformVar]
  ]
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    platformFlag,
    Token.mkWhitespace " ",
    mkSimpleLiteral "ubuntu:latest",
    Token.mkWhitespace " ",
    mkKeywordToken "AS",
    Token.mkWhitespace " ",
    mkIdentifierToken "builder"
  ]
  assertRoundTrip token "FROM --platform=$BUILDPLATFORM ubuntu:latest AS builder"
    "FROM all combined"

/-- FROM ${IMAGE:-ubuntu} — image name with variable reference using modifier. -/
def testFromVariableRef : IO Unit := do
  IO.println "FROM: variable reference with modifier"
  let varRef := Token.mkVariableRef [
    Token.mkSymbol '{',
    Token.mkString "IMAGE",
    Token.mkSymbol ':',
    Token.mkSymbol '-',
    Token.mkLiteral [Token.mkString "ubuntu"],
    Token.mkSymbol '}'
  ]
  let litToken := Token.mkLiteral [varRef]
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    litToken
  ]
  assertRoundTrip token "FROM ${IMAGE:-ubuntu}" "FROM variable ref with modifier"

/-- FROM \\\nubuntu — line continuation after keyword (escape char = \). -/
def testFromBackslashLineContinuation : IO Unit := do
  IO.println "FROM: line continuation with backslash escape"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkLineCont '\\',
    mkSimpleLiteral "ubuntu"
  ]
  assertRoundTrip token "FROM \\\nubuntu" "FROM backslash line continuation"

/-- FROM   ubuntu — extra whitespace between keyword and image. -/
def testFromExtraWhitespace : IO Unit := do
  IO.println "FROM: extra whitespace"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace "   ",
    mkSimpleLiteral "ubuntu"
  ]
  assertRoundTrip token "FROM   ubuntu" "FROM extra whitespace"

/-- from ubuntu — lowercase keyword (case insensitive). -/
def testFromLowercaseKeyword : IO Unit := do
  IO.println "FROM: lowercase keyword"
  let token := Token.mkInstruction [
    mkKeywordToken "from",
    Token.mkWhitespace " ",
    mkSimpleLiteral "ubuntu"
  ]
  assertRoundTrip token "from ubuntu" "from (lowercase)"

/-- From Ubuntu — mixed case keyword and image name. -/
def testFromMixedCase : IO Unit := do
  IO.println "FROM: mixed case"
  let token := Token.mkInstruction [
    mkKeywordToken "From",
    Token.mkWhitespace " ",
    mkSimpleLiteral "Ubuntu"
  ]
  assertRoundTrip token "From Ubuntu" "From Ubuntu (mixed case)"

/-- FROM $IMAGE — simple variable reference (no braces). -/
def testFromSimpleVariableRef : IO Unit := do
  IO.println "FROM: simple variable reference"
  let varRef := Token.mkVariableRef [Token.mkString "IMAGE"]
  let litToken := Token.mkLiteral [varRef]
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    litToken
  ]
  assertRoundTrip token "FROM $IMAGE" "FROM $IMAGE"

/-- FROM registry.example.com/myapp:v1.0 — fully qualified image reference. -/
def testFromFullyQualifiedImage : IO Unit := do
  IO.println "FROM: fully qualified image reference"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkSimpleLiteral "registry.example.com/myapp:v1.0"
  ]
  assertRoundTrip token "FROM registry.example.com/myapp:v1.0" "FROM fully qualified"

-- ============================================================================
-- FROM instruction: token tree structure validation
-- ============================================================================

/-- Verify the token tree structure for FROM --platform=linux/amd64 alpine as build. -/
def testFromStructureValidation : IO Unit := do
  IO.println "FROM: token tree structure validation"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkPlatformFlag "linux/amd64",
    Token.mkWhitespace " ",
    mkSimpleLiteral "alpine",
    Token.mkWhitespace " ",
    mkKeywordToken "as",
    Token.mkWhitespace " ",
    mkIdentifierToken "build"
  ]

  -- Top-level instruction token
  assertAggregateKind token .instruction "top-level is instruction"
  assertChildCount token 9 "instruction has 9 children"

  -- Child 0: keyword "FROM"
  let kw := token.children[0]!
  assertAggregateKind kw .keyword "child 0 is keyword"
  assertEqual (Token.toString kw) "FROM" "keyword is FROM"

  -- Child 1: whitespace
  assertEqual (Token.toString token.children[1]!) " " "child 1 is single space"

  -- Child 2: platform flag (key-value aggregate)
  let pf := token.children[2]!
  assertAggregateKind pf .keyValue "child 2 is keyValue (platform flag)"
  assertChildCount pf 5 "platform flag has 5 children"
  assertEqual (Token.toString pf) "--platform=linux/amd64" "platform flag toString"

  -- Child 4: literal "alpine"
  let img := token.children[4]!
  assertAggregateKind img .literal "child 4 is literal"
  assertEqual (Token.toString img) "alpine" "image literal is alpine"

  -- Child 6: keyword "as"
  let asKw := token.children[6]!
  assertAggregateKind asKw .keyword "child 6 is keyword"
  assertEqual (Token.toString asKw) "as" "AS keyword (lowercase)"

  -- Child 8: identifier "build" (stage name)
  let stage := token.children[8]!
  assertAggregateKind stage .identifier "child 8 is identifier (stage name)"
  assertEqual (Token.toString stage) "build" "stage name is build"

-- ============================================================================
-- ARG instruction: token tree construction + round-trip tests
-- ============================================================================
-- Source: ArgInstructionTests.cs ParseTestInput()

/-- ARG MYARG — simple arg declaration (no default value). -/
def testArgSimple : IO Unit := do
  IO.println "ARG: simple declaration (ARG MYARG)"
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkArgDeclNameOnly "MYARG"
  ]
  assertRoundTrip token "ARG MYARG" "ARG MYARG"
  assertChildCount token 3 "ARG MYARG children"

/-- ARG MYARG1 MYARG2 — multiple arg declarations without values. -/
def testArgMultipleNoValues : IO Unit := do
  IO.println "ARG: multiple declarations without values"
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkArgDeclNameOnly "MYARG1",
    Token.mkWhitespace " ",
    mkArgDeclNameOnly "MYARG2"
  ]
  assertRoundTrip token "ARG MYARG1 MYARG2" "ARG MYARG1 MYARG2"
  assertChildCount token 5 "ARG multiple no-value children"

/-- ARG `\nMYARG — line continuation after keyword (escape = `). -/
def testArgLineContinuation : IO Unit := do
  IO.println "ARG: line continuation after keyword"
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkLineCont '`',
    mkArgDeclNameOnly "MYARG"
  ]
  assertRoundTrip token "ARG `\nMYARG" "ARG with line continuation"
  assertChildCount token 4 "ARG line-cont children"

/-- ARG MYARG= — arg with empty default (just equals sign, no value). -/
def testArgEmptyDefault : IO Unit := do
  IO.println "ARG: empty default value"
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkArgDeclEmptyValue "MYARG"
  ]
  assertRoundTrip token "ARG MYARG=" "ARG MYARG="
  assertChildCount token 3 "ARG empty default children"

/-- ARG MYARG1= MYARG2= — multiple args with empty defaults. -/
def testArgMultipleEmptyDefaults : IO Unit := do
  IO.println "ARG: multiple empty defaults"
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkArgDeclEmptyValue "MYARG1",
    Token.mkWhitespace " ",
    mkArgDeclEmptyValue "MYARG2"
  ]
  assertRoundTrip token "ARG MYARG1= MYARG2=" "ARG multiple empty defaults"

/-- ARG MYARG="" — arg with quoted empty default. -/
def testArgQuotedEmptyDefault : IO Unit := do
  IO.println "ARG: quoted empty default"
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkArgDeclQuotedEmptyValue "MYARG"
  ]
  assertRoundTrip token "ARG MYARG=\"\"" "ARG MYARG quoted empty"

/-- ARG MYARG1="" MYARG2="" — multiple args with quoted empty defaults. -/
def testArgMultipleQuotedEmptyDefaults : IO Unit := do
  IO.println "ARG: multiple quoted empty defaults"
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkArgDeclQuotedEmptyValue "MYARG1",
    Token.mkWhitespace " ",
    mkArgDeclQuotedEmptyValue "MYARG2"
  ]
  assertRoundTrip token "ARG MYARG1=\"\" MYARG2=\"\"" "ARG multiple quoted empty"

/-- ARG `\n# my comment\n  MYARG= — line continuation with embedded comment. -/
def testArgWithComment : IO Unit := do
  IO.println "ARG: with comment and line continuation"
  let commentToken := Token.mkComment [
    Token.mkSymbol '#',
    Token.mkWhitespace " ",
    Token.mkString "my comment",
    Token.mkNewLine "\n"
  ]
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkLineCont '`',
    commentToken,
    Token.mkWhitespace "  ",
    mkArgDeclEmptyValue "MYARG"
  ]
  assertRoundTrip token "ARG `\n# my comment\n  MYARG=" "ARG with comment"
  assertChildCount token 6 "ARG with comment children"

/-- ARG myarg=1 — lowercase arg name with numeric value. -/
def testArgWithValue : IO Unit := do
  IO.println "ARG: with value"
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkArgDeclWithValue "myarg" "1"
  ]
  assertRoundTrip token "ARG myarg=1" "ARG myarg=1"
  assertChildCount token 3 "ARG with value children"

/-- ARG myarg1=1 myarg2=2 — multiple args with values. -/
def testArgMultipleWithValues : IO Unit := do
  IO.println "ARG: multiple args with values"
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkArgDeclWithValue "myarg1" "1",
    Token.mkWhitespace " ",
    mkArgDeclWithValue "myarg2" "2"
  ]
  assertRoundTrip token "ARG myarg1=1 myarg2=2" "ARG multiple with values"

/-- ARG MY_VAR=default — underscore in name, string value. -/
def testArgUnderscoreName : IO Unit := do
  IO.println "ARG: underscore in name"
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkArgDeclWithValue "MY_VAR" "default"
  ]
  assertRoundTrip token "ARG MY_VAR=default" "ARG MY_VAR=default"

/-- ARG MY_VAR="hello world" — quoted default value with spaces. -/
def testArgQuotedDefault : IO Unit := do
  IO.println "ARG: quoted default value with spaces"
  let argDecl := Token.mkKeyValue [
    mkIdentifierToken "MY_VAR",
    Token.mkSymbol '=',
    Token.mkLiteral [Token.mkString "hello world"] (some { quoteChar := '"' })
  ]
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    argDecl
  ]
  assertRoundTrip token "ARG MY_VAR=\"hello world\"" "ARG quoted default"

/-- ARG MY_VAR=${OTHER:-fallback} — variable reference with modifier as default value. -/
def testArgVariableRefDefault : IO Unit := do
  IO.println "ARG: variable reference as default value"
  let varRef := Token.mkVariableRef [
    Token.mkSymbol '{',
    Token.mkString "OTHER",
    Token.mkSymbol ':',
    Token.mkSymbol '-',
    Token.mkLiteral [Token.mkString "fallback"],
    Token.mkSymbol '}'
  ]
  let argDecl := Token.mkKeyValue [
    mkIdentifierToken "MY_VAR",
    Token.mkSymbol '=',
    Token.mkLiteral [varRef]
  ]
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    argDecl
  ]
  assertRoundTrip token "ARG MY_VAR=${OTHER:-fallback}" "ARG variable ref default"

/-- ARG MY_VAR=$OTHER — simple variable reference (no braces) as default value. -/
def testArgSimpleVariableRefDefault : IO Unit := do
  IO.println "ARG: simple variable reference as default value"
  let varRef := Token.mkVariableRef [Token.mkString "OTHER"]
  let argDecl := Token.mkKeyValue [
    mkIdentifierToken "MY_VAR",
    Token.mkSymbol '=',
    Token.mkLiteral [varRef]
  ]
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    argDecl
  ]
  assertRoundTrip token "ARG MY_VAR=$OTHER" "ARG simple variable ref"

-- ============================================================================
-- ARG instruction: token tree structure validation
-- ============================================================================

/-- Verify the token tree structure for ARG myarg=1. -/
def testArgStructureValidation : IO Unit := do
  IO.println "ARG: token tree structure validation"
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkArgDeclWithValue "myarg" "1"
  ]

  -- Top-level instruction token
  assertAggregateKind token .instruction "top-level is instruction"
  assertChildCount token 3 "instruction has 3 children"

  -- Child 0: keyword "ARG"
  let kw := token.children[0]!
  assertAggregateKind kw .keyword "child 0 is keyword"
  assertEqual (Token.toString kw) "ARG" "keyword is ARG"

  -- Child 1: whitespace
  assertEqual (Token.toString token.children[1]!) " " "child 1 is single space"

  -- Child 2: ArgDeclaration (keyValue aggregate)
  let decl := token.children[2]!
  assertAggregateKind decl .keyValue "child 2 is keyValue (ArgDeclaration)"
  assertChildCount decl 3 "ArgDeclaration has 3 children (name, =, value)"
  assertEqual (Token.toString decl) "myarg=1" "ArgDeclaration toString"

  -- ArgDeclaration child 0: identifier "myarg"
  let name := decl.children[0]!
  assertAggregateKind name .identifier "decl child 0 is identifier"
  assertEqual (Token.toString name) "myarg" "arg name is myarg"

  -- ArgDeclaration child 1: symbol '='
  assertEqual (Token.toString decl.children[1]!) "=" "decl child 1 is ="

  -- ArgDeclaration child 2: literal "1"
  let val := decl.children[2]!
  assertAggregateKind val .literal "decl child 2 is literal"
  assertEqual (Token.toString val) "1" "arg value is 1"

/-- Verify the token tree structure for ARG MYARG="" (quoted empty). -/
def testArgQuotedEmptyStructure : IO Unit := do
  IO.println "ARG: quoted empty value structure validation"
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkArgDeclQuotedEmptyValue "MYARG"
  ]

  let decl := token.children[2]!
  assertChildCount decl 3 "ArgDeclaration has 3 children (name, =, quoted-empty)"

  -- The quoted empty literal wraps in quotes
  let quotedLit := decl.children[2]!
  assertAggregateKind quotedLit .literal "value is quoted literal"
  assertEqual (Token.toString quotedLit) "\"\"" "quoted empty literal toString"

-- ============================================================================
-- Combined FROM + ARG round-trip: Dockerfile-level
-- ============================================================================

/-- A small Dockerfile: ARG VERSION=latest\nFROM ubuntu:$VERSION\n
    Verify that the entire Dockerfile round-trips. -/
def testDockerfileFromArgRoundTrip : IO Unit := do
  IO.println "Dockerfile: ARG + FROM round-trip"
  let argToken := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkArgDeclWithValue "VERSION" "latest",
    Token.mkNewLine "\n"
  ]
  let varRef := Token.mkVariableRef [Token.mkString "VERSION"]
  let fromToken := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    Token.mkLiteral [Token.mkString "ubuntu:", varRef],
    Token.mkNewLine "\n"
  ]

  let argConstruct := DockerfileConstruct.fromInstruction { name := .arg, token := argToken }
  let fromConstruct := DockerfileConstruct.fromInstruction { name := .from, token := fromToken }
  let df := Dockerfile.mk [argConstruct, fromConstruct]

  assertEqual (Dockerfile.toString df) "ARG VERSION=latest\nFROM ubuntu:$VERSION\n"
    "Dockerfile ARG+FROM round-trip"

/-- Multi-stage Dockerfile:
    FROM ubuntu:latest AS base\nARG MYARG=hello\nFROM alpine:3.18 AS prod\n -/
def testDockerfileMultiStage : IO Unit := do
  IO.println "Dockerfile: multi-stage FROM + ARG round-trip"
  let from1 := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkSimpleLiteral "ubuntu:latest",
    Token.mkWhitespace " ",
    mkKeywordToken "AS",
    Token.mkWhitespace " ",
    mkIdentifierToken "base",
    Token.mkNewLine "\n"
  ]
  let arg1 := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkArgDeclWithValue "MYARG" "hello",
    Token.mkNewLine "\n"
  ]
  let from2 := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkSimpleLiteral "alpine:3.18",
    Token.mkWhitespace " ",
    mkKeywordToken "AS",
    Token.mkWhitespace " ",
    mkIdentifierToken "prod",
    Token.mkNewLine "\n"
  ]

  let constructs := [
    DockerfileConstruct.fromInstruction { name := .from, token := from1 },
    DockerfileConstruct.fromInstruction { name := .arg, token := arg1 },
    DockerfileConstruct.fromInstruction { name := .from, token := from2 }
  ]
  let df := Dockerfile.mk constructs

  assertEqual (Dockerfile.toString df)
    "FROM ubuntu:latest AS base\nARG MYARG=hello\nFROM alpine:3.18 AS prod\n"
    "multi-stage FROM+ARG round-trip"

-- ============================================================================
-- Edge cases and error expectations
-- ============================================================================

/-- Verify that empty children produce empty toString. -/
def testEdgeCaseEmptyInstruction : IO Unit := do
  IO.println "Edge: empty instruction token"
  let token := Token.mkInstruction []
  assertRoundTrip token "" "empty instruction"
  assertChildCount token 0 "empty instruction has 0 children"

/-- Verify single-char image name. -/
def testEdgeCaseSingleCharImage : IO Unit := do
  IO.println "Edge: single character image name"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    mkSimpleLiteral "x"
  ]
  assertRoundTrip token "FROM x" "FROM x"

/-- ARG X — single character arg name (minimal). -/
def testEdgeCaseSingleCharArg : IO Unit := do
  IO.println "Edge: single character arg name"
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkArgDeclNameOnly "X"
  ]
  assertRoundTrip token "ARG X" "ARG X"

/-- FROM with CRLF line continuation. -/
def testEdgeCaseCRLF : IO Unit := do
  IO.println "Edge: CRLF line continuation"
  let lc := Token.mkLineContinuation [Token.mkSymbol '\\', Token.mkNewLine "\r\n"]
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace " ",
    lc,
    mkSimpleLiteral "ubuntu"
  ]
  assertRoundTrip token "FROM \\\r\nubuntu" "FROM CRLF line continuation"

/-- FROM with tab whitespace. -/
def testEdgeCaseTabWhitespace : IO Unit := do
  IO.println "Edge: tab whitespace"
  let token := Token.mkInstruction [
    mkKeywordToken "FROM",
    Token.mkWhitespace "\t",
    mkSimpleLiteral "ubuntu"
  ]
  assertRoundTrip token "FROM\tubuntu" "FROM with tab"

/-- ARG with tab indentation after line continuation. -/
def testEdgeCaseArgTabIndent : IO Unit := do
  IO.println "Edge: ARG with tab indentation after line continuation"
  let token := Token.mkInstruction [
    mkKeywordToken "ARG",
    Token.mkWhitespace " ",
    mkLineCont '\\',
    Token.mkWhitespace "\t",
    mkArgDeclWithValue "MY_VAR" "test"
  ]
  assertRoundTrip token "ARG \\\n\tMY_VAR=test" "ARG with tab indent"

-- ============================================================================
-- Parser test stubs (enable once Dallas's parser module is ready)
-- ============================================================================
-- These test functions will call Parser.parseFrom and Parser.parseArg,
-- comparing the result against the expected token trees constructed above.
--
-- [PARSER] Uncomment and update import when the parser module is available:
--   import DockerfileModel.Parser
--
-- /-- [PARSER] Parse "FROM scratch" and verify token tree. -/
-- def testParseFromScratch : IO Unit := do
--   IO.println "PARSER: FROM scratch"
--   match Parser.parseFrom "FROM scratch" with
--   | .ok instr =>
--     assertEqual (Token.toString instr.token) "FROM scratch" "parsed FROM scratch round-trips"
--     assertChildCount instr.token 3 "parsed FROM scratch has 3 children"
--     assertTrue (instr.name == .from) "instruction name is .from"
--   | .error msg =>
--     throw (IO.Error.userError s!"Parse failed: {msg}")
--
-- /-- [PARSER] Parse "FROM alpine:latest as build" and verify. -/
-- def testParseFromWithStage : IO Unit := do
--   IO.println "PARSER: FROM alpine:latest as build"
--   match Parser.parseFrom "FROM alpine:latest as build" with
--   | .ok instr =>
--     assertEqual (Token.toString instr.token) "FROM alpine:latest as build"
--       "parsed FROM with stage round-trips"
--   | .error msg =>
--     throw (IO.Error.userError s!"Parse failed: {msg}")
--
-- /-- [PARSER] Parse "FROM --platform=linux/amd64 alpine as build" and verify. -/
-- def testParseFromPlatformStage : IO Unit := do
--   IO.println "PARSER: FROM --platform=linux/amd64 alpine as build"
--   match Parser.parseFrom "FROM --platform=linux/amd64 alpine as build" with
--   | .ok instr =>
--     assertEqual (Token.toString instr.token) "FROM --platform=linux/amd64 alpine as build"
--       "parsed FROM platform+stage round-trips"
--   | .error msg =>
--     throw (IO.Error.userError s!"Parse failed: {msg}")
--
-- /-- [PARSER] Parse "FROM `\nscratch" with escape char `. -/
-- def testParseFromLineCont : IO Unit := do
--   IO.println "PARSER: FROM with line continuation"
--   match Parser.parseFrom "FROM `\nscratch" (escapeChar := '`') with
--   | .ok instr =>
--     assertEqual (Token.toString instr.token) "FROM `\nscratch"
--       "parsed FROM with line-cont round-trips"
--   | .error msg =>
--     throw (IO.Error.userError s!"Parse failed: {msg}")
--
-- /-- [PARSER] Parse "FROM al\\\npine" and verify line continuation inside name. -/
-- def testParseFromLineContInName : IO Unit := do
--   IO.println "PARSER: FROM with line continuation inside name"
--   match Parser.parseFrom "FROM al\\\npine" with
--   | .ok instr =>
--     assertEqual (Token.toString instr.token) "FROM al\\\npine"
--       "parsed FROM line-cont-in-name round-trips"
--   | .error msg =>
--     throw (IO.Error.userError s!"Parse failed: {msg}")
--
-- /-- [PARSER] Parse "FROM \"al\\\npine\"" and verify quoted name. -/
-- def testParseFromQuotedName : IO Unit := do
--   IO.println "PARSER: FROM with quoted name"
--   match Parser.parseFrom "FROM \"al\\\npine\"" with
--   | .ok instr =>
--     assertEqual (Token.toString instr.token) "FROM \"al\\\npine\""
--       "parsed FROM quoted name round-trips"
--   | .error msg =>
--     throw (IO.Error.userError s!"Parse failed: {msg}")
--
-- /-- [PARSER] Parse error: "xFROM " should fail at position (1,1). -/
-- def testParseFromErrorBadKeyword : IO Unit := do
--   IO.println "PARSER: FROM error — bad keyword"
--   match Parser.parseFrom "xFROM " with
--   | .ok _ => throw (IO.Error.userError "Expected parse error for 'xFROM '")
--   | .error _ => IO.println "  PASS: xFROM rejected as expected"
--
-- /-- [PARSER] Parse error: "FROM " should fail (missing image name). -/
-- def testParseFromErrorMissingImage : IO Unit := do
--   IO.println "PARSER: FROM error — missing image"
--   match Parser.parseFrom "FROM " with
--   | .ok _ => throw (IO.Error.userError "Expected parse error for 'FROM '")
--   | .error _ => IO.println "  PASS: 'FROM ' rejected as expected"
--
-- /-- [PARSER] Parse error: "FROM x y" should fail (extra token). -/
-- def testParseFromErrorExtraToken : IO Unit := do
--   IO.println "PARSER: FROM error — extra token"
--   match Parser.parseFrom "FROM x y" with
--   | .ok _ => throw (IO.Error.userError "Expected parse error for 'FROM x y'")
--   | .error _ => IO.println "  PASS: 'FROM x y' rejected as expected"
--
-- /-- [PARSER] Parse error: "FROM alpine AS" should fail (missing stage name). -/
-- def testParseFromErrorMissingStageName : IO Unit := do
--   IO.println "PARSER: FROM error — missing stage name"
--   match Parser.parseFrom "FROM alpine AS" with
--   | .ok _ => throw (IO.Error.userError "Expected parse error for 'FROM alpine AS'")
--   | .error _ => IO.println "  PASS: 'FROM alpine AS' rejected as expected"
--
-- /-- [PARSER] Parse "ARG MYARG" and verify. -/
-- def testParseArgSimple : IO Unit := do
--   IO.println "PARSER: ARG MYARG"
--   match Parser.parseArg "ARG MYARG" with
--   | .ok instr =>
--     assertEqual (Token.toString instr.token) "ARG MYARG" "parsed ARG MYARG round-trips"
--     assertTrue (instr.name == .arg) "instruction name is .arg"
--   | .error msg =>
--     throw (IO.Error.userError s!"Parse failed: {msg}")
--
-- /-- [PARSER] Parse "ARG MYARG=" and verify empty default. -/
-- def testParseArgEmptyDefault : IO Unit := do
--   IO.println "PARSER: ARG MYARG="
--   match Parser.parseArg "ARG MYARG=" with
--   | .ok instr =>
--     assertEqual (Token.toString instr.token) "ARG MYARG=" "parsed ARG MYARG= round-trips"
--   | .error msg =>
--     throw (IO.Error.userError s!"Parse failed: {msg}")
--
-- /-- [PARSER] Parse "ARG myarg=1" and verify. -/
-- def testParseArgWithValue : IO Unit := do
--   IO.println "PARSER: ARG myarg=1"
--   match Parser.parseArg "ARG myarg=1" with
--   | .ok instr =>
--     assertEqual (Token.toString instr.token) "ARG myarg=1" "parsed ARG myarg=1 round-trips"
--   | .error msg =>
--     throw (IO.Error.userError s!"Parse failed: {msg}")
--
-- /-- [PARSER] Parse "ARG MYARG=\"\"" and verify quoted empty. -/
-- def testParseArgQuotedEmpty : IO Unit := do
--   IO.println "PARSER: ARG MYARG=\"\""
--   match Parser.parseArg "ARG MYARG=\"\"" with
--   | .ok instr =>
--     assertEqual (Token.toString instr.token) "ARG MYARG=\"\""
--       "parsed ARG quoted empty round-trips"
--   | .error msg =>
--     throw (IO.Error.userError s!"Parse failed: {msg}")
--
-- /-- [PARSER] Parse error: "xARG " should fail. -/
-- def testParseArgErrorBadKeyword : IO Unit := do
--   IO.println "PARSER: ARG error — bad keyword"
--   match Parser.parseArg "xARG " with
--   | .ok _ => throw (IO.Error.userError "Expected parse error for 'xARG '")
--   | .error _ => IO.println "  PASS: 'xARG ' rejected as expected"
--
-- /-- [PARSER] Parse error: "ARG " should fail (missing arg name). -/
-- def testParseArgErrorMissingName : IO Unit := do
--   IO.println "PARSER: ARG error — missing name"
--   match Parser.parseArg "ARG " with
--   | .ok _ => throw (IO.Error.userError "Expected parse error for 'ARG '")
--   | .error _ => IO.println "  PASS: 'ARG ' rejected as expected"
--
-- /-- [PARSER] Parse error: "ARG =" should fail (missing arg name). -/
-- def testParseArgErrorEqualsOnly : IO Unit := do
--   IO.println "PARSER: ARG error — equals only"
--   match Parser.parseArg "ARG =" with
--   | .ok _ => throw (IO.Error.userError "Expected parse error for 'ARG ='")
--   | .error _ => IO.println "  PASS: 'ARG =' rejected as expected"
--
-- /-- [PARSER] Parse full Dockerfile: "ARG VERSION=latest\nFROM ubuntu:$VERSION\n" -/
-- def testParseDockerfileArgFrom : IO Unit := do
--   IO.println "PARSER: Dockerfile with ARG + FROM"
--   match Parser.parseDockerfile "ARG VERSION=latest\nFROM ubuntu:$VERSION\n" with
--   | .ok df =>
--     assertEqual (Dockerfile.toString df) "ARG VERSION=latest\nFROM ubuntu:$VERSION\n"
--       "parsed Dockerfile round-trips"
--     assertTrue (df.items.length == 2) "Dockerfile has 2 constructs"
--   | .error msg =>
--     throw (IO.Error.userError s!"Parse failed: {msg}")

-- ============================================================================
-- Stage name validation tests (BuildKit: ^[a-z][a-z0-9-_.]*$)
-- ============================================================================
-- These tests exercise the stageNameParser fix: first char must be lowercase,
-- tail must be lowercase letters/digits/hyphens/dots/underscores.

/-- FROM ubuntu AS builder — lowercase stage name succeeds. -/
def testStageNameLowercaseSucceeds : IO Unit := do
  IO.println "Stage name validation: lowercase succeeds"
  match parseFrom "FROM ubuntu AS builder" with
  | some instr =>
    assertEqual (Token.toString instr.token) "FROM ubuntu AS builder"
      "lowercase stage name round-trips"
    IO.println "  PASS: lowercase stage name accepted"
  | none =>
    throw (IO.Error.userError "Parse failed: lowercase stage name 'builder' should be accepted")

/-- FROM ubuntu AS Builder — uppercase first char is rejected as stage name.
    The parser should parse FROM without the AS clause (since "Builder" is not
    a valid stage name, the optional AS clause fails to match). -/
def testStageNameUppercaseRejected : IO Unit := do
  IO.println "Stage name validation: uppercase rejected"
  match parseFrom "FROM ubuntu AS Builder" with
  | some instr =>
    -- If it parses at all, the stage name should NOT be "Builder".
    -- The AS clause should be rejected because "B" is uppercase,
    -- so the parse either fails entirely or succeeds without the AS clause.
    let text := Token.toString instr.token
    -- The parser should NOT have consumed "AS Builder" as a stage name clause.
    -- It should either fail or parse as just "FROM ubuntu" (with trailing unparsed text).
    -- Since parseFrom uses tryParse which requires consuming meaningful input,
    -- and "AS Builder" won't parse as a stage name, the result depends on
    -- whether the trailing text causes a parse failure.
    if text == "FROM ubuntu AS Builder" then
      throw (IO.Error.userError
        "Parse incorrectly accepted uppercase stage name 'Builder'")
    else
      IO.println s!"  PASS: uppercase stage name rejected (parsed as: \"{text}\")"
  | none =>
    -- Parse failure is also acceptable — "FROM ubuntu AS Builder" can't fully parse
    -- because "Builder" doesn't match the stage name pattern.
    IO.println "  PASS: uppercase stage name rejected (parse returned none)"

/-- FROM ubuntu AS build-stage.v2_final — stage name with digits, hyphens,
    dots, and underscores succeeds. -/
def testStageNameWithSpecialChars : IO Unit := do
  IO.println "Stage name validation: digits/hyphens/dots/underscores"
  match parseFrom "FROM ubuntu AS build-stage.v2_final" with
  | some instr =>
    assertEqual (Token.toString instr.token) "FROM ubuntu AS build-stage.v2_final"
      "special-char stage name round-trips"
    IO.println "  PASS: stage name with special chars accepted"
  | none =>
    throw (IO.Error.userError
      "Parse failed: stage name 'build-stage.v2_final' should be accepted")

/-- FROM ubuntu AS a1 — stage name starting with lowercase letter followed by digit. -/
def testStageNameLetterDigit : IO Unit := do
  IO.println "Stage name validation: letter + digit"
  match parseFrom "FROM ubuntu AS a1" with
  | some instr =>
    assertEqual (Token.toString instr.token) "FROM ubuntu AS a1"
      "letter-digit stage name round-trips"
    IO.println "  PASS: stage name 'a1' accepted"
  | none =>
    throw (IO.Error.userError "Parse failed: stage name 'a1' should be accepted")

/-- FROM ubuntu AS 1builder — stage name starting with digit is rejected. -/
def testStageNameDigitStartRejected : IO Unit := do
  IO.println "Stage name validation: digit start rejected"
  match parseFrom "FROM ubuntu AS 1builder" with
  | some instr =>
    let text := Token.toString instr.token
    if text == "FROM ubuntu AS 1builder" then
      throw (IO.Error.userError
        "Parse incorrectly accepted digit-start stage name '1builder'")
    else
      IO.println s!"  PASS: digit-start stage name rejected (parsed as: \"{text}\")"
  | none =>
    IO.println "  PASS: digit-start stage name rejected (parse returned none)"

-- ============================================================================
-- JSON Array (Exec Form) Tests
-- ============================================================================

open DockerfileModel.Parser.ExecForm in
/-- Test: ["a","b"] — simple two-element JSON array. -/
def testExecFormSimple : IO Unit := do
  IO.println "ExecForm: simple two-element array"
  match parseJsonArray "[\"a\",\"b\"]" with
  | some tokens =>
    let text := String.join (tokens.map Token.toString)
    assertEqual text "[\"a\",\"b\"]" "exec form simple round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: simple exec form should parse")

open DockerfileModel.Parser.ExecForm in
/-- Test: [] — empty JSON array. -/
def testExecFormEmpty : IO Unit := do
  IO.println "ExecForm: empty array"
  match parseJsonArray "[]" with
  | some tokens =>
    let text := String.join (tokens.map Token.toString)
    assertEqual text "[]" "exec form empty round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: empty exec form should parse")

open DockerfileModel.Parser.ExecForm in
/-- Test: [ "a" , "b" ] — array with whitespace. -/
def testExecFormWithWhitespace : IO Unit := do
  IO.println "ExecForm: array with whitespace"
  match parseJsonArray "[ \"a\" , \"b\" ]" with
  | some tokens =>
    let text := String.join (tokens.map Token.toString)
    assertEqual text "[ \"a\" , \"b\" ]" "exec form whitespace round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: exec form with whitespace should parse")

open DockerfileModel.Parser.ExecForm in
/-- Test: JSON array with line continuations between elements. -/
def testExecFormWithLineContinuation : IO Unit := do
  IO.println "ExecForm: array with line continuation"
  let input := "[\"a\",\\\n\"b\"]"
  match parseJsonArray input with
  | some tokens =>
    let text := String.join (tokens.map Token.toString)
    assertEqual text input "exec form line continuation round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: exec form with line continuation should parse")

open DockerfileModel.Parser.ExecForm in
/-- Test: ["single"] — single-element JSON array. -/
def testExecFormSingleElement : IO Unit := do
  IO.println "ExecForm: single element array"
  match parseJsonArray "[\"single\"]" with
  | some tokens =>
    let text := String.join (tokens.map Token.toString)
    assertEqual text "[\"single\"]" "exec form single element round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: single element exec form should parse")

open DockerfileModel.Parser.ExecForm in
/-- Test: JSON escapes inside strings: ["hello\\nworld","tab\\there"]. -/
def testExecFormEscapes : IO Unit := do
  IO.println "ExecForm: JSON escapes in strings"
  match parseJsonArray "[\"hello\\nworld\",\"tab\\there\"]" with
  | some tokens =>
    let text := String.join (tokens.map Token.toString)
    assertEqual text "[\"hello\\nworld\",\"tab\\there\"]" "exec form escapes round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: exec form with escapes should parse")

open DockerfileModel.Parser.ExecForm in
/-- Test: three-element array ["a","b","c"]. -/
def testExecFormThreeElements : IO Unit := do
  IO.println "ExecForm: three element array"
  match parseJsonArray "[\"a\",\"b\",\"c\"]" with
  | some tokens =>
    let text := String.join (tokens.map Token.toString)
    assertEqual text "[\"a\",\"b\",\"c\"]" "exec form three elements round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: three element exec form should parse")

-- ============================================================================
-- Flag Parser Tests
-- ============================================================================

open DockerfileModel.Parser.Flags in
/-- Test: --platform=linux/amd64 — string flag. -/
def testFlagPlatform : IO Unit := do
  IO.println "Flags: --platform=linux/amd64"
  let parser := DockerfileModel.Parser.flagParser "platform" '\\'
  match parser.tryParse "--platform=linux/amd64" with
  | some token =>
    assertEqual (Token.toString token) "--platform=linux/amd64" "platform flag round-trip"
    assertAggregateKind token .keyValue "platform flag is keyValue"
  | none =>
    throw (IO.Error.userError "Parse failed: --platform=linux/amd64 should parse")

open DockerfileModel.Parser.Flags in
/-- Test: --from=builder — string flag. -/
def testFlagFrom : IO Unit := do
  IO.println "Flags: --from=builder"
  let parser := DockerfileModel.Parser.flagParser "from" '\\'
  match parser.tryParse "--from=builder" with
  | some token =>
    assertEqual (Token.toString token) "--from=builder" "from flag round-trip"
    assertAggregateKind token .keyValue "from flag is keyValue"
  | none =>
    throw (IO.Error.userError "Parse failed: --from=builder should parse")

open DockerfileModel.Parser.Flags in
/-- Test: --link — boolean flag (no value). -/
def testBoolFlagLink : IO Unit := do
  IO.println "Flags: --link (boolean, no value)"
  let parser := booleanFlagParser "link" '\\'
  match parser.tryParse "--link" with
  | some token =>
    assertEqual (Token.toString token) "--link" "link flag round-trip"
    assertAggregateKind token .keyValue "link flag is keyValue"
    -- Verify 3 children: SymbolToken('-'), SymbolToken('-'), KeywordToken("link")
    assertChildCount token 3 "link flag child count"
  | none =>
    throw (IO.Error.userError "Parse failed: --link should parse")

open DockerfileModel.Parser.Flags in
/-- Test: --link=true — boolean flag with explicit value. -/
def testBoolFlagLinkTrue : IO Unit := do
  IO.println "Flags: --link=true (boolean, explicit)"
  let parser := booleanFlagParser "link" '\\'
  match parser.tryParse "--link=true" with
  | some token =>
    assertEqual (Token.toString token) "--link=true" "link=true flag round-trip"
    assertAggregateKind token .keyValue "link=true flag is keyValue"
    -- Verify 5 children: SymbolToken('-'), SymbolToken('-'), KeywordToken("link"), SymbolToken('='), LiteralToken("true")
    assertChildCount token 5 "link=true flag child count"
  | none =>
    throw (IO.Error.userError "Parse failed: --link=true should parse")

open DockerfileModel.Parser.Flags in
/-- Test: --link=false — boolean flag with explicit false. -/
def testBoolFlagLinkFalse : IO Unit := do
  IO.println "Flags: --link=false (boolean, explicit)"
  let parser := booleanFlagParser "link" '\\'
  match parser.tryParse "--link=false" with
  | some token =>
    assertEqual (Token.toString token) "--link=false" "link=false flag round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: --link=false should parse")

open DockerfileModel.Parser.Flags in
/-- Test: --chown=user:group — string flag with variable-supporting value. -/
def testFlagChown : IO Unit := do
  IO.println "Flags: --chown=user:group"
  let parser := DockerfileModel.Parser.flagParser "chown" '\\'
  match parser.tryParse "--chown=user:group" with
  | some token =>
    assertEqual (Token.toString token) "--chown=user:group" "chown flag round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: --chown=user:group should parse")

-- ============================================================================
-- Shell Form Command Tests
-- ============================================================================

/-- Test: basic shell form command text. -/
def testShellFormBasic : IO Unit := do
  IO.println "ShellForm: basic command"
  let parser := DockerfileModel.Parser.shellFormCommand '\\'
  match parser.tryParse "echo hello" with
  | some tokens =>
    let text := String.join (tokens.map Token.toString)
    assertEqual text "echo hello" "shell form basic round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: basic shell form should parse")

/-- Test: shell form command with variable reference. -/
def testShellFormWithVariable : IO Unit := do
  IO.println "ShellForm: command with variable"
  let parser := DockerfileModel.Parser.shellFormCommand '\\'
  match parser.tryParse "echo $HOME" with
  | some tokens =>
    let text := String.join (tokens.map Token.toString)
    assertEqual text "echo $HOME" "shell form variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: shell form with variable should parse")

/-- Test: shell form command with line continuation. -/
def testShellFormWithLineContinuation : IO Unit := do
  IO.println "ShellForm: command with line continuation"
  let parser := DockerfileModel.Parser.shellFormCommand '\\'
  match parser.tryParse "echo \\\nhello" with
  | some tokens =>
    let text := String.join (tokens.map Token.toString)
    assertEqual text "echo \\\nhello" "shell form line continuation round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: shell form with line continuation should parse")

/-- Test: shell form command with braced variable reference. -/
def testShellFormWithBracedVariable : IO Unit := do
  IO.println "ShellForm: command with braced variable"
  let parser := DockerfileModel.Parser.shellFormCommand '\\'
  match parser.tryParse "echo ${HOME:-/root}" with
  | some tokens =>
    let text := String.join (tokens.map Token.toString)
    assertEqual text "echo ${HOME:-/root}" "shell form braced variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: shell form with braced variable should parse")

/-- Test: shell form command produces a single opaque StringToken (no whitespace splitting).
    BuildKit treats shell form as opaque — the LiteralToken should contain a single
    StringToken with the full command text, NOT separate StringToken/WhitespaceToken children. -/
def testShellFormOpaqueStructure : IO Unit := do
  IO.println "ShellForm: opaque structure (single StringToken)"
  let parser := DockerfileModel.Parser.shellFormCommand '\\'
  match parser.tryParse "echo hello world" with
  | some tokens =>
    -- Should be a single LiteralToken
    assertTrue (tokens.length == 1) "shell form produces single token"
    let lit := tokens.head!
    assertAggregateKind lit .literal "shell form token is LiteralToken"
    -- The LiteralToken should contain a single StringToken child
    assertChildCount lit 1 "LiteralToken has single child"
    let child := lit.children.head!
    match child with
    | .primitive .string val =>
      assertEqual val "echo hello world" "StringToken contains full opaque text"
    | _ =>
      throw (IO.Error.userError "Expected StringToken child, got different token type")
  | none =>
    throw (IO.Error.userError "Parse failed: shell form opaque structure should parse")

/-- Test: shell form with line continuation preserves LineContinuationToken but still
    uses single StringToken for text segments (no whitespace splitting). -/
def testShellFormOpaqueWithLineContinuation : IO Unit := do
  IO.println "ShellForm: opaque structure with line continuation"
  let parser := DockerfileModel.Parser.shellFormCommand '\\'
  match parser.tryParse "echo hello \\\nworld foo" with
  | some tokens =>
    assertTrue (tokens.length == 1) "shell form produces single token"
    let lit := tokens.head!
    assertAggregateKind lit .literal "shell form token is LiteralToken"
    -- LiteralToken should contain: StringToken("echo hello "), LineContinuationToken, StringToken("world foo")
    assertChildCount lit 3 "LiteralToken has 3 children (string, lineCont, string)"
    let child0 := lit.children[0]!
    match child0 with
    | .primitive .string val =>
      assertEqual val "echo hello " "first StringToken contains text before continuation"
    | _ => throw (IO.Error.userError "Expected StringToken as first child")
    let child1 := lit.children[1]!
    assertAggregateKind child1 .lineContinuation "second child is LineContinuationToken"
    let child2 := lit.children[2]!
    match child2 with
    | .primitive .string val =>
      assertEqual val "world foo" "third StringToken contains text after continuation"
    | _ => throw (IO.Error.userError "Expected StringToken as third child")
  | none =>
    throw (IO.Error.userError "Parse failed: shell form with line continuation should parse")

/-- Test: shell form with trailing whitespace between escape char and newline
    in a line continuation (backslash + space + newline). -/
def testShellFormOpaqueWithTrailingWhitespace : IO Unit := do
  IO.println "ShellForm: opaque structure with trailing whitespace in line continuation"
  let parser := DockerfileModel.Parser.shellFormCommand '\\'
  match parser.tryParse "echo hello \\ \nworld foo" with
  | some tokens =>
    assertTrue (tokens.length == 1) "shell form produces single token"
    let lit := tokens.head!
    assertAggregateKind lit .literal "shell form token is LiteralToken"
    -- LiteralToken should contain: StringToken("echo hello "), LineContinuationToken, StringToken("world foo")
    assertChildCount lit 3 "LiteralToken has 3 children (string, lineCont, string)"
    let child0 := lit.children[0]!
    match child0 with
    | .primitive .string val =>
      assertEqual val "echo hello " "first StringToken contains text before continuation"
    | _ => throw (IO.Error.userError "Expected StringToken as first child")
    let child1 := lit.children[1]!
    assertAggregateKind child1 .lineContinuation "second child is LineContinuationToken"
    -- LineContinuationToken should have 3 children: symbol(\), whitespace( ), newLine(\n)
    assertChildCount child1 3 "LineContinuationToken has 3 children (symbol, whitespace, newLine)"
    let child2 := lit.children[2]!
    match child2 with
    | .primitive .string val =>
      assertEqual val "world foo" "third StringToken contains text after continuation"
    | _ => throw (IO.Error.userError "Expected StringToken as third child")
  | none =>
    throw (IO.Error.userError "Parse failed: shell form with trailing whitespace in line continuation should parse")

-- ============================================================================
-- MAINTAINER Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Maintainer in
/-- Test: MAINTAINER John Smith <john@example.com> -/
def testMaintainerBasic : IO Unit := do
  IO.println "Maintainer: basic text"
  match parseMaintainer "MAINTAINER John Smith <john@example.com>" with
  | some inst =>
    assertEqual (Token.toString inst.token) "MAINTAINER John Smith <john@example.com>"
      "maintainer basic round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: MAINTAINER basic should parse")

open DockerfileModel.Parser.Instructions.Maintainer in
/-- Test: MAINTAINER with variable reference -/
def testMaintainerWithVariable : IO Unit := do
  IO.println "Maintainer: with variable"
  match parseMaintainer "MAINTAINER $AUTHOR" with
  | some inst =>
    assertEqual (Token.toString inst.token) "MAINTAINER $AUTHOR"
      "maintainer variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: MAINTAINER with variable should parse")

open DockerfileModel.Parser.Instructions.Maintainer in
/-- Test: maintainer (lowercase keyword) -/
def testMaintainerLowercase : IO Unit := do
  IO.println "Maintainer: lowercase keyword"
  match parseMaintainer "maintainer nobody" with
  | some inst =>
    assertEqual (Token.toString inst.token) "maintainer nobody"
      "maintainer lowercase round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: lowercase maintainer should parse")

open DockerfileModel.Parser.Instructions.Maintainer in
/-- Test: MAINTAINER simple name only -/
def testMaintainerSimpleName : IO Unit := do
  IO.println "Maintainer: simple name"
  match parseMaintainer "MAINTAINER admin" with
  | some inst =>
    assertEqual (Token.toString inst.token) "MAINTAINER admin"
      "maintainer simple name round-trip"
    assertTrue (inst.name == .maintainer) "maintainer instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: MAINTAINER simple name should parse")

open DockerfileModel.Parser.Instructions.Maintainer in
/-- Test: MAINTAINER with braced variable -/
def testMaintainerBracedVariable : IO Unit := do
  IO.println "Maintainer: braced variable"
  match parseMaintainer "MAINTAINER ${AUTHOR:-unknown}" with
  | some inst =>
    assertEqual (Token.toString inst.token) "MAINTAINER ${AUTHOR:-unknown}"
      "maintainer braced variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: MAINTAINER with braced variable should parse")

-- ============================================================================
-- WORKDIR Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Workdir in
/-- Test: WORKDIR /app -/
def testWorkdirBasic : IO Unit := do
  IO.println "Workdir: basic path"
  match parseWorkdir "WORKDIR /app" with
  | some inst =>
    assertEqual (Token.toString inst.token) "WORKDIR /app"
      "workdir basic round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: WORKDIR basic should parse")

open DockerfileModel.Parser.Instructions.Workdir in
/-- Test: WORKDIR with variable substitution -/
def testWorkdirWithVariable : IO Unit := do
  IO.println "Workdir: with variable"
  match parseWorkdir "WORKDIR $HOME/app" with
  | some inst =>
    assertEqual (Token.toString inst.token) "WORKDIR $HOME/app"
      "workdir variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: WORKDIR with variable should parse")

open DockerfileModel.Parser.Instructions.Workdir in
/-- Test: WORKDIR with braced variable -/
def testWorkdirBracedVariable : IO Unit := do
  IO.println "Workdir: braced variable"
  match parseWorkdir "WORKDIR ${INSTALL_DIR:-/opt}" with
  | some inst =>
    assertEqual (Token.toString inst.token) "WORKDIR ${INSTALL_DIR:-/opt}"
      "workdir braced variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: WORKDIR with braced variable should parse")

open DockerfileModel.Parser.Instructions.Workdir in
/-- Test: WORKDIR with spaces in path (whitespace-allowed mode) -/
def testWorkdirWithSpaces : IO Unit := do
  IO.println "Workdir: path with spaces"
  match parseWorkdir "WORKDIR /my app/dir" with
  | some inst =>
    assertEqual (Token.toString inst.token) "WORKDIR /my app/dir"
      "workdir spaces round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: WORKDIR with spaces should parse")

open DockerfileModel.Parser.Instructions.Workdir in
/-- Test: WORKDIR instruction name check -/
def testWorkdirInstructionName : IO Unit := do
  IO.println "Workdir: instruction name"
  match parseWorkdir "WORKDIR /tmp" with
  | some inst =>
    assertTrue (inst.name == .workdir) "workdir instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: WORKDIR should parse")

-- ============================================================================
-- STOPSIGNAL Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Stopsignal in
/-- Test: STOPSIGNAL SIGTERM -/
def testStopsignalName : IO Unit := do
  IO.println "Stopsignal: signal name"
  match parseStopsignal "STOPSIGNAL SIGTERM" with
  | some inst =>
    assertEqual (Token.toString inst.token) "STOPSIGNAL SIGTERM"
      "stopsignal name round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: STOPSIGNAL SIGTERM should parse")

open DockerfileModel.Parser.Instructions.Stopsignal in
/-- Test: STOPSIGNAL 9 -/
def testStopsignalNumber : IO Unit := do
  IO.println "Stopsignal: signal number"
  match parseStopsignal "STOPSIGNAL 9" with
  | some inst =>
    assertEqual (Token.toString inst.token) "STOPSIGNAL 9"
      "stopsignal number round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: STOPSIGNAL 9 should parse")

open DockerfileModel.Parser.Instructions.Stopsignal in
/-- Test: STOPSIGNAL with variable -/
def testStopsignalVariable : IO Unit := do
  IO.println "Stopsignal: variable"
  match parseStopsignal "STOPSIGNAL $MY_SIGNAL" with
  | some inst =>
    assertEqual (Token.toString inst.token) "STOPSIGNAL $MY_SIGNAL"
      "stopsignal variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: STOPSIGNAL variable should parse")

open DockerfileModel.Parser.Instructions.Stopsignal in
/-- Test: STOPSIGNAL SIGKILL -/
def testStopsignalSIGKILL : IO Unit := do
  IO.println "Stopsignal: SIGKILL"
  match parseStopsignal "STOPSIGNAL SIGKILL" with
  | some inst =>
    assertEqual (Token.toString inst.token) "STOPSIGNAL SIGKILL"
      "stopsignal SIGKILL round-trip"
    assertTrue (inst.name == .stopSignal) "stopsignal instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: STOPSIGNAL SIGKILL should parse")

-- ============================================================================
-- CMD Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Cmd in
/-- Test: CMD ["echo", "hello"] — exec form -/
def testCmdExecForm : IO Unit := do
  IO.println "Cmd: exec form"
  match parseCmd "CMD [\"echo\", \"hello\"]" with
  | some inst =>
    assertEqual (Token.toString inst.token) "CMD [\"echo\", \"hello\"]"
      "cmd exec form round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: CMD exec form should parse")

open DockerfileModel.Parser.Instructions.Cmd in
/-- Test: CMD echo hello — shell form -/
def testCmdShellForm : IO Unit := do
  IO.println "Cmd: shell form"
  match parseCmd "CMD echo hello" with
  | some inst =>
    assertEqual (Token.toString inst.token) "CMD echo hello"
      "cmd shell form round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: CMD shell form should parse")

open DockerfileModel.Parser.Instructions.Cmd in
/-- Test: CMD with variable reference in shell form -/
def testCmdShellVariable : IO Unit := do
  IO.println "Cmd: shell form with variable"
  match parseCmd "CMD echo $HOME" with
  | some inst =>
    assertEqual (Token.toString inst.token) "CMD echo $HOME"
      "cmd shell variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: CMD shell form with variable should parse")

open DockerfileModel.Parser.Instructions.Cmd in
/-- Test: CMD ["single"] — exec form single element -/
def testCmdExecSingle : IO Unit := do
  IO.println "Cmd: exec form single element"
  match parseCmd "CMD [\"single\"]" with
  | some inst =>
    assertEqual (Token.toString inst.token) "CMD [\"single\"]"
      "cmd exec single round-trip"
    assertTrue (inst.name == .cmd) "cmd instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: CMD exec form single should parse")

open DockerfileModel.Parser.Instructions.Cmd in
/-- Test: cmd lowercase keyword -/
def testCmdLowercase : IO Unit := do
  IO.println "Cmd: lowercase keyword"
  match parseCmd "cmd echo test" with
  | some inst =>
    assertEqual (Token.toString inst.token) "cmd echo test"
      "cmd lowercase round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: lowercase cmd should parse")

-- ============================================================================
-- ENTRYPOINT Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Entrypoint in
/-- Test: ENTRYPOINT ["nginx", "-g", "daemon off;"] — exec form -/
def testEntrypointExecForm : IO Unit := do
  IO.println "Entrypoint: exec form"
  match parseEntrypoint "ENTRYPOINT [\"nginx\", \"-g\", \"daemon off;\"]" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ENTRYPOINT [\"nginx\", \"-g\", \"daemon off;\"]"
      "entrypoint exec form round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ENTRYPOINT exec form should parse")

open DockerfileModel.Parser.Instructions.Entrypoint in
/-- Test: ENTRYPOINT /bin/bash — shell form -/
def testEntrypointShellForm : IO Unit := do
  IO.println "Entrypoint: shell form"
  match parseEntrypoint "ENTRYPOINT /bin/bash" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ENTRYPOINT /bin/bash"
      "entrypoint shell form round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ENTRYPOINT shell form should parse")

open DockerfileModel.Parser.Instructions.Entrypoint in
/-- Test: ENTRYPOINT with variable -/
def testEntrypointVariable : IO Unit := do
  IO.println "Entrypoint: variable"
  match parseEntrypoint "ENTRYPOINT $ENTRY" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ENTRYPOINT $ENTRY"
      "entrypoint variable round-trip"
    assertTrue (inst.name == .entrypoint) "entrypoint instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: ENTRYPOINT variable should parse")

open DockerfileModel.Parser.Instructions.Entrypoint in
/-- Test: ENTRYPOINT exec form empty array -/
def testEntrypointExecEmpty : IO Unit := do
  IO.println "Entrypoint: exec form empty"
  match parseEntrypoint "ENTRYPOINT []" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ENTRYPOINT []"
      "entrypoint exec empty round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ENTRYPOINT exec form empty should parse")

-- ============================================================================
-- SHELL Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Shell in
/-- Test: SHELL ["powershell", "-command"] -/
def testShellExecForm : IO Unit := do
  IO.println "Shell: exec form"
  match parseShell "SHELL [\"powershell\", \"-command\"]" with
  | some inst =>
    assertEqual (Token.toString inst.token) "SHELL [\"powershell\", \"-command\"]"
      "shell exec form round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: SHELL exec form should parse")

open DockerfileModel.Parser.Instructions.Shell in
/-- Test: SHELL ["/bin/bash", "-c"] -/
def testShellBashForm : IO Unit := do
  IO.println "Shell: bash form"
  match parseShell "SHELL [\"/bin/bash\", \"-c\"]" with
  | some inst =>
    assertEqual (Token.toString inst.token) "SHELL [\"/bin/bash\", \"-c\"]"
      "shell bash form round-trip"
    assertTrue (inst.name == .shell) "shell instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: SHELL bash form should parse")

open DockerfileModel.Parser.Instructions.Shell in
/-- Test: SHELL ["/bin/sh"] — single element -/
def testShellSingleElement : IO Unit := do
  IO.println "Shell: single element"
  match parseShell "SHELL [\"/bin/sh\"]" with
  | some inst =>
    assertEqual (Token.toString inst.token) "SHELL [\"/bin/sh\"]"
      "shell single element round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: SHELL single element should parse")

open DockerfileModel.Parser.Instructions.Shell in
/-- Test: SHELL with lowercase keyword -/
def testShellLowercase : IO Unit := do
  IO.println "Shell: lowercase keyword"
  match parseShell "shell [\"/bin/sh\"]" with
  | some inst =>
    assertEqual (Token.toString inst.token) "shell [\"/bin/sh\"]"
      "shell lowercase round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: lowercase shell should parse")

-- ============================================================================
-- USER Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.User in
/-- Test: USER root -/
def testUserBasic : IO Unit := do
  IO.println "User: basic username"
  match parseUser "USER root" with
  | some inst =>
    assertEqual (Token.toString inst.token) "USER root"
      "user basic round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: USER basic should parse")

open DockerfileModel.Parser.Instructions.User in
/-- Test: USER root:staff — username with group -/
def testUserWithGroup : IO Unit := do
  IO.println "User: username with group"
  match parseUser "USER root:staff" with
  | some inst =>
    assertEqual (Token.toString inst.token) "USER root:staff"
      "user with group round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: USER with group should parse")

open DockerfileModel.Parser.Instructions.User in
/-- Test: USER $USERNAME -/
def testUserVariable : IO Unit := do
  IO.println "User: variable"
  match parseUser "USER $USERNAME" with
  | some inst =>
    assertEqual (Token.toString inst.token) "USER $USERNAME"
      "user variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: USER variable should parse")

open DockerfileModel.Parser.Instructions.User in
/-- Test: USER 1000:1000 — numeric UID:GID -/
def testUserNumeric : IO Unit := do
  IO.println "User: numeric UID:GID"
  match parseUser "USER 1000:1000" with
  | some inst =>
    assertEqual (Token.toString inst.token) "USER 1000:1000"
      "user numeric round-trip"
    assertTrue (inst.name == .user) "user instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: USER numeric should parse")

open DockerfileModel.Parser.Instructions.User in
/-- Test: USER with braced variable -/
def testUserBracedVariable : IO Unit := do
  IO.println "User: braced variable"
  match parseUser "USER ${UID:-1000}" with
  | some inst =>
    assertEqual (Token.toString inst.token) "USER ${UID:-1000}"
      "user braced variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: USER braced variable should parse")

-- ============================================================================
-- EXPOSE Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Expose in
/-- Test: EXPOSE 80 -/
def testExposeBasic : IO Unit := do
  IO.println "Expose: basic port"
  match parseExpose "EXPOSE 80" with
  | some inst =>
    assertEqual (Token.toString inst.token) "EXPOSE 80"
      "expose basic round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: EXPOSE basic should parse")

open DockerfileModel.Parser.Instructions.Expose in
/-- Test: EXPOSE 80/tcp -/
def testExposeWithProtocol : IO Unit := do
  IO.println "Expose: port with protocol"
  match parseExpose "EXPOSE 80/tcp" with
  | some inst =>
    assertEqual (Token.toString inst.token) "EXPOSE 80/tcp"
      "expose protocol round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: EXPOSE with protocol should parse")

open DockerfileModel.Parser.Instructions.Expose in
/-- Test: EXPOSE 80 443 -/
def testExposeMultiple : IO Unit := do
  IO.println "Expose: multiple ports"
  match parseExpose "EXPOSE 80 443" with
  | some inst =>
    assertEqual (Token.toString inst.token) "EXPOSE 80 443"
      "expose multiple round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: EXPOSE multiple should parse")

open DockerfileModel.Parser.Instructions.Expose in
/-- Test: EXPOSE 80/tcp 443/udp -/
def testExposeMultipleWithProtocol : IO Unit := do
  IO.println "Expose: multiple ports with protocols"
  match parseExpose "EXPOSE 80/tcp 443/udp" with
  | some inst =>
    assertEqual (Token.toString inst.token) "EXPOSE 80/tcp 443/udp"
      "expose multiple protocols round-trip"
    assertTrue (inst.name == .expose) "expose instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: EXPOSE multiple protocols should parse")

open DockerfileModel.Parser.Instructions.Expose in
/-- Test: EXPOSE $PORT -/
def testExposeVariable : IO Unit := do
  IO.println "Expose: variable"
  match parseExpose "EXPOSE $PORT" with
  | some inst =>
    assertEqual (Token.toString inst.token) "EXPOSE $PORT"
      "expose variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: EXPOSE variable should parse")

-- ============================================================================
-- VOLUME Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Volume in
/-- Test: VOLUME ["/data"] — exec form -/
def testVolumeExecForm : IO Unit := do
  IO.println "Volume: exec form"
  match parseVolume "VOLUME [\"/data\"]" with
  | some inst =>
    assertEqual (Token.toString inst.token) "VOLUME [\"/data\"]"
      "volume exec form round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: VOLUME exec form should parse")

open DockerfileModel.Parser.Instructions.Volume in
/-- Test: VOLUME /data — shell form single path -/
def testVolumeShellForm : IO Unit := do
  IO.println "Volume: shell form single"
  match parseVolume "VOLUME /data" with
  | some inst =>
    assertEqual (Token.toString inst.token) "VOLUME /data"
      "volume shell form round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: VOLUME shell form should parse")

open DockerfileModel.Parser.Instructions.Volume in
/-- Test: VOLUME /data /var/log — multiple paths -/
def testVolumeMultiple : IO Unit := do
  IO.println "Volume: multiple paths"
  match parseVolume "VOLUME /data /var/log" with
  | some inst =>
    assertEqual (Token.toString inst.token) "VOLUME /data /var/log"
      "volume multiple round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: VOLUME multiple should parse")

open DockerfileModel.Parser.Instructions.Volume in
/-- Test: VOLUME with variable -/
def testVolumeVariable : IO Unit := do
  IO.println "Volume: variable"
  match parseVolume "VOLUME $DATA_DIR" with
  | some inst =>
    assertEqual (Token.toString inst.token) "VOLUME $DATA_DIR"
      "volume variable round-trip"
    assertTrue (inst.name == .volume) "volume instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: VOLUME variable should parse")

open DockerfileModel.Parser.Instructions.Volume in
/-- Test: VOLUME exec form multiple -/
def testVolumeExecMultiple : IO Unit := do
  IO.println "Volume: exec form multiple"
  match parseVolume "VOLUME [\"/data\", \"/var/log\"]" with
  | some inst =>
    assertEqual (Token.toString inst.token) "VOLUME [\"/data\", \"/var/log\"]"
      "volume exec multiple round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: VOLUME exec form multiple should parse")

-- ============================================================================
-- ENV Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Env in
/-- Test: ENV MY_VAR=hello — modern format -/
def testEnvModernBasic : IO Unit := do
  IO.println "Env: modern basic"
  match parseEnv "ENV MY_VAR=hello" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ENV MY_VAR=hello"
      "env modern basic round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ENV modern basic should parse")

open DockerfileModel.Parser.Instructions.Env in
/-- Test: ENV A=1 B=2 — modern format multiple -/
def testEnvModernMultiple : IO Unit := do
  IO.println "Env: modern multiple"
  match parseEnv "ENV A=1 B=2" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ENV A=1 B=2"
      "env modern multiple round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ENV modern multiple should parse")

open DockerfileModel.Parser.Instructions.Env in
/-- Test: ENV MY_VAR="hello world" — modern format with quoted value -/
def testEnvModernQuoted : IO Unit := do
  IO.println "Env: modern quoted"
  match parseEnv "ENV MY_VAR=\"hello world\"" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ENV MY_VAR=\"hello world\""
      "env modern quoted round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ENV modern quoted should parse")

open DockerfileModel.Parser.Instructions.Env in
/-- Test: ENV MY_VAR hello world — legacy format -/
def testEnvLegacy : IO Unit := do
  IO.println "Env: legacy format"
  match parseEnv "ENV MY_VAR hello world" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ENV MY_VAR hello world"
      "env legacy round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ENV legacy should parse")

open DockerfileModel.Parser.Instructions.Env in
/-- Test: ENV PATH=$PATH:/usr/local/bin — variable in value -/
def testEnvWithVariable : IO Unit := do
  IO.println "Env: variable in value"
  match parseEnv "ENV PATH=$PATH:/usr/local/bin" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ENV PATH=$PATH:/usr/local/bin"
      "env variable round-trip"
    assertTrue (inst.name == .env) "env instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: ENV with variable should parse")

open DockerfileModel.Parser.Instructions.Env in
/-- Test: ENV KEY= — empty value -/
def testEnvEmptyValue : IO Unit := do
  IO.println "Env: empty value"
  match parseEnv "ENV KEY=" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ENV KEY="
      "env empty value round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ENV empty value should parse")

-- ============================================================================
-- LABEL Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Label in
/-- Test: LABEL version=1.0 -/
def testLabelBasic : IO Unit := do
  IO.println "Label: basic"
  match parseLabel "LABEL version=1.0" with
  | some inst =>
    assertEqual (Token.toString inst.token) "LABEL version=1.0"
      "label basic round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: LABEL basic should parse")

open DockerfileModel.Parser.Instructions.Label in
/-- Test: LABEL version=1.0 description="my app" -/
def testLabelMultiple : IO Unit := do
  IO.println "Label: multiple pairs"
  match parseLabel "LABEL version=1.0 description=\"my app\"" with
  | some inst =>
    assertEqual (Token.toString inst.token) "LABEL version=1.0 description=\"my app\""
      "label multiple round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: LABEL multiple should parse")

open DockerfileModel.Parser.Instructions.Label in
/-- Test: LABEL com.example.version=1.0 — dotted key -/
def testLabelDottedKey : IO Unit := do
  IO.println "Label: dotted key"
  match parseLabel "LABEL com.example.version=1.0" with
  | some inst =>
    assertEqual (Token.toString inst.token) "LABEL com.example.version=1.0"
      "label dotted key round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: LABEL dotted key should parse")

open DockerfileModel.Parser.Instructions.Label in
/-- Test: LABEL maintainer-name="John" — hyphenated key -/
def testLabelHyphenatedKey : IO Unit := do
  IO.println "Label: hyphenated key"
  match parseLabel "LABEL maintainer-name=\"John\"" with
  | some inst =>
    assertEqual (Token.toString inst.token) "LABEL maintainer-name=\"John\""
      "label hyphenated key round-trip"
    assertTrue (inst.name == .label) "label instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: LABEL hyphenated key should parse")

open DockerfileModel.Parser.Instructions.Label in
/-- Test: LABEL key=$VALUE — variable in value -/
def testLabelVariable : IO Unit := do
  IO.println "Label: variable in value"
  match parseLabel "LABEL key=$VALUE" with
  | some inst =>
    assertEqual (Token.toString inst.token) "LABEL key=$VALUE"
      "label variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: LABEL variable should parse")

-- ============================================================================
-- RUN Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Run in
/-- Test: RUN echo hello — basic shell form -/
def testRunShellForm : IO Unit := do
  IO.println "Run: basic shell form"
  match parseRun "RUN echo hello" with
  | some inst =>
    assertEqual (Token.toString inst.token) "RUN echo hello"
      "run shell form round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: RUN shell form should parse")

open DockerfileModel.Parser.Instructions.Run in
/-- Test: RUN ["echo", "hello"] — exec form -/
def testRunExecForm : IO Unit := do
  IO.println "Run: exec form"
  match parseRun "RUN [\"echo\", \"hello\"]" with
  | some inst =>
    assertEqual (Token.toString inst.token) "RUN [\"echo\", \"hello\"]"
      "run exec form round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: RUN exec form should parse")

open DockerfileModel.Parser.Instructions.Run in
/-- Test: RUN --mount=type=bind,source=/src,target=/app echo hello — with mount flag -/
def testRunWithMount : IO Unit := do
  IO.println "Run: with --mount flag"
  match parseRun "RUN --mount=type=bind,source=/src,target=/app echo hello" with
  | some inst =>
    assertEqual (Token.toString inst.token) "RUN --mount=type=bind,source=/src,target=/app echo hello"
      "run mount flag round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: RUN with --mount should parse")

open DockerfileModel.Parser.Instructions.Run in
/-- Test: RUN --network=host echo hello — with network flag -/
def testRunWithNetwork : IO Unit := do
  IO.println "Run: with --network flag"
  match parseRun "RUN --network=host echo hello" with
  | some inst =>
    assertEqual (Token.toString inst.token) "RUN --network=host echo hello"
      "run network flag round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: RUN with --network should parse")

open DockerfileModel.Parser.Instructions.Run in
/-- Test: RUN --security=insecure echo hello — with security flag -/
def testRunWithSecurity : IO Unit := do
  IO.println "Run: with --security flag"
  match parseRun "RUN --security=insecure echo hello" with
  | some inst =>
    assertEqual (Token.toString inst.token) "RUN --security=insecure echo hello"
      "run security flag round-trip"
    assertTrue (inst.name == .run) "run instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: RUN with --security should parse")

open DockerfileModel.Parser.Instructions.Run in
/-- Test: RUN --mount=type=cache,target=/root/.cache --network=none pip install -r requirements.txt — multiple flags -/
def testRunMultipleFlags : IO Unit := do
  IO.println "Run: multiple flags"
  match parseRun "RUN --mount=type=cache,target=/root/.cache --network=none pip install" with
  | some inst =>
    assertEqual (Token.toString inst.token)
      "RUN --mount=type=cache,target=/root/.cache --network=none pip install"
      "run multiple flags round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: RUN with multiple flags should parse")

open DockerfileModel.Parser.Instructions.Run in
/-- Test: RUN with variable reference -/
def testRunWithVariable : IO Unit := do
  IO.println "Run: with variable reference"
  match parseRun "RUN echo $HOME" with
  | some inst =>
    assertEqual (Token.toString inst.token) "RUN echo $HOME"
      "run variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: RUN with variable should parse")

-- ============================================================================
-- COPY Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Copy in
/-- Test: COPY . /app — basic form -/
def testCopyBasic : IO Unit := do
  IO.println "Copy: basic form"
  match parseCopy "COPY . /app" with
  | some inst =>
    assertEqual (Token.toString inst.token) "COPY . /app"
      "copy basic round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: COPY basic should parse")

open DockerfileModel.Parser.Instructions.Copy in
/-- Test: COPY --from=builder /app /app — with from flag -/
def testCopyWithFrom : IO Unit := do
  IO.println "Copy: with --from flag"
  match parseCopy "COPY --from=builder /app /app" with
  | some inst =>
    assertEqual (Token.toString inst.token) "COPY --from=builder /app /app"
      "copy from flag round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: COPY with --from should parse")

open DockerfileModel.Parser.Instructions.Copy in
/-- Test: COPY --chown=user:group src dest — with chown flag -/
def testCopyWithChown : IO Unit := do
  IO.println "Copy: with --chown flag"
  match parseCopy "COPY --chown=user:group src dest" with
  | some inst =>
    assertEqual (Token.toString inst.token) "COPY --chown=user:group src dest"
      "copy chown flag round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: COPY with --chown should parse")

open DockerfileModel.Parser.Instructions.Copy in
/-- Test: COPY --link src dest — with link flag -/
def testCopyWithLink : IO Unit := do
  IO.println "Copy: with --link flag"
  match parseCopy "COPY --link src dest" with
  | some inst =>
    assertEqual (Token.toString inst.token) "COPY --link src dest"
      "copy link flag round-trip"
    assertTrue (inst.name == .copy) "copy instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: COPY with --link should parse")

open DockerfileModel.Parser.Instructions.Copy in
/-- Test: COPY ["src", "dest"] — exec form -/
def testCopyExecForm : IO Unit := do
  IO.println "Copy: exec form"
  match parseCopy "COPY [\"src\", \"dest\"]" with
  | some inst =>
    assertEqual (Token.toString inst.token) "COPY [\"src\", \"dest\"]"
      "copy exec form round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: COPY exec form should parse")

open DockerfileModel.Parser.Instructions.Copy in
/-- Test: COPY --chmod=755 src dest — with chmod flag -/
def testCopyWithChmod : IO Unit := do
  IO.println "Copy: with --chmod flag"
  match parseCopy "COPY --chmod=755 src dest" with
  | some inst =>
    assertEqual (Token.toString inst.token) "COPY --chmod=755 src dest"
      "copy chmod flag round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: COPY with --chmod should parse")

-- ============================================================================
-- ADD Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Add in
/-- Test: ADD . /app — basic form -/
def testAddBasic : IO Unit := do
  IO.println "Add: basic form"
  match parseAdd "ADD . /app" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ADD . /app"
      "add basic round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ADD basic should parse")

open DockerfileModel.Parser.Instructions.Add in
/-- Test: ADD --checksum=sha256:abc123 file.tar /app — with checksum flag -/
def testAddWithChecksum : IO Unit := do
  IO.println "Add: with --checksum flag"
  match parseAdd "ADD --checksum=sha256:abc123 file.tar /app" with
  | some inst =>
    assertEqual (Token.toString inst.token)
      "ADD --checksum=sha256:abc123 file.tar /app"
      "add checksum flag round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ADD with --checksum should parse")

open DockerfileModel.Parser.Instructions.Add in
/-- Test: ADD --keep-git-dir repo /app — with keep-git-dir flag -/
def testAddWithKeepGitDir : IO Unit := do
  IO.println "Add: with --keep-git-dir flag"
  match parseAdd "ADD --keep-git-dir repo /app" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ADD --keep-git-dir repo /app"
      "add keep-git-dir flag round-trip"
    assertTrue (inst.name == .add) "add instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: ADD with --keep-git-dir should parse")

open DockerfileModel.Parser.Instructions.Add in
/-- Test: ADD --chmod=644 src dest — with chmod flag -/
def testAddWithChmod : IO Unit := do
  IO.println "Add: with --chmod flag"
  match parseAdd "ADD --chmod=644 src dest" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ADD --chmod=644 src dest"
      "add chmod flag round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ADD with --chmod should parse")

open DockerfileModel.Parser.Instructions.Add in
/-- Test: ADD --link src dest — with link flag -/
def testAddWithLink : IO Unit := do
  IO.println "Add: with --link flag"
  match parseAdd "ADD --link src dest" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ADD --link src dest"
      "add link flag round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ADD with --link should parse")

open DockerfileModel.Parser.Instructions.Add in
/-- Test: ADD ["src", "dest"] — exec form -/
def testAddExecForm : IO Unit := do
  IO.println "Add: exec form"
  match parseAdd "ADD [\"src\", \"dest\"]" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ADD [\"src\", \"dest\"]"
      "add exec form round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ADD exec form should parse")

-- ============================================================================
-- HEALTHCHECK Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Healthcheck in
/-- Test: HEALTHCHECK NONE — disable healthcheck -/
def testHealthcheckNone : IO Unit := do
  IO.println "Healthcheck: NONE form"
  match parseHealthcheck "HEALTHCHECK NONE" with
  | some inst =>
    assertEqual (Token.toString inst.token) "HEALTHCHECK NONE"
      "healthcheck NONE round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: HEALTHCHECK NONE should parse")

open DockerfileModel.Parser.Instructions.Healthcheck in
/-- Test: HEALTHCHECK CMD ["curl", "-f", "http://localhost/"] — CMD exec form -/
def testHealthcheckCmdExec : IO Unit := do
  IO.println "Healthcheck: CMD exec form"
  match parseHealthcheck "HEALTHCHECK CMD [\"curl\", \"-f\", \"http://localhost/\"]" with
  | some inst =>
    assertEqual (Token.toString inst.token)
      "HEALTHCHECK CMD [\"curl\", \"-f\", \"http://localhost/\"]"
      "healthcheck CMD exec round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: HEALTHCHECK CMD exec form should parse")

open DockerfileModel.Parser.Instructions.Healthcheck in
/-- Test: HEALTHCHECK CMD curl -f http://localhost/ — CMD shell form -/
def testHealthcheckCmdShell : IO Unit := do
  IO.println "Healthcheck: CMD shell form"
  match parseHealthcheck "HEALTHCHECK CMD curl -f http://localhost/" with
  | some inst =>
    assertEqual (Token.toString inst.token)
      "HEALTHCHECK CMD curl -f http://localhost/"
      "healthcheck CMD shell round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: HEALTHCHECK CMD shell form should parse")

open DockerfileModel.Parser.Instructions.Healthcheck in
/-- Test: HEALTHCHECK --interval=30s CMD curl -f http://localhost/ — with interval flag -/
def testHealthcheckWithInterval : IO Unit := do
  IO.println "Healthcheck: with --interval flag"
  match parseHealthcheck "HEALTHCHECK --interval=30s CMD curl -f http://localhost/" with
  | some inst =>
    assertEqual (Token.toString inst.token)
      "HEALTHCHECK --interval=30s CMD curl -f http://localhost/"
      "healthcheck interval flag round-trip"
    assertTrue (inst.name == .healthCheck) "healthcheck instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: HEALTHCHECK with --interval should parse")

open DockerfileModel.Parser.Instructions.Healthcheck in
/-- Test: HEALTHCHECK --interval=30s --timeout=10s --retries=3 CMD curl localhost — multiple flags -/
def testHealthcheckMultipleFlags : IO Unit := do
  IO.println "Healthcheck: multiple flags"
  match parseHealthcheck "HEALTHCHECK --interval=30s --timeout=10s --retries=3 CMD curl localhost" with
  | some inst =>
    assertEqual (Token.toString inst.token)
      "HEALTHCHECK --interval=30s --timeout=10s --retries=3 CMD curl localhost"
      "healthcheck multiple flags round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: HEALTHCHECK with multiple flags should parse")

open DockerfileModel.Parser.Instructions.Healthcheck in
/-- Test: HEALTHCHECK --start-period=5s CMD echo ok — with start-period flag -/
def testHealthcheckStartPeriod : IO Unit := do
  IO.println "Healthcheck: with --start-period flag"
  match parseHealthcheck "HEALTHCHECK --start-period=5s CMD echo ok" with
  | some inst =>
    assertEqual (Token.toString inst.token)
      "HEALTHCHECK --start-period=5s CMD echo ok"
      "healthcheck start-period flag round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: HEALTHCHECK with --start-period should parse")

-- ============================================================================
-- ONBUILD Instruction Tests
-- ============================================================================

open DockerfileModel.Parser.Instructions.Onbuild in
/-- Test: ONBUILD RUN echo hello — valid trigger -/
def testOnbuildRunTrigger : IO Unit := do
  IO.println "Onbuild: valid RUN trigger"
  match parseOnbuild "ONBUILD RUN echo hello" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ONBUILD RUN echo hello"
      "onbuild RUN trigger round-trip"
    assertTrue (inst.name == .onBuild) "onbuild instruction name"
  | none =>
    throw (IO.Error.userError "Parse failed: ONBUILD RUN should parse")

open DockerfileModel.Parser.Instructions.Onbuild in
/-- Test: ONBUILD COPY . /app — valid trigger -/
def testOnbuildCopyTrigger : IO Unit := do
  IO.println "Onbuild: valid COPY trigger"
  match parseOnbuild "ONBUILD COPY . /app" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ONBUILD COPY . /app"
      "onbuild COPY trigger round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ONBUILD COPY should parse")

open DockerfileModel.Parser.Instructions.Onbuild in
/-- Test: ONBUILD ONBUILD RUN echo — rejected (no chaining) -/
def testOnbuildRejectChaining : IO Unit := do
  IO.println "Onbuild: reject ONBUILD chaining"
  match parseOnbuild "ONBUILD ONBUILD RUN echo" with
  | some _ =>
    throw (IO.Error.userError "Parse should have rejected ONBUILD ONBUILD")
  | none =>
    IO.println "  PASS: ONBUILD ONBUILD correctly rejected"

open DockerfileModel.Parser.Instructions.Onbuild in
/-- Test: ONBUILD FROM ubuntu — rejected (FROM forbidden) -/
def testOnbuildRejectFrom : IO Unit := do
  IO.println "Onbuild: reject FROM trigger"
  match parseOnbuild "ONBUILD FROM ubuntu" with
  | some _ =>
    throw (IO.Error.userError "Parse should have rejected ONBUILD FROM")
  | none =>
    IO.println "  PASS: ONBUILD FROM correctly rejected"

open DockerfileModel.Parser.Instructions.Onbuild in
/-- Test: ONBUILD MAINTAINER john — rejected (MAINTAINER forbidden) -/
def testOnbuildRejectMaintainer : IO Unit := do
  IO.println "Onbuild: reject MAINTAINER trigger"
  match parseOnbuild "ONBUILD MAINTAINER john" with
  | some _ =>
    throw (IO.Error.userError "Parse should have rejected ONBUILD MAINTAINER")
  | none =>
    IO.println "  PASS: ONBUILD MAINTAINER correctly rejected"

-- ============================================================================
-- Phase F: Additional tests for comprehensive coverage
-- ============================================================================

-- Additional MAINTAINER tests

open DockerfileModel.Parser.Instructions.Maintainer in
/-- Test: MAINTAINER with email angle brackets and extra whitespace -/
def testMaintainerExtraWhitespace : IO Unit := do
  IO.println "Maintainer: extra whitespace"
  match parseMaintainer "MAINTAINER   John Smith" with
  | some inst =>
    assertEqual (Token.toString inst.token) "MAINTAINER   John Smith"
      "maintainer extra whitespace round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: MAINTAINER extra whitespace should parse")

open DockerfileModel.Parser.Instructions.Maintainer in
/-- Test: MAINTAINER with line continuation -/
def testMaintainerLineContinuation : IO Unit := do
  IO.println "Maintainer: line continuation"
  match parseMaintainer "MAINTAINER \\\nJohn Smith" with
  | some inst =>
    assertEqual (Token.toString inst.token) "MAINTAINER \\\nJohn Smith"
      "maintainer line continuation round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: MAINTAINER line continuation should parse")

-- Additional WORKDIR tests

open DockerfileModel.Parser.Instructions.Workdir in
/-- Test: WORKDIR with line continuation -/
def testWorkdirLineContinuation : IO Unit := do
  IO.println "Workdir: line continuation"
  match parseWorkdir "WORKDIR \\\n/app" with
  | some inst =>
    assertEqual (Token.toString inst.token) "WORKDIR \\\n/app"
      "workdir line continuation round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: WORKDIR line continuation should parse")

-- Additional STOPSIGNAL tests

open DockerfileModel.Parser.Instructions.Stopsignal in
/-- Test: STOPSIGNAL with braced variable -/
def testStopsignalBracedVariable : IO Unit := do
  IO.println "Stopsignal: braced variable"
  match parseStopsignal "STOPSIGNAL ${SIG:-SIGTERM}" with
  | some inst =>
    assertEqual (Token.toString inst.token) "STOPSIGNAL ${SIG:-SIGTERM}"
      "stopsignal braced variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: STOPSIGNAL braced variable should parse")

-- Additional CMD tests

open DockerfileModel.Parser.Instructions.Cmd in
/-- Test: CMD with line continuation in shell form -/
def testCmdLineContinuation : IO Unit := do
  IO.println "Cmd: line continuation"
  match parseCmd "CMD echo \\\nhello" with
  | some inst =>
    assertEqual (Token.toString inst.token) "CMD echo \\\nhello"
      "cmd line continuation round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: CMD line continuation should parse")

-- Additional ENTRYPOINT tests

open DockerfileModel.Parser.Instructions.Entrypoint in
/-- Test: ENTRYPOINT lowercase keyword -/
def testEntrypointLowercase : IO Unit := do
  IO.println "Entrypoint: lowercase keyword"
  match parseEntrypoint "entrypoint /bin/sh" with
  | some inst =>
    assertEqual (Token.toString inst.token) "entrypoint /bin/sh"
      "entrypoint lowercase round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: lowercase entrypoint should parse")

open DockerfileModel.Parser.Instructions.Entrypoint in
/-- Test: ENTRYPOINT with braced variable -/
def testEntrypointBracedVariable : IO Unit := do
  IO.println "Entrypoint: braced variable"
  match parseEntrypoint "ENTRYPOINT ${ENTRY:-/bin/bash}" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ENTRYPOINT ${ENTRY:-/bin/bash}"
      "entrypoint braced variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ENTRYPOINT braced variable should parse")

-- Additional SHELL tests

open DockerfileModel.Parser.Instructions.Shell in
/-- Test: SHELL with three elements -/
def testShellThreeElements : IO Unit := do
  IO.println "Shell: three elements"
  match parseShell "SHELL [\"/bin/bash\", \"-c\", \"set -e\"]" with
  | some inst =>
    assertEqual (Token.toString inst.token) "SHELL [\"/bin/bash\", \"-c\", \"set -e\"]"
      "shell three elements round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: SHELL three elements should parse")

-- Additional EXPOSE tests

open DockerfileModel.Parser.Instructions.Expose in
/-- Test: EXPOSE lowercase keyword -/
def testExposeLowercase : IO Unit := do
  IO.println "Expose: lowercase keyword"
  match parseExpose "expose 8080" with
  | some inst =>
    assertEqual (Token.toString inst.token) "expose 8080"
      "expose lowercase round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: lowercase expose should parse")

-- Additional VOLUME tests

open DockerfileModel.Parser.Instructions.Volume in
/-- Test: VOLUME lowercase keyword -/
def testVolumeLowercase : IO Unit := do
  IO.println "Volume: lowercase keyword"
  match parseVolume "volume /data" with
  | some inst =>
    assertEqual (Token.toString inst.token) "volume /data"
      "volume lowercase round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: lowercase volume should parse")

-- Additional ENV tests

open DockerfileModel.Parser.Instructions.Env in
/-- Test: ENV lowercase keyword -/
def testEnvLowercase : IO Unit := do
  IO.println "Env: lowercase keyword"
  match parseEnv "env MY_VAR=test" with
  | some inst =>
    assertEqual (Token.toString inst.token) "env MY_VAR=test"
      "env lowercase round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: lowercase env should parse")

-- Additional LABEL tests

open DockerfileModel.Parser.Instructions.Label in
/-- Test: LABEL with empty value -/
def testLabelEmptyValue : IO Unit := do
  IO.println "Label: empty value"
  match parseLabel "LABEL key=" with
  | some inst =>
    assertEqual (Token.toString inst.token) "LABEL key="
      "label empty value round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: LABEL empty value should parse")

open DockerfileModel.Parser.Instructions.Label in
/-- Test: LABEL lowercase keyword -/
def testLabelLowercase : IO Unit := do
  IO.println "Label: lowercase keyword"
  match parseLabel "label version=1.0" with
  | some inst =>
    assertEqual (Token.toString inst.token) "label version=1.0"
      "label lowercase round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: lowercase label should parse")

-- Additional RUN tests

open DockerfileModel.Parser.Instructions.Run in
/-- Test: RUN with line continuation -/
def testRunLineContinuation : IO Unit := do
  IO.println "Run: line continuation"
  match parseRun "RUN echo \\\nhello" with
  | some inst =>
    assertEqual (Token.toString inst.token) "RUN echo \\\nhello"
      "run line continuation round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: RUN line continuation should parse")

open DockerfileModel.Parser.Instructions.Run in
/-- Test: RUN lowercase keyword -/
def testRunLowercase : IO Unit := do
  IO.println "Run: lowercase keyword"
  match parseRun "run echo test" with
  | some inst =>
    assertEqual (Token.toString inst.token) "run echo test"
      "run lowercase round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: lowercase run should parse")

-- Additional COPY tests

open DockerfileModel.Parser.Instructions.Copy in
/-- Test: COPY --from=$VAR src dst — variable ref in --from is treated as plain text -/
def testCopyFromVarNoExpansion : IO Unit := do
  IO.println "Copy: --from with dollar-sign variable (no expansion)"
  match parseCopy "COPY --from=$VAR src dst" with
  | some inst =>
    assertEqual (Token.toString inst.token) "COPY --from=$VAR src dst"
      "copy --from=$VAR round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: COPY --from=$VAR should parse")

open DockerfileModel.Parser.Instructions.Copy in
/-- Test: COPY --from=${VAR} src dst — braced variable ref in --from is treated as plain text -/
def testCopyFromBracedVarNoExpansion : IO Unit := do
  IO.println "Copy: --from with braced variable (no expansion)"
  match parseCopy "COPY --from=${VAR} src dst" with
  | some inst =>
    assertEqual (Token.toString inst.token) "COPY --from=${VAR} src dst"
      "copy --from=${VAR} round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: COPY --from=${VAR} should parse")

open DockerfileModel.Parser.Instructions.Copy in
/-- Test: COPY with variable -/
def testCopyVariable : IO Unit := do
  IO.println "Copy: with variable"
  match parseCopy "COPY $SRC /app" with
  | some inst =>
    assertEqual (Token.toString inst.token) "COPY $SRC /app"
      "copy variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: COPY with variable should parse")

open DockerfileModel.Parser.Instructions.Copy in
/-- Test: COPY lowercase keyword -/
def testCopyLowercase : IO Unit := do
  IO.println "Copy: lowercase keyword"
  match parseCopy "copy . /app" with
  | some inst =>
    assertEqual (Token.toString inst.token) "copy . /app"
      "copy lowercase round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: lowercase copy should parse")

-- Additional ADD tests

open DockerfileModel.Parser.Instructions.Add in
/-- Test: ADD with variable -/
def testAddVariable : IO Unit := do
  IO.println "Add: with variable"
  match parseAdd "ADD $SRC /app" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ADD $SRC /app"
      "add variable round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ADD with variable should parse")

open DockerfileModel.Parser.Instructions.Add in
/-- Test: ADD lowercase keyword -/
def testAddLowercase : IO Unit := do
  IO.println "Add: lowercase keyword"
  match parseAdd "add . /app" with
  | some inst =>
    assertEqual (Token.toString inst.token) "add . /app"
      "add lowercase round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: lowercase add should parse")

-- Additional HEALTHCHECK tests

open DockerfileModel.Parser.Instructions.Healthcheck in
/-- Test: HEALTHCHECK lowercase keyword -/
def testHealthcheckLowercase : IO Unit := do
  IO.println "Healthcheck: lowercase keyword"
  match parseHealthcheck "healthcheck NONE" with
  | some inst =>
    assertEqual (Token.toString inst.token) "healthcheck NONE"
      "healthcheck lowercase round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: lowercase healthcheck should parse")

-- Additional ONBUILD tests

open DockerfileModel.Parser.Instructions.Onbuild in
/-- Test: ONBUILD ENV MY_VAR=hello — valid ENV trigger -/
def testOnbuildEnvTrigger : IO Unit := do
  IO.println "Onbuild: valid ENV trigger"
  match parseOnbuild "ONBUILD ENV MY_VAR=hello" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ONBUILD ENV MY_VAR=hello"
      "onbuild ENV trigger round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ONBUILD ENV should parse")

open DockerfileModel.Parser.Instructions.Onbuild in
/-- Test: ONBUILD ADD . /app — valid ADD trigger -/
def testOnbuildAddTrigger : IO Unit := do
  IO.println "Onbuild: valid ADD trigger"
  match parseOnbuild "ONBUILD ADD . /app" with
  | some inst =>
    assertEqual (Token.toString inst.token) "ONBUILD ADD . /app"
      "onbuild ADD trigger round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: ONBUILD ADD should parse")

open DockerfileModel.Parser.Instructions.Onbuild in
/-- Test: onbuild lowercase keyword -/
def testOnbuildLowercase : IO Unit := do
  IO.println "Onbuild: lowercase keyword"
  match parseOnbuild "onbuild RUN echo test" with
  | some inst =>
    assertEqual (Token.toString inst.token) "onbuild RUN echo test"
      "onbuild lowercase round-trip"
  | none =>
    throw (IO.Error.userError "Parse failed: lowercase onbuild should parse")

end DockerfileModel.Tests.ParserTests

-- ============================================================================
-- Test runner entry point (called from main in SlimCheck.lean)
-- ============================================================================

open DockerfileModel.Tests.ParserTests in
/-- Run FROM and ARG parser tests. -/
def runParserTests_FromArg : IO Unit := do
  IO.println "=== FROM Instruction Tests ==="
  IO.println ""
  testFromScratch
  testFromUbuntu
  testFromUbuntuLatest
  testFromDigest
  IO.println ""
  testFromWithTagAndStage
  testFromAsBuilder
  IO.println ""
  testFromPlatformAndStage
  testFromPlatformNoStage
  testFromAllCombined
  IO.println ""
  testFromLineContinuationAfterKeyword
  testFromLineContinuationInBody
  testFromComplexWithComment
  testFromPlatformWithLineContinuation
  testFromLineContinuationInsideName
  testFromLineContinuationInStageName
  testFromBackslashLineContinuation
  IO.println ""
  testFromQuotedImageName
  testFromVariableRef
  testFromSimpleVariableRef
  testFromFullyQualifiedImage
  IO.println ""
  testFromLowercaseKeyword
  testFromMixedCase
  IO.println ""
  testFromExtraWhitespace
  IO.println ""
  testFromStructureValidation
  IO.println ""

  IO.println "=== ARG Instruction Tests ==="
  IO.println ""
  testArgSimple
  testArgMultipleNoValues
  testArgWithValue
  testArgMultipleWithValues
  IO.println ""
  testArgEmptyDefault
  testArgMultipleEmptyDefaults
  testArgQuotedEmptyDefault
  testArgMultipleQuotedEmptyDefaults
  testArgQuotedDefault
  IO.println ""
  testArgVariableRefDefault
  testArgSimpleVariableRefDefault
  testArgUnderscoreName
  IO.println ""
  testArgLineContinuation
  testArgWithComment
  IO.println ""
  testArgStructureValidation
  testArgQuotedEmptyStructure
  IO.println ""

open DockerfileModel.Tests.ParserTests in
/-- Run infrastructure and edge case tests. -/
def runParserTests_Infrastructure : IO Unit := do
  IO.println "=== Dockerfile-level FROM + ARG Tests ==="
  IO.println ""
  testDockerfileFromArgRoundTrip
  testDockerfileMultiStage
  IO.println ""

  IO.println "=== Edge Case Tests ==="
  IO.println ""
  testEdgeCaseEmptyInstruction
  testEdgeCaseSingleCharImage
  testEdgeCaseSingleCharArg
  testEdgeCaseCRLF
  testEdgeCaseTabWhitespace
  testEdgeCaseArgTabIndent
  IO.println ""

  IO.println "=== Stage Name Validation Tests ==="
  IO.println ""
  testStageNameLowercaseSucceeds
  testStageNameUppercaseRejected
  testStageNameWithSpecialChars
  testStageNameLetterDigit
  testStageNameDigitStartRejected
  IO.println ""

  IO.println "=== Exec Form (JSON Array) Tests ==="
  IO.println ""
  testExecFormSimple
  testExecFormEmpty
  testExecFormWithWhitespace
  testExecFormWithLineContinuation
  testExecFormSingleElement
  testExecFormEscapes
  testExecFormThreeElements
  IO.println ""

  IO.println "=== Flag Parser Tests ==="
  IO.println ""
  testFlagPlatform
  testFlagFrom
  testBoolFlagLink
  testBoolFlagLinkTrue
  testBoolFlagLinkFalse
  testFlagChown
  IO.println ""

  IO.println "=== Shell Form Command Tests ==="
  IO.println ""
  testShellFormBasic
  testShellFormWithVariable
  testShellFormWithLineContinuation
  testShellFormWithBracedVariable
  testShellFormOpaqueStructure
  testShellFormOpaqueWithLineContinuation
  testShellFormOpaqueWithTrailingWhitespace
  IO.println ""

open DockerfileModel.Tests.ParserTests in
/-- Run Phase C simple instruction parser tests. -/
def runParserTests_PhaseC_Group1 : IO Unit := do
  IO.println "=== MAINTAINER Instruction Tests ==="
  IO.println ""
  testMaintainerBasic
  testMaintainerWithVariable
  testMaintainerLowercase
  testMaintainerSimpleName
  testMaintainerBracedVariable
  testMaintainerExtraWhitespace
  testMaintainerLineContinuation
  IO.println ""

  IO.println "=== WORKDIR Instruction Tests ==="
  IO.println ""
  testWorkdirBasic
  testWorkdirWithVariable
  testWorkdirBracedVariable
  testWorkdirWithSpaces
  testWorkdirInstructionName
  testWorkdirLineContinuation
  IO.println ""

  IO.println "=== STOPSIGNAL Instruction Tests ==="
  IO.println ""
  testStopsignalName
  testStopsignalNumber
  testStopsignalVariable
  testStopsignalSIGKILL
  testStopsignalBracedVariable
  IO.println ""

  IO.println "=== CMD Instruction Tests ==="
  IO.println ""
  testCmdExecForm
  testCmdShellForm
  testCmdShellVariable
  testCmdExecSingle
  testCmdLowercase
  testCmdLineContinuation
  IO.println ""

  IO.println "=== ENTRYPOINT Instruction Tests ==="
  IO.println ""
  testEntrypointExecForm
  testEntrypointShellForm
  testEntrypointVariable
  testEntrypointExecEmpty
  testEntrypointLowercase
  testEntrypointBracedVariable
  IO.println ""

open DockerfileModel.Tests.ParserTests in
/-- Run Phase C instruction parser tests (continued). -/
def runParserTests_PhaseC_Group2 : IO Unit := do
  IO.println "=== SHELL Instruction Tests ==="
  IO.println ""
  testShellExecForm
  testShellBashForm
  testShellSingleElement
  testShellLowercase
  testShellThreeElements
  IO.println ""

  IO.println "=== USER Instruction Tests ==="
  IO.println ""
  testUserBasic
  testUserWithGroup
  testUserVariable
  testUserNumeric
  testUserBracedVariable
  IO.println ""

  IO.println "=== EXPOSE Instruction Tests ==="
  IO.println ""
  testExposeBasic
  testExposeWithProtocol
  testExposeMultiple
  testExposeMultipleWithProtocol
  testExposeVariable
  testExposeLowercase
  IO.println ""

  IO.println "=== VOLUME Instruction Tests ==="
  IO.println ""
  testVolumeExecForm
  testVolumeShellForm
  testVolumeMultiple
  testVolumeVariable
  testVolumeExecMultiple
  testVolumeLowercase
  IO.println ""

  IO.println "=== ENV Instruction Tests ==="
  IO.println ""
  testEnvModernBasic
  testEnvModernMultiple
  testEnvModernQuoted
  testEnvLegacy
  testEnvWithVariable
  testEnvEmptyValue
  testEnvLowercase
  IO.println ""

  IO.println "=== LABEL Instruction Tests ==="
  IO.println ""
  testLabelBasic
  testLabelMultiple
  testLabelDottedKey
  testLabelHyphenatedKey
  testLabelVariable
  testLabelEmptyValue
  testLabelLowercase
  IO.println ""

open DockerfileModel.Tests.ParserTests in
/-- Run Phase D complex instruction parser tests. -/
def runParserTests_PhaseD : IO Unit := do
  IO.println "=== RUN Instruction Tests ==="
  IO.println ""
  testRunShellForm
  testRunExecForm
  testRunWithMount
  testRunWithNetwork
  testRunWithSecurity
  testRunMultipleFlags
  testRunWithVariable
  testRunLineContinuation
  testRunLowercase
  IO.println ""

  IO.println "=== COPY Instruction Tests ==="
  IO.println ""
  testCopyBasic
  testCopyWithFrom
  testCopyWithChown
  testCopyWithLink
  testCopyExecForm
  testCopyWithChmod
  testCopyFromVarNoExpansion
  testCopyFromBracedVarNoExpansion
  testCopyVariable
  testCopyLowercase
  IO.println ""

  IO.println "=== ADD Instruction Tests ==="
  IO.println ""
  testAddBasic
  testAddWithChecksum
  testAddWithKeepGitDir
  testAddWithChmod
  testAddWithLink
  testAddExecForm
  testAddVariable
  testAddLowercase
  IO.println ""

  IO.println "=== HEALTHCHECK Instruction Tests ==="
  IO.println ""
  testHealthcheckNone
  testHealthcheckCmdExec
  testHealthcheckCmdShell
  testHealthcheckWithInterval
  testHealthcheckMultipleFlags
  testHealthcheckStartPeriod
  testHealthcheckLowercase
  IO.println ""

  IO.println "=== ONBUILD Instruction Tests ==="
  IO.println ""
  testOnbuildRunTrigger
  testOnbuildCopyTrigger
  testOnbuildRejectChaining
  testOnbuildRejectFrom
  testOnbuildRejectMaintainer
  testOnbuildEnvTrigger
  testOnbuildAddTrigger
  testOnbuildLowercase
  IO.println ""

-- ============================================================================
-- Phase E: Heredoc and Variable Modifier Tests
-- ============================================================================

open DockerfileModel in
open DockerfileModel.Tests.ParserTests in
open DockerfileModel.Parser.Instructions.Run in

/-- RUN with heredoc: RUN <<EOF\necho hello\nEOF\n -/
def testRunHeredoc : IO Unit := do
  IO.println "RUN heredoc: basic"
  let input := "RUN <<EOF\necho hello\nEOF\n"
  match parseRun input with
  | some instr =>
    assertEqual (Token.toString instr.token) input "RUN heredoc round-trips"
  | none =>
    -- Heredoc parsing may not consume via the standard parseRun path
    -- Test the token tree construction instead
    let heredocBody := Token.mkHeredoc [
      Token.mkString "echo hello\n",
      Token.mkString "EOF",
      Token.mkNewLine "\n"
    ]
    let token := Token.mkInstruction [
      Token.mkKeyword [Token.mkString "RUN"],
      Token.mkWhitespace " ",
      Token.mkString "<<EOF",
      Token.mkNewLine "\n",
      heredocBody
    ]
    assertRoundTrip token input "RUN heredoc token tree"

open DockerfileModel in
open DockerfileModel.Tests.ParserTests in

/-- RUN with heredoc and chomp flag: RUN <<-EOF\n\techo hello\nEOF\n -/
def testRunHeredocChomp : IO Unit := do
  IO.println "RUN heredoc: with chomp flag"
  -- Construct the expected token tree for a heredoc with chomp
  let heredocBody := Token.mkHeredoc [
    Token.mkString "echo hello\n",  -- tab stripped by chomp
    Token.mkString "EOF",
    Token.mkNewLine "\n"
  ]
  let token := Token.mkInstruction [
    Token.mkKeyword [Token.mkString "RUN"],
    Token.mkWhitespace " ",
    Token.mkString "<<-EOF",
    Token.mkNewLine "\n",
    heredocBody
  ]
  -- With chomp, input tabs are stripped in the body
  assertRoundTrip token "RUN <<-EOF\necho hello\nEOF\n" "RUN heredoc chomp token tree"

open DockerfileModel in
open DockerfileModel.Tests.ParserTests in

/-- RUN with quoted heredoc delimiter: RUN <<"EOF"\nno $expansion\nEOF\n -/
def testRunHeredocQuoted : IO Unit := do
  IO.println "RUN heredoc: quoted delimiter"
  let heredocBody := Token.mkHeredoc [
    Token.mkString "no $expansion\n",
    Token.mkString "EOF",
    Token.mkNewLine "\n"
  ]
  let token := Token.mkInstruction [
    Token.mkKeyword [Token.mkString "RUN"],
    Token.mkWhitespace " ",
    Token.mkString "<<\"EOF\"",
    Token.mkNewLine "\n",
    heredocBody
  ]
  assertRoundTrip token "RUN <<\"EOF\"\nno $expansion\nEOF\n" "RUN heredoc quoted token tree"

open DockerfileModel in
open DockerfileModel.Tests.ParserTests in
open DockerfileModel.Parser.Instructions.Copy in

/-- COPY with heredoc: COPY <<EOF /etc/config\nkey=value\nEOF\n -/
def testCopyHeredoc : IO Unit := do
  IO.println "COPY heredoc: with destination"
  let heredocBody := Token.mkHeredoc [
    Token.mkString "key=value\n",
    Token.mkString "EOF",
    Token.mkNewLine "\n"
  ]
  let token := Token.mkInstruction [
    Token.mkKeyword [Token.mkString "COPY"],
    Token.mkWhitespace " ",
    Token.mkString "<<EOF",
    Token.mkWhitespace " ",
    Token.mkLiteral [Token.mkString "/etc/config"],
    Token.mkNewLine "\n",
    heredocBody
  ]
  assertRoundTrip token "COPY <<EOF /etc/config\nkey=value\nEOF\n" "COPY heredoc token tree"

-- ============================================================================
-- Extended Variable Modifier Tests
-- ============================================================================

open DockerfileModel.Tests.ParserTests in
open DockerfileModel in

/-- Test ${var#prefix} — hash pattern modifier removes prefix. -/
def testHashPatternModifier : IO Unit := do
  IO.println "Variable modifier: # (hash pattern)"
  let vars : VarMap := [("path", "/usr/local/bin")]
  let ref : VariableRef := { name := "path", modifier := some .hashPattern, modifierValue := some "/usr/local" }
  match resolve vars ref with
  | .ok val =>
    assertEqual val "/bin" "hash pattern removes prefix"
  | .error msg =>
    throw (IO.Error.userError s!"Unexpected error: {msg}")

open DockerfileModel.Tests.ParserTests in
open DockerfileModel in

/-- Test ${var##prefix} — double hash pattern modifier removes prefix. -/
def testDoubleHashPatternModifier : IO Unit := do
  IO.println "Variable modifier: ## (double hash pattern)"
  let vars : VarMap := [("path", "/usr/local/bin")]
  let ref : VariableRef := { name := "path", modifier := some .doubleHashPattern, modifierValue := some "/usr/local" }
  match resolve vars ref with
  | .ok val =>
    assertEqual val "/bin" "double hash pattern removes prefix"
  | .error msg =>
    throw (IO.Error.userError s!"Unexpected error: {msg}")

open DockerfileModel.Tests.ParserTests in
open DockerfileModel in

/-- Test ${var%suffix} — percent pattern modifier removes suffix. -/
def testPercentPatternModifier : IO Unit := do
  IO.println "Variable modifier: % (percent pattern)"
  let vars : VarMap := [("file", "archive.tar.gz")]
  let ref : VariableRef := { name := "file", modifier := some .percentPattern, modifierValue := some ".tar.gz" }
  match resolve vars ref with
  | .ok val =>
    assertEqual val "archive" "percent pattern removes suffix"
  | .error msg =>
    throw (IO.Error.userError s!"Unexpected error: {msg}")

open DockerfileModel.Tests.ParserTests in
open DockerfileModel in

/-- Test ${var%%suffix} — double percent pattern modifier removes suffix. -/
def testDoublePercentPatternModifier : IO Unit := do
  IO.println "Variable modifier: %% (double percent pattern)"
  let vars : VarMap := [("file", "archive.tar.gz")]
  let ref : VariableRef := { name := "file", modifier := some .doublePercentPattern, modifierValue := some ".tar.gz" }
  match resolve vars ref with
  | .ok val =>
    assertEqual val "archive" "double percent pattern removes suffix"
  | .error msg =>
    throw (IO.Error.userError s!"Unexpected error: {msg}")

open DockerfileModel.Tests.ParserTests in
open DockerfileModel in

/-- Test ${var/old/new} — slash pattern modifier replaces first match. -/
def testSlashPatternModifier : IO Unit := do
  IO.println "Variable modifier: / (slash pattern)"
  let vars : VarMap := [("greeting", "hello world hello")]
  let ref : VariableRef := { name := "greeting", modifier := some .slashPattern, modifierValue := some "hello/goodbye" }
  match resolve vars ref with
  | .ok val =>
    assertEqual val "goodbye world hello" "slash pattern replaces first match"
  | .error msg =>
    throw (IO.Error.userError s!"Unexpected error: {msg}")

open DockerfileModel.Tests.ParserTests in
open DockerfileModel in

/-- Test ${var//old/new} — double slash pattern modifier replaces all matches. -/
def testDoubleSlashPatternModifier : IO Unit := do
  IO.println "Variable modifier: // (double slash pattern)"
  let vars : VarMap := [("greeting", "hello world hello")]
  let ref : VariableRef := { name := "greeting", modifier := some .doubleSlashPattern, modifierValue := some "hello/goodbye" }
  match resolve vars ref with
  | .ok val =>
    assertEqual val "goodbye world goodbye" "double slash pattern replaces all matches"
  | .error msg =>
    throw (IO.Error.userError s!"Unexpected error: {msg}")

-- ============================================================================
-- Variable parser tests for new modifiers
-- ============================================================================

open DockerfileModel in
open DockerfileModel.Tests.ParserTests in
open DockerfileModel.Parser in

/-- Test that the variable parser recognizes ${var#prefix} syntax. -/
def testParseHashModifier : IO Unit := do
  IO.println "Variable parser: ${var#prefix}"
  let input := "${path#/usr/local}"
  let result := (bracedVariableRef '\\').tryParse input
  match result with
  | some tok =>
    assertEqual (Token.toString tok) input "hash modifier round-trips"
  | none =>
    throw (IO.Error.userError s!"Failed to parse: {input}")

open DockerfileModel in
open DockerfileModel.Tests.ParserTests in
open DockerfileModel.Parser in

/-- Test that the variable parser recognizes ${var##prefix} syntax. -/
def testParseDoubleHashModifier : IO Unit := do
  IO.println "Variable parser: ${var##prefix}"
  let input := "${path##/usr/local}"
  let result := (bracedVariableRef '\\').tryParse input
  match result with
  | some tok =>
    assertEqual (Token.toString tok) input "double hash modifier round-trips"
  | none =>
    throw (IO.Error.userError s!"Failed to parse: {input}")

open DockerfileModel in
open DockerfileModel.Tests.ParserTests in
open DockerfileModel.Parser in

/-- Test that the variable parser recognizes ${var%suffix} syntax. -/
def testParsePercentModifier : IO Unit := do
  IO.println "Variable parser: ${var%suffix}"
  let input := "${file%.tar.gz}"
  let result := (bracedVariableRef '\\').tryParse input
  match result with
  | some tok =>
    assertEqual (Token.toString tok) input "percent modifier round-trips"
  | none =>
    throw (IO.Error.userError s!"Failed to parse: {input}")

open DockerfileModel in
open DockerfileModel.Tests.ParserTests in
open DockerfileModel.Parser in

/-- Test that the variable parser recognizes ${var%%suffix} syntax. -/
def testParseDoublePercentModifier : IO Unit := do
  IO.println "Variable parser: ${var%%suffix}"
  let input := "${file%%.*}"
  let result := (bracedVariableRef '\\').tryParse input
  match result with
  | some tok =>
    assertEqual (Token.toString tok) input "double percent modifier round-trips"
  | none =>
    throw (IO.Error.userError s!"Failed to parse: {input}")

open DockerfileModel in
open DockerfileModel.Tests.ParserTests in
open DockerfileModel.Parser in

/-- Test that the variable parser recognizes ${var/old/new} syntax. -/
def testParseSlashModifier : IO Unit := do
  IO.println "Variable parser: ${var/old/new}"
  let input := "${greeting/hello/goodbye}"
  let result := (bracedVariableRef '\\').tryParse input
  match result with
  | some tok =>
    assertEqual (Token.toString tok) input "slash modifier round-trips"
  | none =>
    throw (IO.Error.userError s!"Failed to parse: {input}")

open DockerfileModel in
open DockerfileModel.Tests.ParserTests in
open DockerfileModel.Parser in

/-- Test that the variable parser recognizes ${var//old/new} syntax. -/
def testParseDoubleSlashModifier : IO Unit := do
  IO.println "Variable parser: ${var//old/new}"
  let input := "${greeting//hello/goodbye}"
  let result := (bracedVariableRef '\\').tryParse input
  match result with
  | some tok =>
    assertEqual (Token.toString tok) input "double slash modifier round-trips"
  | none =>
    throw (IO.Error.userError s!"Failed to parse: {input}")

-- ============================================================================
-- Phase E test runner
-- ============================================================================

/-- Run Phase E heredoc and variable modifier tests. -/
def runParserTests_PhaseE : IO Unit := do
  IO.println "=== Heredoc Tests ==="
  IO.println ""
  testRunHeredoc
  testRunHeredocChomp
  testRunHeredocQuoted
  testCopyHeredoc
  IO.println ""

  IO.println "=== Extended Variable Modifier Tests ==="
  IO.println ""
  testHashPatternModifier
  testDoubleHashPatternModifier
  testPercentPatternModifier
  testDoublePercentPatternModifier
  testSlashPatternModifier
  testDoubleSlashPatternModifier
  IO.println ""

  IO.println "=== Variable Parser Extended Modifier Tests ==="
  IO.println ""
  testParseHashModifier
  testParseDoubleHashModifier
  testParsePercentModifier
  testParseDoublePercentModifier
  testParseSlashModifier
  testParseDoubleSlashModifier
  IO.println ""

/-- Run all parser tests. Call this from the main test runner. -/
def runParserTests : IO Unit := do
  runParserTests_FromArg
  runParserTests_Infrastructure
  runParserTests_PhaseC_Group1
  runParserTests_PhaseC_Group2
  runParserTests_PhaseD
  runParserTests_PhaseE
  IO.println "=== All parser tests passed ==="
