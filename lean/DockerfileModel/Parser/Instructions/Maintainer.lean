/-
  Parser/Instructions/Maintainer.lean -- MAINTAINER instruction parser.

  Parses the MAINTAINER instruction:
    MAINTAINER <text>

  The argument is everything after MAINTAINER and whitespace to end of line,
  captured as a single LiteralToken. This instruction is deprecated but still
  supported for parsing.

  Token structure produced:
    InstructionToken [
      WhitespaceToken?,            -- leading whitespace
      KeywordToken("MAINTAINER"),  -- instruction keyword
      WhitespaceToken(" "),        -- separator
      LiteralToken(text)           -- maintainer text (rest of line)
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers

namespace DockerfileModel.Parser.Instructions.Maintainer

open DockerfileModel
open DockerfileModel.Parser

-- ============================================================
-- MAINTAINER args parser
-- ============================================================

/-- Parse the arguments of a MAINTAINER instruction: rest-of-line as literal text.
    Corresponds to MaintainerInstruction.GetArgsParser() -/
def maintainerArgsParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    let lit ← literalWithVariables escapeChar [] (whitespaceMode := .allowed)
    Parser.pure [lit]) escapeChar

-- ============================================================
-- MAINTAINER instruction parser
-- ============================================================

/-- Parse a complete MAINTAINER instruction.
    Corresponds to MaintainerInstruction.GetInnerParser() -/
def maintainerInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "MAINTAINER" escapeChar (maintainerArgsParser escapeChar)

/-- Parse a MAINTAINER instruction and produce an Instruction value. -/
def parseMaintainerInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← maintainerInstructionParser escapeChar
  Parser.pure {
    name := .maintainer,
    token := Token.mkInstruction tokens
  }

/-- Parse MAINTAINER instruction from text, returning an Instruction. -/
def parseMaintainer (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseMaintainerInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Maintainer
