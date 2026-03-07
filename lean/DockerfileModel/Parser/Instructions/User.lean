/-
  Parser/Instructions/User.lean -- USER instruction parser.

  Parses the USER instruction:
    USER <username>[:<group>]

  The username is parsed as a literal (excluding ':' from valid chars).
  If ':' is present, the group name follows. When ':' is present, the
  result is wrapped as a KeyValueToken; otherwise just a LiteralToken.

  Token structure produced (without group):
    InstructionToken [
      WhitespaceToken?,        -- leading whitespace
      KeywordToken("USER"),    -- instruction keyword
      WhitespaceToken(" "),    -- separator
      LiteralToken(username)   -- username
    ]

  Token structure produced (with group):
    InstructionToken [
      WhitespaceToken?,          -- leading whitespace
      KeywordToken("USER"),      -- instruction keyword
      WhitespaceToken(" "),      -- separator
      KeyValueToken [            -- user:group pair
        LiteralToken(username),  -- username
        SymbolToken(':'),        -- separator
        LiteralToken(group)      -- group name
      ]
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

/-- Parse the arguments of a USER instruction: username[:group].
    Corresponds to UserInstruction.GetArgsParser() -/
def userArgsParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    let username ← literalWithVariables escapeChar [':']
    let groupPart ← Parser.optional (do
      let colon ← symbolParser ':'
      let group ← literalWithVariables escapeChar
      Parser.pure (colon, group))
    match groupPart with
    | some (colon, group) =>
      Parser.pure [Token.mkKeyValue [username, colon, group]]
    | none =>
      Parser.pure [username]
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
