/-
  Parser/Instructions/Expose.lean -- EXPOSE instruction parser.

  Parses the EXPOSE instruction:
    EXPOSE <port-spec> [<port-spec> ...]

  Each port spec is parsed as a single opaque literal that may include an
  optional protocol suffix (e.g., "80", "80/tcp", "443/udp"). The entire
  port/protocol string, including the '/', is captured as one literal token
  — it is not split into separate port, separator, and protocol tokens.
  Port specs can contain variable references (e.g., "$PORT/$PROTO").
  Multiple port specs are separated by whitespace.

  Token structure produced:
    InstructionToken [
      WhitespaceToken?,          -- leading whitespace
      KeywordToken("EXPOSE"),    -- instruction keyword
      WhitespaceToken(" "),      -- separator
      LiteralToken(port-spec),   -- port spec (flat, e.g., "80" or "80/tcp")
      WhitespaceToken?,          -- separator between port specs
      LiteralToken(port-spec),   -- additional port spec
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
