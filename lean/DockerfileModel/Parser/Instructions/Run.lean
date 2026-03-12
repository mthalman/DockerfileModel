/-
  Parser/Instructions/Run.lean -- RUN instruction parser.

  Parses the RUN instruction:
    RUN [RunFlags] (ExecForm | ShellForm)

  RunFlags (optional, any order, before the command):
    --mount=MountSpec      — string flag, repeatable (opaque literal value)
    --network=NetworkValue — string flag via flagParser "network"
    --security=SecurityVal — string flag via flagParser "security"

  After flags, try exec form (JSON array) first, fall back to shell form.

  Token structure produced:
    InstructionToken [
      WhitespaceToken?,        -- leading whitespace
      KeywordToken("RUN"),     -- instruction keyword
      WhitespaceToken(" "),    -- separator
      KeyValueToken(--mount=...), -- optional mount flags (repeatable)
      WhitespaceToken?,
      KeyValueToken(--network=...), -- optional network flag
      WhitespaceToken?,
      KeyValueToken(--security=...), -- optional security flag
      WhitespaceToken?,
      SymbolToken('[') | LiteralToken(command) -- exec or shell form
      ...
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers
import DockerfileModel.Parser.ExecForm
import DockerfileModel.Parser.Flags
import DockerfileModel.Parser.Heredoc

namespace DockerfileModel.Parser.Instructions.Run

open DockerfileModel
open DockerfileModel.Parser
open Parser
open DockerfileModel.Parser.ExecForm
open DockerfileModel.Parser.Flags
open DockerfileModel.Parser.Heredoc

-- ============================================================
-- RUN flag parsers
-- ============================================================

/-- Parse a single RUN flag: --mount=..., --network=..., or --security=...
    Mount uses `flagParserStrict` (no whitespace absorption) because C#'s
    mount value parser is structured (MountParser) and rejects empty values.
    Network and security use the regular `flagParser` which absorbs whitespace.
    Returns the flag token wrapped in argTokens (with leading whitespace). -/
private def runFlagParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    let flag ← or' (flagParserStrict "mount" escapeChar)
               (or' (flagParser "network" escapeChar)
                    (flagParser "security" escapeChar))
    Parser.pure [flag]) escapeChar (excludeTrailingWhitespace := true)

-- ============================================================
-- RUN args parser
-- ============================================================

/-- Parse the arguments of a RUN instruction: [flags] (heredoc | exec form | shell form).
    Flags can appear in any order and some are repeatable (mount).
    After flags, try heredoc (<<DELIM) first, then exec form, then shell form.
    Corresponds to RunInstruction.GetArgsParser() -/
partial def runArgsParser (escapeChar : Char) : Parser (List Token) := do
  -- Parse 0+ flags in any order
  let flags ← many (runFlagParser escapeChar)
  -- Then parse command: heredoc, exec form, or shell form
  let command ← argTokens
    (or' heredocInstructionArg
      (or' (jsonArrayParser escapeChar) (shellFormCommand escapeChar)))
    escapeChar
  Parser.pure (concatTokens [flags.flatten, command])

-- ============================================================
-- RUN instruction parser
-- ============================================================

/-- Parse a complete RUN instruction.
    Corresponds to RunInstruction.GetInnerParser() -/
def runInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "RUN" escapeChar (runArgsParser escapeChar)

/-- Parse a RUN instruction and produce an Instruction value. -/
def parseRunInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← runInstructionParser escapeChar
  Parser.pure {
    name := .run,
    token := Token.mkInstruction tokens
  }

/-- Parse RUN instruction from text, returning an Instruction. -/
def parseRun (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseRunInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Run
