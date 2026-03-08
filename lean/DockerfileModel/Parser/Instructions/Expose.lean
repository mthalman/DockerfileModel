/-
  Parser/Instructions/Expose.lean -- EXPOSE instruction parser.

  Parses the EXPOSE instruction:
    EXPOSE <port>[/<protocol>] [<port>[/<protocol>] ...]

  Port is a literal (can contain variables). Protocol is optional: 'tcp' or 'udp'
  after '/'. Multiple port specs are separated by whitespace.

  BuildKit treats port/protocol specs (e.g., 80/tcp) as single opaque values,
  not key-value pairs. The '/' is part of the port specification syntax.

  Token structure produced:
    InstructionToken [
      WhitespaceToken?,          -- leading whitespace
      KeywordToken("EXPOSE"),    -- instruction keyword
      WhitespaceToken(" "),      -- separator
      LiteralToken(port),        -- port spec (flat, e.g., "80" or "80/tcp")
      WhitespaceToken?,          -- separator between port specs
      LiteralToken(port),        -- additional port spec
      ...
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
    BuildKit treats the entire port/protocol spec (e.g., "80/tcp") as a single
    opaque value. The '/' is part of the literal text, not a key-value separator. -/
def portSpecParser (escapeChar : Char) : Parser Token :=
  literalWithVariables escapeChar

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
