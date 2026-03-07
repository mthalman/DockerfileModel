/-
  Parser/Instructions/Cmd.lean -- CMD instruction parser.

  Parses the CMD instruction:
    CMD (ExecForm | ShellForm)

  ExecForm: CMD ["executable","param1","param2"]
  ShellForm: CMD command param1 param2

  The parser tries exec form (JSON array) first, then falls back to shell form.

  Token structure produced (exec form):
    InstructionToken [
      WhitespaceToken?,        -- leading whitespace
      KeywordToken("CMD"),     -- instruction keyword
      WhitespaceToken(" "),    -- separator
      SymbolToken('['),        -- JSON array tokens
      LiteralToken("..."),
      ...
      SymbolToken(']')
    ]

  Token structure produced (shell form):
    InstructionToken [
      WhitespaceToken?,        -- leading whitespace
      KeywordToken("CMD"),     -- instruction keyword
      WhitespaceToken(" "),    -- separator
      LiteralToken(command)    -- shell command text
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers
import DockerfileModel.Parser.ExecForm

namespace DockerfileModel.Parser.Instructions.Cmd

open DockerfileModel
open DockerfileModel.Parser
open Parser
open DockerfileModel.Parser.ExecForm

-- ============================================================
-- CMD args parser
-- ============================================================

/-- Parse the arguments of a CMD instruction: exec form or shell form.
    Try JSON array first, fall back to shell form command.
    Corresponds to CmdInstruction.GetArgsParser() -/
partial def cmdArgsParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (or' (jsonArrayParser escapeChar) (shellFormCommand escapeChar)) escapeChar

-- ============================================================
-- CMD instruction parser
-- ============================================================

/-- Parse a complete CMD instruction.
    Corresponds to CmdInstruction.GetInnerParser() -/
def cmdInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "CMD" escapeChar (cmdArgsParser escapeChar)

/-- Parse a CMD instruction and produce an Instruction value. -/
def parseCmdInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← cmdInstructionParser escapeChar
  Parser.pure {
    name := .cmd,
    token := Token.mkInstruction tokens
  }

/-- Parse CMD instruction from text, returning an Instruction. -/
def parseCmd (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseCmdInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Cmd
