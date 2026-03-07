/-
  Parser/Instructions/Workdir.lean -- WORKDIR instruction parser.

  Parses the WORKDIR instruction:
    WORKDIR <path>

  The path argument supports variable substitution ($VAR, ${VAR}).

  Token structure produced:
    InstructionToken [
      WhitespaceToken?,          -- leading whitespace
      KeywordToken("WORKDIR"),   -- instruction keyword
      WhitespaceToken(" "),      -- separator
      LiteralToken(path)         -- path (may contain variable refs)
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers

namespace DockerfileModel.Parser.Instructions.Workdir

open DockerfileModel
open DockerfileModel.Parser

-- ============================================================
-- WORKDIR args parser
-- ============================================================

/-- Parse the arguments of a WORKDIR instruction: a path with variable substitution.
    Corresponds to WorkdirInstruction.GetArgsParser() -/
def workdirArgsParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    let lit ← literalWithVariables escapeChar [] (whitespaceMode := .allowed)
    Parser.pure [lit]) escapeChar

-- ============================================================
-- WORKDIR instruction parser
-- ============================================================

/-- Parse a complete WORKDIR instruction.
    Corresponds to WorkdirInstruction.GetInnerParser() -/
def workdirInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "WORKDIR" escapeChar (workdirArgsParser escapeChar)

/-- Parse a WORKDIR instruction and produce an Instruction value. -/
def parseWorkdirInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← workdirInstructionParser escapeChar
  Parser.pure {
    name := .workdir,
    token := Token.mkInstruction tokens
  }

/-- Parse WORKDIR instruction from text, returning an Instruction. -/
def parseWorkdir (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseWorkdirInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Workdir
