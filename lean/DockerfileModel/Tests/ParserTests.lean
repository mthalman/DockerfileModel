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

namespace DockerfileModel.Tests.ParserTests

open DockerfileModel

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

end DockerfileModel.Tests.ParserTests

-- ============================================================================
-- Test runner entry point (called from main in SlimCheck.lean)
-- ============================================================================

open DockerfileModel.Tests.ParserTests in

/-- Run all parser tests. Call this from the main test runner. -/
def runParserTests : IO Unit := do
  IO.println "=== FROM Instruction Tests ==="
  IO.println ""
  -- Simple FROM tests
  testFromScratch
  testFromUbuntu
  testFromUbuntuLatest
  testFromDigest
  IO.println ""
  -- FROM with stage name
  testFromWithTagAndStage
  testFromAsBuilder
  IO.println ""
  -- FROM with platform flag
  testFromPlatformAndStage
  testFromPlatformNoStage
  testFromAllCombined
  IO.println ""
  -- FROM with line continuations
  testFromLineContinuationAfterKeyword
  testFromLineContinuationInBody
  testFromComplexWithComment
  testFromPlatformWithLineContinuation
  testFromLineContinuationInsideName
  testFromLineContinuationInStageName
  testFromBackslashLineContinuation
  IO.println ""
  -- FROM with quotes and variables
  testFromQuotedImageName
  testFromVariableRef
  testFromSimpleVariableRef
  testFromFullyQualifiedImage
  IO.println ""
  -- FROM case sensitivity
  testFromLowercaseKeyword
  testFromMixedCase
  IO.println ""
  -- FROM extra whitespace
  testFromExtraWhitespace
  IO.println ""
  -- FROM structure validation
  testFromStructureValidation
  IO.println ""

  IO.println "=== ARG Instruction Tests ==="
  IO.println ""
  -- Simple ARG tests
  testArgSimple
  testArgMultipleNoValues
  testArgWithValue
  testArgMultipleWithValues
  IO.println ""
  -- ARG with empty/quoted defaults
  testArgEmptyDefault
  testArgMultipleEmptyDefaults
  testArgQuotedEmptyDefault
  testArgMultipleQuotedEmptyDefaults
  testArgQuotedDefault
  IO.println ""
  -- ARG with variable references
  testArgVariableRefDefault
  testArgSimpleVariableRefDefault
  testArgUnderscoreName
  IO.println ""
  -- ARG with line continuations and comments
  testArgLineContinuation
  testArgWithComment
  IO.println ""
  -- ARG structure validation
  testArgStructureValidation
  testArgQuotedEmptyStructure
  IO.println ""

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
  IO.println "=== All parser tests passed ==="
