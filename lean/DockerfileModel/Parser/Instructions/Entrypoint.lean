/-
  Parser/Instructions/Entrypoint.lean -- ENTRYPOINT instruction parser.

  Parses the ENTRYPOINT instruction:
    ENTRYPOINT (ExecForm | ShellForm)

  ExecForm: ENTRYPOINT ["executable","param1","param2"]
  ShellForm: ENTRYPOINT command param1 param2

  The parser tries exec form (JSON array) first, then falls back to shell form.
  Structurally identical to CMD.

  Token structure produced (exec form):
    InstructionToken [
      WhitespaceToken?,              -- leading whitespace
      KeywordToken("ENTRYPOINT"),    -- instruction keyword
      WhitespaceToken(" "),          -- separator
      SymbolToken('['),              -- JSON array tokens
      LiteralToken("..."),
      ...
      SymbolToken(']')
    ]

  Token structure produced (shell form):
    InstructionToken [
      WhitespaceToken?,              -- leading whitespace
      KeywordToken("ENTRYPOINT"),    -- instruction keyword
      WhitespaceToken(" "),          -- separator
      LiteralToken(command)          -- shell command text
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers
import DockerfileModel.Parser.ExecForm

namespace DockerfileModel.Parser.Instructions.Entrypoint

open DockerfileModel
open DockerfileModel.Parser
open Parser
open DockerfileModel.Parser.ExecForm

-- ============================================================
-- ENTRYPOINT args parser
-- ============================================================

/-- Parse the arguments of an ENTRYPOINT instruction: exec form or shell form.
    Try JSON array first, fall back to shell form command.
    Corresponds to EntrypointInstruction.GetArgsParser() -/
partial def entrypointArgsParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (or' (jsonArrayParser escapeChar) (shellFormCommand escapeChar)) escapeChar

-- ============================================================
-- ENTRYPOINT instruction parser
-- ============================================================

/-- Parse a complete ENTRYPOINT instruction.
    Corresponds to EntrypointInstruction.GetInnerParser() -/
def entrypointInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "ENTRYPOINT" escapeChar (entrypointArgsParser escapeChar)

/-- Parse an ENTRYPOINT instruction and produce an Instruction value. -/
def parseEntrypointInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← entrypointInstructionParser escapeChar
  Parser.pure {
    name := .entrypoint,
    token := Token.mkInstruction tokens
  }

/-- Parse ENTRYPOINT instruction from text, returning an Instruction. -/
def parseEntrypoint (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseEntrypointInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Entrypoint
