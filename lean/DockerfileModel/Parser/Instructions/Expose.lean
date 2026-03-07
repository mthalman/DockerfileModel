/-
  Parser/Instructions/Expose.lean -- EXPOSE instruction parser.

  Parses the EXPOSE instruction:
    EXPOSE <port>[/<protocol>] [<port>[/<protocol>] ...]

  Port is a literal (can contain variables). Protocol is optional: 'tcp' or 'udp'
  after '/'. Multiple port specs are separated by whitespace.

  Token structure produced:
    InstructionToken [
      WhitespaceToken?,          -- leading whitespace
      KeywordToken("EXPOSE"),    -- instruction keyword
      WhitespaceToken(" "),      -- separator
      LiteralToken(port),        -- port spec (may be KeyValueToken if /proto)
      WhitespaceToken?,          -- separator between port specs
      LiteralToken(port),        -- additional port spec
      ...
    ]

  A port spec with protocol produces:
    KeyValueToken [
      LiteralToken(port),
      SymbolToken('/'),
      LiteralToken(protocol)
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers

namespace DockerfileModel.Parser.Instructions.Expose

open DockerfileModel
open DockerfileModel.Parser
open Parser

-- ============================================================
-- Port spec parser
-- ============================================================

/-- Parse a single port specification: port[/protocol].
    Port is a literal (excluding '/' and whitespace). Protocol is optional. -/
def portSpecParser (escapeChar : Char) : Parser Token := do
  let port ← literalWithVariables escapeChar ['/']
  let protoPart ← Parser.optional (do
    let slash ← symbolParser '/'
    let proto ← literalWithVariables escapeChar
    Parser.pure (slash, proto))
  match protoPart with
  | some (slash, proto) =>
    Parser.pure (Token.mkKeyValue [port, slash, proto])
  | none =>
    Parser.pure port

-- ============================================================
-- EXPOSE args parser
-- ============================================================

/-- Parse the arguments of an EXPOSE instruction: one or more port specs.
    Corresponds to ExposeInstruction.GetArgsParser() -/
def exposeArgsParser (escapeChar : Char) : Parser (List Token) := do
  let firstSpec ← argTokens (do
    let spec ← portSpecParser escapeChar
    Parser.pure [spec]) escapeChar
  let restSpecs ← many (argTokens (do
    let spec ← portSpecParser escapeChar
    Parser.pure [spec]) escapeChar)
  Parser.pure (concatTokens (firstSpec :: restSpecs))

-- ============================================================
-- EXPOSE instruction parser
-- ============================================================

/-- Parse a complete EXPOSE instruction.
    Corresponds to ExposeInstruction.GetInnerParser() -/
def exposeInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "EXPOSE" escapeChar (exposeArgsParser escapeChar)

/-- Parse an EXPOSE instruction and produce an Instruction value. -/
def parseExposeInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← exposeInstructionParser escapeChar
  Parser.pure {
    name := .expose,
    token := Token.mkInstruction tokens
  }

/-- Parse EXPOSE instruction from text, returning an Instruction. -/
def parseExpose (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseExposeInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Expose
