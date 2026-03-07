/-
  Tests/SlimCheck.lean — Property tests using Lean's built-in testing.

  These property tests mirror the Phase 0 FsCheck properties from the C# test suite:
  1. Token tree consistency: toString == concat children (for non-special aggregates)
  2. Primitive token identity: toString returns the stored value
  3. VariableRefToken prepends "$"
  4. Quoted tokens wrap in quote characters
  5. Dockerfile toString is concat of constructs

  Since SlimCheck requires `Testable` instances and `Shrinkable`/`SampleableExt`
  instances for custom types, and our Token type is recursive (making automatic
  derivation complex), we use `#eval` based tests that construct representative
  token trees and verify the properties hold.

  These tests serve as executable specifications that complement the formal proofs
  in Proofs/TokenConcat.lean.
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Dockerfile
import DockerfileModel.Tests.ParserTests

namespace DockerfileModel.Tests

open DockerfileModel

-- ============================================================================
-- Test helpers
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

-- ============================================================================
-- Property 1: Primitive token toString returns stored value
-- ============================================================================

def testPrimitiveTokenIdentity : IO Unit := do
  IO.println "Property 1: Primitive token toString returns stored value"

  -- StringToken
  let t1 := Token.mkString "hello"
  assertEqual (Token.toString t1) "hello" "StringToken(\"hello\")"

  -- WhitespaceToken
  let t2 := Token.mkWhitespace "  "
  assertEqual (Token.toString t2) "  " "WhitespaceToken(\"  \")"

  -- SymbolToken
  let t3 := Token.mkSymbol '#'
  assertEqual (Token.toString t3) "#" "SymbolToken('#')"

  -- NewLineToken
  let t4 := Token.mkNewLine "\n"
  assertEqual (Token.toString t4) "\n" "NewLineToken(\"\\n\")"

  -- Empty string
  let t5 := Token.mkString ""
  assertEqual (Token.toString t5) "" "StringToken(\"\")"

  -- CRLF newline
  let t6 := Token.mkNewLine "\r\n"
  assertEqual (Token.toString t6) "\r\n" "NewLineToken(\"\\r\\n\")"

-- ============================================================================
-- Property 2: Aggregate token toString == concat children toString
-- ============================================================================

def testAggregateTokenConcat : IO Unit := do
  IO.println "Property 2: Aggregate token toString == concat children"

  -- Simple keyword from string tokens
  let children1 := [Token.mkString "F", Token.mkString "R", Token.mkString "O", Token.mkString "M"]
  let kw := Token.mkKeyword children1
  let expected1 := String.join (children1.map Token.toString)
  assertEqual (Token.toString kw) expected1 "KeywordToken(FROM) == concat children"

  -- Literal with mixed children
  let children2 := [Token.mkString "ubuntu", Token.mkSymbol ':', Token.mkString "latest"]
  let lit := Token.mkLiteral children2
  let expected2 := String.join (children2.map Token.toString)
  assertEqual (Token.toString lit) expected2 "LiteralToken(ubuntu:latest) == concat children"

  -- Nested aggregates: instruction containing keyword + whitespace + literal
  let kwToken := Token.mkKeyword [Token.mkString "FROM"]
  let wsToken := Token.mkWhitespace " "
  let litToken := Token.mkLiteral [Token.mkString "alpine"]
  let instrChildren := [kwToken, wsToken, litToken]
  let instr := Token.mkInstruction instrChildren
  let expected3 := String.join (instrChildren.map Token.toString)
  assertEqual (Token.toString instr) expected3 "InstructionToken(FROM alpine) == concat children"

  -- Comment token
  let commentChildren := [Token.mkSymbol '#', Token.mkString " this is a comment"]
  let comment := Token.mkComment commentChildren
  assertEqual (Token.toString comment) "# this is a comment" "CommentToken == concat children"

  -- KeyValue token (--from=builder)
  let kvChildren := [Token.mkSymbol '-', Token.mkSymbol '-',
                     Token.mkKeyword [Token.mkString "from"],
                     Token.mkSymbol '=',
                     Token.mkLiteral [Token.mkString "builder"]]
  let kv := Token.mkKeyValue kvChildren
  let expected4 := String.join (kvChildren.map Token.toString)
  assertEqual (Token.toString kv) expected4 "KeyValueToken(--from=builder) == concat children"

  -- Empty aggregate
  let emptyAgg := Token.mkKeyword []
  assertEqual (Token.toString emptyAgg) "" "Empty aggregate == \"\""

-- ============================================================================
-- Property 3: VariableRefToken prepends "$"
-- ============================================================================

def testVariableRefPrependsDollar : IO Unit := do
  IO.println "Property 3: VariableRefToken prepends \"$\""

  -- Simple variable: $VAR -> children are just [StringToken("VAR")]
  let varRef1 := Token.mkVariableRef [Token.mkString "VAR"]
  assertEqual (Token.toString varRef1) "$VAR" "VariableRef($VAR)"

  -- Braced variable: ${VAR} -> children are [{, StringToken("VAR"), }]
  let varRef2 := Token.mkVariableRef [Token.mkSymbol '{', Token.mkString "VAR", Token.mkSymbol '}']
  assertEqual (Token.toString varRef2) "${VAR}" "VariableRef(${VAR})"

  -- Variable with modifier: ${VAR:-default}
  let varRef3 := Token.mkVariableRef [
    Token.mkSymbol '{',
    Token.mkString "VAR",
    Token.mkSymbol ':',
    Token.mkSymbol '-',
    Token.mkLiteral [Token.mkString "default"],
    Token.mkSymbol '}'
  ]
  assertEqual (Token.toString varRef3) "${VAR:-default}" "VariableRef(${VAR:-default})"

  -- Empty variable ref (edge case)
  let varRef4 := Token.mkVariableRef []
  assertEqual (Token.toString varRef4) "$" "VariableRef(empty) == \"$\""

-- ============================================================================
-- Property 4: Quoted tokens wrap in quote characters
-- ============================================================================

def testQuotedTokenWrapping : IO Unit := do
  IO.println "Property 4: Quoted tokens wrap in quote characters"

  -- Double-quoted literal
  let quotedLit := Token.mkLiteral [Token.mkString "hello world"]
    (some { quoteChar := '"' })
  assertEqual (Token.toString quotedLit) "\"hello world\"" "Quoted literal with double quotes"

  -- Single-quoted literal
  let quotedLit2 := Token.mkLiteral [Token.mkString "hello"]
    (some { quoteChar := '\'' })
  assertEqual (Token.toString quotedLit2) "'hello'" "Quoted literal with single quotes"

  -- Quoted identifier
  let quotedId := Token.mkIdentifier [Token.mkString "myvar"]
    (some { quoteChar := '"' })
  assertEqual (Token.toString quotedId) "\"myvar\"" "Quoted identifier"

  -- Unquoted literal (no wrapping)
  let unquotedLit := Token.mkLiteral [Token.mkString "hello"]
  assertEqual (Token.toString unquotedLit) "hello" "Unquoted literal — no wrapping"

-- ============================================================================
-- Property 5: Dockerfile toString == concat constructs toString
-- ============================================================================

def testDockerfileConcat : IO Unit := do
  IO.println "Property 5: Dockerfile toString == concat constructs"

  -- Build a simple Dockerfile: FROM alpine\nRUN echo hello\n
  let fromInstr := Instruction.mkSimple .from [Token.mkLiteral [Token.mkString "alpine"]]
  let nlToken := Token.mkNewLine "\n"

  let runInstr := Instruction.mkSimple .run [Token.mkLiteral [Token.mkString "echo hello"]]

  let constructs := [
    DockerfileConstruct.fromInstruction
      { name := .from, token := Token.mkInstruction (fromInstr.token.children ++ [nlToken]) },
    DockerfileConstruct.fromInstruction
      { name := .run, token := runInstr.token }
  ]

  let df := Dockerfile.mk constructs
  let expected := String.join (constructs.map DockerfileConstruct.toString)
  assertEqual (Dockerfile.toString df) expected "Dockerfile toString == concat constructs"

  -- Empty Dockerfile
  let emptyDf := Dockerfile.empty
  assertEqual (Dockerfile.toString emptyDf) "" "Empty Dockerfile == \"\""

  -- Dockerfile with comment
  let commentToken := Token.mkComment [Token.mkSymbol '#', Token.mkString " test comment", Token.mkNewLine "\n"]
  let wsToken := Token.mkConstruct [Token.mkWhitespace "\n"]
  let dfWithComment := Dockerfile.mk [
    DockerfileConstruct.mkComment commentToken,
    DockerfileConstruct.mkWhitespace wsToken,
    DockerfileConstruct.fromInstruction fromInstr
  ]
  let expected2 := String.join ([commentToken, wsToken, fromInstr.token].map Token.toString)
  assertEqual (Dockerfile.toString dfWithComment) expected2 "Dockerfile with comment and whitespace"

-- ============================================================================
-- Property 6: Instruction name keyword mapping
-- ============================================================================

def testInstructionNames : IO Unit := do
  IO.println "Property 6: All 18 instruction names map to correct keywords"

  let pairs := [
    (InstructionName.from, "FROM"),
    (InstructionName.run, "RUN"),
    (InstructionName.cmd, "CMD"),
    (InstructionName.entrypoint, "ENTRYPOINT"),
    (InstructionName.copy, "COPY"),
    (InstructionName.add, "ADD"),
    (InstructionName.env, "ENV"),
    (InstructionName.arg, "ARG"),
    (InstructionName.expose, "EXPOSE"),
    (InstructionName.volume, "VOLUME"),
    (InstructionName.user, "USER"),
    (InstructionName.workdir, "WORKDIR"),
    (InstructionName.label, "LABEL"),
    (InstructionName.stopSignal, "STOPSIGNAL"),
    (InstructionName.healthCheck, "HEALTHCHECK"),
    (InstructionName.shell, "SHELL"),
    (InstructionName.maintainer, "MAINTAINER"),
    (InstructionName.onBuild, "ONBUILD")
  ]

  for (name, keyword) in pairs do
    assertEqual name.toKeyword keyword s!"InstructionName.{repr name} == \"{keyword}\""

  assertTrue (InstructionName.all.length == 18) "There are exactly 18 instruction types"

-- ============================================================================
-- Property 7: Token tree consistency — recursive structure
-- ============================================================================

/-- Recursively verify that every aggregate token's toString equals
    the concat of its children's toString (accounting for variableRef and quotes). -/
partial def verifyTokenTreeConsistency (t : Token) : IO Unit := do
  match t with
  | .primitive _ _ => pure ()
  | .aggregate kind children quoteInfo =>
    -- Verify children recursively first
    for child in children do
      verifyTokenTreeConsistency child

    -- Compute expected value
    let childConcat := String.join (children.map Token.toString)
    let expectedUnderlying := match kind with
      | .variableRef => "$" ++ childConcat
      | _ => childConcat
    let expected := match quoteInfo with
      | some qi =>
        String.singleton qi.quoteChar ++ expectedUnderlying ++ String.singleton qi.quoteChar
      | none => expectedUnderlying

    let actual := Token.toString t
    if actual != expected then
      throw (IO.Error.userError s!"Token tree inconsistency: expected \"{expected}\", got \"{actual}\"")

def testTokenTreeConsistency : IO Unit := do
  IO.println "Property 7: Token tree consistency — recursive verification"

  -- Build a complex nested token tree mimicking:
  -- FROM --platform=$PLATFORM ubuntu:${TAG:-latest} AS builder
  let platformVar := Token.mkVariableRef [Token.mkString "PLATFORM"]
  let platformFlag := Token.mkKeyValue [
    Token.mkSymbol '-', Token.mkSymbol '-',
    Token.mkKeyword [Token.mkString "platform"],
    Token.mkSymbol '=',
    platformVar
  ]
  let tagVar := Token.mkVariableRef [
    Token.mkSymbol '{',
    Token.mkString "TAG",
    Token.mkSymbol ':',
    Token.mkSymbol '-',
    Token.mkLiteral [Token.mkString "latest"],
    Token.mkSymbol '}'
  ]
  let imageName := Token.mkLiteral [Token.mkString "ubuntu", Token.mkSymbol ':', tagVar]
  let instruction := Token.mkInstruction [
    Token.mkKeyword [Token.mkString "FROM"],
    Token.mkWhitespace " ",
    platformFlag,
    Token.mkWhitespace " ",
    imageName,
    Token.mkWhitespace " ",
    Token.mkKeyword [Token.mkString "AS"],
    Token.mkWhitespace " ",
    Token.mkLiteral [Token.mkString "builder"]
  ]

  verifyTokenTreeConsistency instruction
  IO.println "  PASS: Complex nested token tree is consistent"

  -- Quoted literal with nested variable ref
  let quotedWithVar := Token.mkLiteral
    [Token.mkString "hello ", Token.mkVariableRef [Token.mkString "NAME"]]
    (some { quoteChar := '"' })
  verifyTokenTreeConsistency quotedWithVar
  IO.println "  PASS: Quoted literal with variable ref is consistent"

-- ============================================================================
-- Property 8: Heredoc token toString == concat children
-- ============================================================================

def testHeredocTokenConcat : IO Unit := do
  IO.println "Property 8: Heredoc token toString == concat children"

  -- Simple heredoc body
  let children1 := [Token.mkString "echo hello\n", Token.mkString "EOF", Token.mkNewLine "\n"]
  let heredoc1 := Token.mkHeredoc children1
  let expected1 := String.join (children1.map Token.toString)
  assertEqual (Token.toString heredoc1) expected1 "Heredoc(echo hello) == concat children"

  -- Empty heredoc
  let heredoc2 := Token.mkHeredoc []
  assertEqual (Token.toString heredoc2) "" "Empty heredoc == \"\""

  -- Heredoc with multiple lines
  let children3 := [
    Token.mkString "line1\n",
    Token.mkString "line2\n",
    Token.mkString "MARKER",
    Token.mkNewLine "\n"
  ]
  let heredoc3 := Token.mkHeredoc children3
  let expected3 := String.join (children3.map Token.toString)
  assertEqual (Token.toString heredoc3) expected3 "Multi-line heredoc == concat children"

  -- Heredoc with nested variable ref (should concat without $ prefix since heredoc kind)
  let varRef := Token.mkVariableRef [Token.mkString "HOME"]
  let children4 := [Token.mkString "echo ", varRef, Token.mkNewLine "\n"]
  let heredoc4 := Token.mkHeredoc children4
  let expected4 := String.join (children4.map Token.toString)
  assertEqual (Token.toString heredoc4) expected4 "Heredoc with variable ref == concat children"

  -- Verify heredoc follows same concatenation rules as other aggregate kinds
  -- (i.e., no "$" prefix, no quote wrapping — plain concatenation)
  let litChildren := [Token.mkString "content\n"]
  let asLiteral := Token.mkLiteral litChildren
  let asHeredoc := Token.mkHeredoc litChildren
  assertEqual (Token.toString asLiteral) (Token.toString asHeredoc)
    "Heredoc and Literal with same children have same toString"

-- ============================================================================
-- Property 9: Heredoc tokens follow aggregate concatenation rules
-- ============================================================================

def testHeredocAggregateConsistency : IO Unit := do
  IO.println "Property 9: Heredoc tokens follow aggregate concatenation rules"

  -- Build a heredoc token tree and verify it via the recursive consistency checker
  let heredocBody := Token.mkHeredoc [
    Token.mkString "#!/bin/bash\n",
    Token.mkString "set -e\n",
    Token.mkString "echo 'done'\n",
    Token.mkString "SCRIPT",
    Token.mkNewLine "\n"
  ]
  verifyTokenTreeConsistency heredocBody
  IO.println "  PASS: Heredoc token tree is consistent"

  -- Heredoc inside an instruction token
  let instrWithHeredoc := Token.mkInstruction [
    Token.mkKeyword [Token.mkString "RUN"],
    Token.mkWhitespace " ",
    Token.mkString "<<EOF",
    Token.mkNewLine "\n",
    heredocBody
  ]
  verifyTokenTreeConsistency instrWithHeredoc
  IO.println "  PASS: Instruction with heredoc is consistent"

  -- Heredoc kind is not variableRef, so no "$" prefix
  let heredocToken := Token.aggregate .heredoc [Token.mkString "body"] none
  let expected := "body"
  assertEqual (Token.toString heredocToken) expected
    "Heredoc kind does not prepend $ (not variableRef)"

end DockerfileModel.Tests

-- ============================================================================
-- Main test runner (outside namespace so Lake linker finds the entry point)
-- ============================================================================

open DockerfileModel.Tests in
def main : IO Unit := do
  IO.println "=== DockerfileModel Lean Property Tests ==="
  IO.println ""
  testPrimitiveTokenIdentity
  IO.println ""
  testAggregateTokenConcat
  IO.println ""
  testVariableRefPrependsDollar
  IO.println ""
  testQuotedTokenWrapping
  IO.println ""
  testDockerfileConcat
  IO.println ""
  testInstructionNames
  IO.println ""
  testTokenTreeConsistency
  IO.println ""
  testHeredocTokenConcat
  IO.println ""
  testHeredocAggregateConsistency
  IO.println ""
  runParserTests
  IO.println ""
  IO.println "=== All tests passed ==="
