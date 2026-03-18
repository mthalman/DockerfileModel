/-
  Parser/Instructions/Label.lean -- LABEL instruction parser.

  Parses the LABEL instruction:
    LABEL Key=Value [Key=Value ...]

  Keys may contain dots and hyphens (e.g., com.example.version, maintainer-name).
  Values may be quoted or unquoted literals with variable substitution.

  Token structure produced:
    InstructionToken [
      WhitespaceToken?,         -- leading whitespace
      KeywordToken("LABEL"),    -- instruction keyword
      WhitespaceToken(" "),     -- separator
      KeyValueToken [           -- key=value pair
        IdentifierToken(key),   -- key (alphanumeric + dots + hyphens)
        SymbolToken('='),
        LiteralToken(value)     -- value (quoted or unquoted)
      ],
      WhitespaceToken?,         -- separator
      KeyValueToken?,           -- additional pairs
      ...
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers

namespace DockerfileModel.Parser.Instructions.Label

open DockerfileModel
open DockerfileModel.Parser
open Parser

-- ============================================================
-- Label key parser
-- ============================================================

/-- Parse a LABEL key: alphanumeric characters plus dots, hyphens, and underscores. -/
def labelKeyParser (escapeChar : Char) : Parser Token := do
  let tokens ← identifierString escapeChar
    (fun c => c.isAlpha || c == '_' || c == '.')
    (fun c => c.isAlpha || c.isDigit || c == '_' || c == '-' || c == '.')
  Parser.pure (Token.mkIdentifier tokens)

-- ============================================================
-- Label key=value pair parser
-- ============================================================

/-- Parse a single Key=Value pair in LABEL format. -/
def labelKeyValuePairParser (escapeChar : Char) : Parser Token := do
  let key ← labelKeyParser escapeChar
  let eq ← symbolParser '='
  let value ← Parser.optional (literalWithVariables escapeChar [] .allowedInQuotes)
  let children := concatTokens [
    [key, eq],
    match value with | some v => [v] | none => []
  ]
  Parser.pure (Token.mkKeyValue children)

-- ============================================================
-- LABEL args parser
-- ============================================================

/-- Parse the arguments of a LABEL instruction: one or more Key=Value pairs.
    Corresponds to LabelInstruction.GetArgsParser() -/
def labelArgsParser (escapeChar : Char) : Parser (List Token) := do
  let firstPairTokens ← argTokens (do
    let pair ← labelKeyValuePairParser escapeChar
    Parser.pure [pair]) escapeChar
  let restPairTokens ← many (argTokens (do
    let pair ← labelKeyValuePairParser escapeChar
    Parser.pure [pair]) escapeChar)
  Parser.pure (concatTokens (firstPairTokens :: restPairTokens))

-- ============================================================
-- LABEL instruction parser
-- ============================================================

/-- Parse a complete LABEL instruction.
    Corresponds to LabelInstruction.GetInnerParser() -/
def labelInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "LABEL" escapeChar (labelArgsParser escapeChar)

/-- Parse a LABEL instruction and produce an Instruction value. -/
def parseLabelInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← labelInstructionParser escapeChar
  Parser.pure {
    name := .label,
    token := Token.mkInstruction tokens
  }

/-- Parse LABEL instruction from text, returning an Instruction. -/
def parseLabel (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseLabelInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Label
