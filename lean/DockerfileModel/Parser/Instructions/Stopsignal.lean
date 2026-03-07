/-
  Parser/Instructions/Stopsignal.lean -- STOPSIGNAL instruction parser.

  Parses the STOPSIGNAL instruction:
    STOPSIGNAL <signal>

  The signal is a name (e.g., SIGTERM) or number (e.g., 9), parsed as a
  literal token with variable substitution support.

  Token structure produced:
    InstructionToken [
      WhitespaceToken?,              -- leading whitespace
      KeywordToken("STOPSIGNAL"),    -- instruction keyword
      WhitespaceToken(" "),          -- separator
      LiteralToken(signal)           -- signal name or number
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers

namespace DockerfileModel.Parser.Instructions.Stopsignal

open DockerfileModel
open DockerfileModel.Parser

-- ============================================================
-- STOPSIGNAL args parser
-- ============================================================

/-- Parse the arguments of a STOPSIGNAL instruction: signal name or number.
    Corresponds to StopSignalInstruction.GetArgsParser() -/
def stopsignalArgsParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    let lit ← literalWithVariables escapeChar
    Parser.pure [lit]) escapeChar

-- ============================================================
-- STOPSIGNAL instruction parser
-- ============================================================

/-- Parse a complete STOPSIGNAL instruction.
    Corresponds to StopSignalInstruction.GetInnerParser() -/
def stopsignalInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "STOPSIGNAL" escapeChar (stopsignalArgsParser escapeChar)

/-- Parse a STOPSIGNAL instruction and produce an Instruction value. -/
def parseStopsignalInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← stopsignalInstructionParser escapeChar
  Parser.pure {
    name := .stopSignal,
    token := Token.mkInstruction tokens
  }

/-- Parse STOPSIGNAL instruction from text, returning an Instruction. -/
def parseStopsignal (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseStopsignalInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Stopsignal
