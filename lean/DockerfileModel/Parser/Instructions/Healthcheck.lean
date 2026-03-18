/-
  Parser/Instructions/Healthcheck.lean -- HEALTHCHECK instruction parser.

  Parses the HEALTHCHECK instruction in two forms:
    1. HEALTHCHECK NONE
    2. HEALTHCHECK [HealthCheckFlags] CMD (ExecForm | ShellForm)

  HealthCheckFlags (optional, any order, before CMD keyword):
    --interval=Duration       — string flag via flagParser "interval"
    --timeout=Duration        — string flag via flagParser "timeout"
    --start-period=Duration   — string flag via flagParser "start-period"
    --start-interval=Duration — string flag via flagParser "start-interval"
    --retries=Integer         — string flag via flagParser "retries"

  Token structure produced (NONE form):
    InstructionToken [
      WhitespaceToken?,                -- leading whitespace
      KeywordToken("HEALTHCHECK"),     -- instruction keyword
      WhitespaceToken(" "),            -- separator
      KeywordToken("NONE")             -- NONE keyword
    ]

  Token structure produced (CMD form):
    InstructionToken [
      WhitespaceToken?,                -- leading whitespace
      KeywordToken("HEALTHCHECK"),     -- instruction keyword
      WhitespaceToken(" "),            -- separator
      KeyValueToken(--interval=...),   -- optional flags
      ...
      KeywordToken("CMD"),             -- CMD keyword
      WhitespaceToken(" "),            -- separator
      SymbolToken('[') | LiteralToken  -- exec or shell form
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

namespace DockerfileModel.Parser.Instructions.Healthcheck

open DockerfileModel
open DockerfileModel.Parser
open Parser
open DockerfileModel.Parser.ExecForm
open DockerfileModel.Parser.Flags

-- ============================================================
-- HEALTHCHECK flag parsers
-- ============================================================

/-- Parse a single HEALTHCHECK flag: --interval, --timeout, --start-period,
    --start-interval, or --retries.
    Returns the flag token wrapped in argTokens. -/
private def healthcheckFlagParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    let flag ← or' (flagParser "interval" escapeChar)
               (or' (flagParser "timeout" escapeChar)
               (or' (flagParser "start-period" escapeChar)
               (or' (flagParser "start-interval" escapeChar)
                    (flagParser "retries" escapeChar))))
    Parser.pure [flag]) escapeChar

-- ============================================================
-- HEALTHCHECK NONE form
-- ============================================================

/-- Parse the NONE form: just the keyword NONE after HEALTHCHECK.
    Returns tokens for the NONE keyword. -/
private def healthcheckNoneParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    let kw ← keywordParser "NONE" escapeChar
    Parser.pure [kw]) escapeChar

-- ============================================================
-- HEALTHCHECK CMD form
-- ============================================================

/-- Parse the CMD form: [flags] CMD (exec | shell).
    Flags can appear in any order before the CMD keyword.
    After CMD, parse exec form or shell form (same as CMD instruction). -/
private partial def healthcheckCmdParser (escapeChar : Char) : Parser (List Token) := do
  -- Parse 0+ flags in any order
  let flags ← many (healthcheckFlagParser escapeChar)
  -- Parse CMD keyword
  let cmdKw ← argTokens (do
    let kw ← keywordParser "CMD" escapeChar
    Parser.pure [kw]) escapeChar
  -- Parse command: exec form or shell form
  let command ← argTokens (or' (jsonArrayParser escapeChar) (shellFormCommand escapeChar)) escapeChar
  Parser.pure (concatTokens [flags.flatten, cmdKw, command])

-- ============================================================
-- HEALTHCHECK args parser
-- ============================================================

/-- Parse the arguments of a HEALTHCHECK instruction.
    Try NONE form first, fall back to CMD form.
    Corresponds to HealthCheckInstruction.GetArgsParser() -/
partial def healthcheckArgsParser (escapeChar : Char) : Parser (List Token) :=
  or' (healthcheckNoneParser escapeChar) (healthcheckCmdParser escapeChar)

-- ============================================================
-- HEALTHCHECK instruction parser
-- ============================================================

/-- Parse a complete HEALTHCHECK instruction.
    Corresponds to HealthCheckInstruction.GetInnerParser() -/
def healthcheckInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "HEALTHCHECK" escapeChar (healthcheckArgsParser escapeChar)

/-- Parse a HEALTHCHECK instruction and produce an Instruction value. -/
def parseHealthcheckInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← healthcheckInstructionParser escapeChar
  Parser.pure {
    name := .healthCheck,
    token := Token.mkInstruction tokens
  }

/-- Parse HEALTHCHECK instruction from text, returning an Instruction. -/
def parseHealthcheck (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseHealthcheckInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Healthcheck
