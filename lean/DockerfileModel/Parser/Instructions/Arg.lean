/-
  Parser/Instructions/Arg.lean -- ARG instruction parser.

  Parses the ARG instruction:
    ARG <name>[=<default>]
    ARG <name1>[=<val1>] <name2>[=<val2>] ...

  This mirrors ArgInstruction.cs:
  - GetInnerParser: Instruction("ARG", escapeChar, GetArgsParser(escapeChar))
  - GetArgsParser: optional whitespace + VariablesParser
  - VariablesParser: ArgTokens(whitespace? + ArgDeclaration, escapeChar).AtLeastOnce().Flatten()

  The parser produces a Token tree matching the C# token structure:
    InstructionToken [
      WhitespaceToken?,           -- leading whitespace
      KeywordToken("ARG"),        -- instruction keyword
      WhitespaceToken(" "),       -- separator
      KeyValueToken [             -- arg declaration
        IdentifierToken(name),    -- variable name
        SymbolToken('=')?,        -- optional assignment operator
        LiteralToken(value)?      -- optional value (may contain variable refs)
      ],
      WhitespaceToken?,           -- optional separator
      KeyValueToken?,             -- additional arg declarations
      ...
    ]

  The ARG instruction validates variable handling:
  - Variable names are identifiers (letter/underscore + alphanumeric/underscore)
  - Default values may contain variable references ($VAR, ${VAR:-default})
  - Multiple ARG declarations can appear on a single line
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers

namespace DockerfileModel.Parser.Instructions

open DockerfileModel
open DockerfileModel.Parser

-- ============================================================
-- ARG variables parser
-- ============================================================

/-- Parse one or more ARG declarations separated by whitespace.
    Corresponds to ArgInstruction.VariablesParser() -/
def argVariablesParser (escapeChar : Char) : Parser (List Token) := do
  let declLists ← atLeastOnce (
    argTokens (do
      let ws ← Parser.optional whitespace
      let decl ← argDeclarationParser escapeChar
      Parser.pure (concatTokens [ws.getD [], [decl]])) escapeChar)
  Parser.pure declLists.flatten

-- ============================================================
-- ARG args parser
-- ============================================================

/-- Parse the arguments of an ARG instruction.
    Corresponds to ArgInstruction.GetArgsParser() -/
def argArgsParser (escapeChar : Char) : Parser (List Token) := do
  let ws ← Parser.optional whitespace
  let variables ← argVariablesParser escapeChar
  Parser.pure (concatTokens [ws.getD [], variables])

-- ============================================================
-- ARG instruction parser
-- ============================================================

/-- Parse a complete ARG instruction.
    Corresponds to ArgInstruction.GetInnerParser() -/
def argInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "ARG" escapeChar (argArgsParser escapeChar)

/-- Parse an ARG instruction and produce an Instruction value. -/
def parseArgInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← argInstructionParser escapeChar
  Parser.pure {
    name := .arg,
    token := Token.mkInstruction tokens
  }

/-- Parse ARG instruction from text, returning an Instruction. -/
def parseArg (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseArgInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions
