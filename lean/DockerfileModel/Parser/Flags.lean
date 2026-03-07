/-
  Parser/Flags.lean -- Generic flag parsers for Dockerfile instructions.

  Provides reusable combinators for parsing instruction flags:
  - `flagParser` — parses `--name=value` (defined in DockerfileParsers.lean, re-exported here)
  - `booleanFlagParser` — parses `--name` (boolean flags like --link, --keep-git-dir)

  The generic `flagParser` is defined in DockerfileParsers.lean (to avoid circular
  imports, since it depends on `keywordParser` and `literalWithVariables`). This module
  adds `booleanFlagParser` and serves as the single import point for flag parsing.

  Token structures:

  flagParser "platform" produces:
    KeyValueToken [
      SymbolToken('-'),
      SymbolToken('-'),
      KeywordToken("platform"),
      SymbolToken('='),
      LiteralToken(value)
    ]

  booleanFlagParser "link" produces:
    KeyValueToken [
      SymbolToken('-'),
      SymbolToken('-'),
      KeywordToken("link")
    ]

  booleanFlagParser "link" with explicit value produces:
    KeyValueToken [
      SymbolToken('-'),
      SymbolToken('-'),
      KeywordToken("link"),
      SymbolToken('='),
      LiteralToken("true"|"false")
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers

namespace DockerfileModel.Parser.Flags

open DockerfileModel
open DockerfileModel.Parser
open Parser

-- Note: `flagParser` (key-value flag: --name=value) is defined in
-- DockerfileParsers.lean and available via `open DockerfileModel.Parser`.
-- It can be used as `DockerfileModel.Parser.flagParser` or just `flagParser`
-- when the namespace is opened.

-- ============================================================
-- Boolean flag: --name (optionally --name=true/false)
-- ============================================================

/-- Parse an explicit boolean value (true or false) as a LiteralToken. -/
private def booleanValueParser (escapeChar : Char) : Parser Token :=
  or' (do let _ ← keywordParser "true" escapeChar
          Parser.pure (Token.mkLiteral [Token.mkString "true"]))
      (do let _ ← keywordParser "false" escapeChar
          Parser.pure (Token.mkLiteral [Token.mkString "false"]))

/-- Parse a boolean flag: `--name` (no value) or `--name=true` / `--name=false`.
    Without value, returns tokens: SymbolToken('-'), SymbolToken('-'), KeywordToken(name)
    With value, returns tokens: SymbolToken('-'), SymbolToken('-'), KeywordToken(name),
                                SymbolToken('='), LiteralToken("true"|"false")

    Corresponds to the C# `BooleanFlag` pattern. -/
def booleanFlagParser (name : String) (escapeChar : Char) : Parser Token := do
  let dash1 ← char '-'
  let dash2 ← char '-'
  let kw ← keywordParser name escapeChar
  -- Try to parse optional =true/=false
  let valueOpt ← optional (do
    let eq ← char '='
    let val ← booleanValueParser escapeChar
    Parser.pure (eq, val))
  match valueOpt with
  | none =>
    Parser.pure (Token.mkKeyValue [
      Token.mkSymbol dash1,
      Token.mkSymbol dash2,
      kw
    ])
  | some (eq, val) =>
    Parser.pure (Token.mkKeyValue [
      Token.mkSymbol dash1,
      Token.mkSymbol dash2,
      kw,
      Token.mkSymbol eq,
      val
    ])

end DockerfileModel.Parser.Flags
