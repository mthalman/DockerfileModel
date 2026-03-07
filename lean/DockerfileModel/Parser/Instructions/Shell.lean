/-
  Parser/Instructions/Shell.lean -- SHELL instruction parser.

  Parses the SHELL instruction:
    SHELL ExecForm

  SHELL only accepts exec form (JSON array). No shell form fallback.
  Example: SHELL ["powershell", "-command"]

  Token structure produced:
    InstructionToken [
      WhitespaceToken?,         -- leading whitespace
      KeywordToken("SHELL"),    -- instruction keyword
      WhitespaceToken(" "),     -- separator
      SymbolToken('['),         -- JSON array tokens
      LiteralToken("..."),
      ...
      SymbolToken(']')
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers
import DockerfileModel.Parser.ExecForm

namespace DockerfileModel.Parser.Instructions.Shell

open DockerfileModel
open DockerfileModel.Parser
open DockerfileModel.Parser.ExecForm

-- ============================================================
-- SHELL args parser
-- ============================================================

/-- Parse the arguments of a SHELL instruction: exec form only.
    Corresponds to ShellInstruction.GetArgsParser() -/
def shellArgsParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (jsonArrayParser escapeChar) escapeChar

-- ============================================================
-- SHELL instruction parser
-- ============================================================

/-- Parse a complete SHELL instruction.
    Corresponds to ShellInstruction.GetInnerParser() -/
def shellInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "SHELL" escapeChar (shellArgsParser escapeChar)

/-- Parse a SHELL instruction and produce an Instruction value. -/
def parseShellInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← shellInstructionParser escapeChar
  Parser.pure {
    name := .shell,
    token := Token.mkInstruction tokens
  }

/-- Parse SHELL instruction from text, returning an Instruction. -/
def parseShell (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseShellInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Shell
