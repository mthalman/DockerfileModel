/-
  Parser/Instructions/User.lean -- USER instruction parser.

  Parses the USER instruction:
    USER <value>

  BuildKit stores the USER value as a plain string — it does NOT decompose
  `user:group` at parse time. The entire value (including any `:`) is treated
  as a single opaque LiteralToken, matching BuildKit's behavior. Variable
  references are still supported since USER is on BuildKit's variable
  expansion list.

  Token structure produced:
    InstructionToken [
      WhitespaceToken?,        -- leading whitespace
      KeywordToken("USER"),    -- instruction keyword
      WhitespaceToken(" "),    -- separator
      LiteralToken(value)      -- opaque user value (may contain ':')
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers

namespace DockerfileModel.Parser.Instructions.User

open DockerfileModel
open DockerfileModel.Parser

-- ============================================================
-- USER args parser
-- ============================================================

/-- Parse the arguments of a USER instruction: the entire value as an opaque literal.
    Corresponds to UserInstruction.GetArgsParser() -/
def userArgsParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    let value ← literalWithVariables escapeChar
    Parser.pure [value]
  ) escapeChar

-- ============================================================
-- USER instruction parser
-- ============================================================

/-- Parse a complete USER instruction.
    Corresponds to UserInstruction.GetInnerParser() -/
def userInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "USER" escapeChar (userArgsParser escapeChar)

/-- Parse a USER instruction and produce an Instruction value. -/
def parseUserInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← userInstructionParser escapeChar
  Parser.pure {
    name := .user,
    token := Token.mkInstruction tokens
  }

/-- Parse USER instruction from text, returning an Instruction. -/
def parseUser (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseUserInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.User
