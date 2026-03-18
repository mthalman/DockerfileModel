/-
  Parser/Instructions/Env.lean -- ENV instruction parser.

  Parses the ENV instruction in two forms:

  Modern format: ENV Key=Value [Key=Value ...]
    Multiple key=value pairs on one line. Values may be quoted.

  Legacy format: ENV Key Value
    No '=' sign. Single key, rest-of-line as value.

  Detection: If the first non-whitespace word contains '=', use modern format.
  Otherwise, use legacy format.

  Token structure produced (modern):
    InstructionToken [
      WhitespaceToken?,        -- leading whitespace
      KeywordToken("ENV"),     -- instruction keyword
      WhitespaceToken(" "),    -- separator
      KeyValueToken [          -- key=value pair
        IdentifierToken(key),
        SymbolToken('='),
        LiteralToken(value)
      ],
      WhitespaceToken?,        -- separator
      KeyValueToken?,          -- additional pairs
      ...
    ]

  Token structure produced (legacy):
    InstructionToken [
      WhitespaceToken?,        -- leading whitespace
      KeywordToken("ENV"),     -- instruction keyword
      WhitespaceToken(" "),    -- separator
      KeyValueToken [          -- key value pair
        IdentifierToken(key),
        WhitespaceToken(" "),
        LiteralToken(value)    -- rest of line as value
      ]
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers

namespace DockerfileModel.Parser.Instructions.Env

open DockerfileModel
open DockerfileModel.Parser
open Parser

-- ============================================================
-- Key parser for ENV
-- ============================================================

/-- Parse an ENV key: identifier characters (letters, digits, underscores).
    The key excludes '=' so we can detect its boundary. -/
def envKeyParser (escapeChar : Char) : Parser Token := do
  let tokens ← identifierString escapeChar
    (fun c => c.isAlpha || c == '_')
    (fun c => c.isAlpha || c.isDigit || c == '_')
  Parser.pure (Token.mkIdentifier tokens)

-- ============================================================
-- Modern format: Key=Value pairs
-- ============================================================

/-- Parse a single Key=Value pair in modern ENV format. -/
def envKeyValuePairParser (escapeChar : Char) : Parser Token := do
  let key ← envKeyParser escapeChar
  let eq ← symbolParser '='
  let value ← Parser.optional (literalWithVariables escapeChar [] .allowedInQuotes)
  let children := concatTokens [
    [key, eq],
    match value with | some v => [v] | none => []
  ]
  Parser.pure (Token.mkKeyValue children)

/-- Parse modern ENV format: one or more Key=Value pairs separated by whitespace. -/
def envModernParser (escapeChar : Char) : Parser (List Token) := do
  let firstPairTokens ← argTokens (do
    let pair ← envKeyValuePairParser escapeChar
    Parser.pure [pair]) escapeChar
  let restPairTokens ← many (argTokens (do
    let pair ← envKeyValuePairParser escapeChar
    Parser.pure [pair]) escapeChar)
  Parser.pure (concatTokens (firstPairTokens :: restPairTokens))

-- ============================================================
-- Legacy format: Key WS Value
-- ============================================================

/-- Parse legacy ENV format: key followed by whitespace then rest-of-line as value. -/
def envLegacyParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    let key ← envKeyParser escapeChar
    let ws ← whitespace
    let value ← literalWithVariables escapeChar [] (whitespaceMode := .allowed)
    Parser.pure [Token.mkKeyValue (concatTokens [[key], ws, [value]])]
  ) escapeChar

-- ============================================================
-- ENV args parser (dispatches between modern and legacy)
-- ============================================================

/-- Parse the arguments of an ENV instruction: modern or legacy format.
    Try modern format first (looks for '='), fall back to legacy.
    Corresponds to EnvInstruction.GetArgsParser() -/
def envArgsParser (escapeChar : Char) : Parser (List Token) :=
  or' (envModernParser escapeChar) (envLegacyParser escapeChar)

-- ============================================================
-- ENV instruction parser
-- ============================================================

/-- Parse a complete ENV instruction.
    Corresponds to EnvInstruction.GetInnerParser() -/
def envInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "ENV" escapeChar (envArgsParser escapeChar)

/-- Parse an ENV instruction and produce an Instruction value. -/
def parseEnvInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← envInstructionParser escapeChar
  Parser.pure {
    name := .env,
    token := Token.mkInstruction tokens
  }

/-- Parse ENV instruction from text, returning an Instruction. -/
def parseEnv (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseEnvInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Env
