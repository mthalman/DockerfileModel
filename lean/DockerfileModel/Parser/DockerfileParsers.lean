/-
  Parser/DockerfileParsers.lean -- Dockerfile-specific combinators from ParseHelper.cs.

  This file translates the C# ParseHelper.cs central grammar hub into Lean 4.
  Every parser here produces Token trees (from Token.lean) rather than raw strings,
  because round-trip fidelity requires preserving the full token structure.

  Key patterns translated:
  - Whitespace handling (preserving whitespace tokens)
  - Line continuation handling (escape char + newline -> LineContinuationToken)
  - Comment parsing
  - Keyword parsing (case-insensitive)
  - Literal/identifier parsing (with quote support)
  - Variable reference parsing ($VAR, ${VAR}, ${VAR:-default}, etc.)
  - Instruction parsing (keyword + args)

  The C# Sprache `from x in p1 from y in p2 select f(x,y)` pattern
  maps to Lean `do let x <- p1; let y <- p2; pure (f x y)`.
-/

import DockerfileModel.Token
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators

namespace DockerfileModel.Parser

open DockerfileModel
open Parser

-- ============================================================
-- Helper: concat tokens (equivalent to ParseHelper.ConcatTokens)
-- ============================================================

/-- Concatenate multiple token lists, filtering out none values.
    Corresponds to ParseHelper.ConcatTokens(params IEnumerable<Token>[]) -/
def concatTokens (tokenLists : List (List Token)) : List Token :=
  tokenLists.flatten

/-- Concatenate optional tokens into a list, filtering nones. -/
def concatOptTokens (tokens : List (Option Token)) : List Token :=
  tokens.filterMap id

-- ============================================================
-- Whitespace parsers
-- ============================================================

/-- Parse whitespace characters (spaces, tabs) but NOT newlines.
    Returns a WhitespaceToken if any whitespace was found, or none.
    Corresponds to ParseHelper.WhitespaceWithoutNewLine() -/
def whitespaceWithoutNewLine : Parser (Option Token) := do
  let chars ← many (satisfy (fun c => c == ' ' || c == '\t') "whitespace (not newline)")
  if chars.isEmpty then
    Parser.pure none
  else
    Parser.pure (some (Token.mkWhitespace (String.ofList chars)))

/-- Parse an optional newline.
    Returns a NewLineToken if found, or none.
    Corresponds to ParseHelper.OptionalNewLine() -/
def optionalNewLine : Parser (Option Token) := do
  let result ← optional lineEnd
  match result with
  | some nl => Parser.pure (some (Token.mkNewLine nl))
  | none => Parser.pure none

/-- Parse a required newline.
    Corresponds to ParseHelper.NewLine() -/
def newLine : Parser Token := do
  let nl ← lineEnd
  Parser.pure (Token.mkNewLine nl)

/-- Parse whitespace: optional non-newline whitespace followed by optional newline.
    Returns a list of tokens (0-2 tokens).
    Corresponds to ParseHelper.Whitespace() -/
def whitespace : Parser (List Token) := do
  let ws ← whitespaceWithoutNewLine
  let nl ← optionalNewLine
  Parser.pure (concatOptTokens [ws, nl])

-- ============================================================
-- Line continuation parsers
-- ============================================================

/-- Parse a single line continuation: escape char + optional whitespace + newline.
    Returns a LineContinuationToken.
    Corresponds to LineContinuationToken.GetParser(escapeChar) -/
def lineContinuationParser (escapeChar : Char) : Parser Token := do
  let escSym ← char escapeChar
  let wsChars ← many (satisfy (fun c => c == ' ' || c == '\t') "whitespace (not newline)")
  let nl ← lineEnd
  let children : List Token := concatOptTokens [
    some (Token.mkSymbol escSym),
    if wsChars.isEmpty then none else some (Token.mkWhitespace (String.ofList wsChars)),
    some (Token.mkNewLine nl)
  ]
  Parser.pure (Token.mkLineContinuation children)

/-- Parse zero or more line continuations.
    Corresponds to ParseHelper.LineContinuations(escapeChar) -/
def lineContinuations (escapeChar : Char) : Parser (List Token) :=
  many (lineContinuationParser escapeChar)

/-- Optionally parse whitespace or line continuations.
    Corresponds to ParseHelper.OptionalWhitespaceOrLineContinuation(escapeChar) -/
def optionalWhitespaceOrLineContinuation (escapeChar : Char) : Parser (List Token) := do
  let leading ← optional whitespace
  let lc ← optional (lineContinuations escapeChar)
  let trailing ← optional whitespace
  Parser.pure (concatTokens [
    leading.getD [],
    match lc with | some lcs => if lcs.isEmpty then [] else lcs | none => [],
    trailing.getD []
  ])

-- ============================================================
-- Comment parsers
-- ============================================================

/-- Parse a comment token: '#' followed by text until end of line.
    Corresponds to CommentToken.GetParser() -/
def commentTokenParser : Parser Token := do
  let hash ← char '#'
  let text ← manyChars (satisfy (fun c => !isLineTerminator c) "non-newline char")
  Parser.pure (Token.mkComment [Token.mkSymbol hash, Token.mkString text])

/-- Parse comment text with leading whitespace.
    Corresponds to ParseHelper.CommentText() -/
def commentText : Parser (List Token) := do
  let leading ← whitespace
  let comment ← commentTokenParser
  let lineEndTok ← optionalNewLine
  let commentChildren := concatOptTokens [some comment, lineEndTok]
  Parser.pure (concatTokens [leading, [Token.mkComment commentChildren]])

-- ============================================================
-- String token helpers (for character-level parsing)
-- ============================================================

/-- Collapse a list of single-character StringTokens into minimal StringTokens.
    Adjacent StringToken values are merged.
    Corresponds to TokenHelper.CollapseStringTokens() -/
def collapseStringTokens (tokens : List Token) : List Token :=
  let rec loop (acc : List Token) (pending : String) (rest : List Token) : List Token :=
    match rest with
    | [] =>
      if pending.isEmpty then acc.reverse
      else (Token.mkString pending :: acc).reverse
    | t :: ts =>
      match t with
      | .primitive .string val =>
        loop acc (pending ++ val) ts
      | _ =>
        if pending.isEmpty then
          loop (t :: acc) "" ts
        else
          loop (t :: Token.mkString pending :: acc) "" ts
  loop [] "" tokens

/-- Parse a single character and wrap it as a StringToken.
    Corresponds to the `ToStringTokens` pattern in ParseHelper. -/
def toStringToken (p : Parser Char) : Parser Token := do
  let c ← p
  Parser.pure (Token.mkString (String.ofList [c]))

-- ============================================================
-- Keyword parser (case-insensitive string matching with line continuations)
-- ============================================================

/-- Parse a case-insensitive string, allowing line continuations between characters.
    Produces a KeywordToken.
    Corresponds to ParseHelper.StringToken(value, escapeChar) and
    KeywordToken.GetParser(keyword, escapeChar).

    The first character is parsed directly; subsequent characters allow optional
    line continuations before them. All resulting tokens are collapsed. -/
def keywordParser (keyword : String) (escapeChar : Char) : Parser Token :=
  let chars := keyword.toList
  match chars with
  | [] => Parser.fail "empty keyword"
  | first :: rest =>
    fun pos =>
      -- Parse first character
      match (charIgnoreCase first) pos with
      | .error msg p => .error msg p
      | .ok firstChar pos' =>
        -- Parse remaining characters with line continuations between them
        let rec parseRest (remaining : List Char) (accTokens : List Token)
            (curPos : Position) : ParseResult Token :=
          match remaining with
          | [] =>
            .ok (Token.mkKeyword (collapseStringTokens accTokens)) curPos
          | c :: cs =>
            match (lineContinuations escapeChar) curPos with
            | .error msg p => .error msg p
            | .ok lcs pos'' =>
              match (charIgnoreCase c) pos'' with
              | .error msg p => .error msg p
              | .ok ch pos''' =>
                parseRest cs (accTokens ++ lcs ++ [Token.mkString (String.ofList [ch])]) pos'''
        parseRest rest [Token.mkString (String.ofList [firstChar])] pos'

-- ============================================================
-- Escaped character parser
-- ============================================================

/-- Parse an escaped character: escape char followed by any non-newline character.
    Corresponds to ParseHelper.EscapedChar(escapeChar) -/
def escapedChar (escapeChar : Char) : Parser Token := do
  let esc ← char escapeChar
  let c ← satisfy (fun ch => !isLineTerminator ch) "non-newline character"
  Parser.pure (Token.mkString (String.ofList [esc, c]))

-- ============================================================
-- Variable reference parsers
-- ============================================================

/-- Parse a variable identifier: one or more alphanumeric or underscore characters.
    Corresponds to ParseHelper.VariableIdentifier() -/
def variableIdentifier : Parser String :=
  many1Chars (satisfy (fun c => c.isAlpha || c.isDigit || c == '_') "alphanumeric or underscore")

/-- Valid variable substitution modifiers, in order of precedence.
    Longer modifiers must come before shorter ones (## before #, %% before %, // before /).
    Corresponds to VariableRefToken.ValidModifiers -/
def validModifiers : List String := [":-", ":+", ":?", "-", "+", "?", "##", "#", "%%", "%", "//", "/"]

/-- Parse any of the variable substitution modifiers.
    Tries each modifier in order. -/
def modifierParser : Parser String :=
  let parsers := validModifiers.map (fun m => string m)
  match parsers with
  | [] => Parser.fail "no modifiers"
  | p :: ps => ps.foldl (fun acc next => or' acc next) p

/-- Parse the characters that start a variable reference: $ followed by letter/digit/{ .
    This is used to exclude variable reference starts from literal parsing. -/
def variableRefChars : Parser Char := do
  let _ ← char '$'
  satisfy (fun c => c.isAlpha || c.isDigit || c == '{') "letter, digit, or '{'"

/-- Check if a character could start a variable reference ($). -/
def isVariableRefStart (c : Char) : Bool :=
  c == '$'

/-- Parse a simple variable reference: $IDENTIFIER
    Returns the inner tokens (without the $ prefix, as VariableRefToken prepends it).
    Corresponds to VariableRefToken.SimpleVariableReference() -/
def simpleVariableRef : Parser Token := do
  let _ ← char '$'
  let name ← variableIdentifier
  Parser.pure (Token.mkVariableRef [Token.mkString name])

/-- Parse a literal string parser suitable for use as a modifier value.
    This is a simplified version for use inside ${VAR:-default} constructs. -/
partial def modifierValueLiteral (escapeChar : Char) : Parser Token := do
  let chars ← many1 (satisfy (fun c =>
    c != '}' && c != escapeChar && !isLineTerminator c) "modifier value character")
  Parser.pure (Token.mkString (String.ofList chars))

/-- Parse a braced variable reference: ${IDENTIFIER}, ${IDENTIFIER:-default}, etc.
    Returns a VariableRefToken.
    Corresponds to VariableRefToken.BracedVariableReference() -/
partial def bracedVariableRef (escapeChar : Char) : Parser Token := do
  let _ ← char '$'
  let openBrace ← char '{'
  let name ← variableIdentifier
  let modifierTokens ← optional (do
    let mod ← modifierParser
    let modSymbols : List Token := mod.toList.map Token.mkSymbol
    -- Parse modifier value: either a variable ref or literal text, one or more
    let valueTokens ← many1 (or' (simpleVariableRef) (or' (bracedVariableRef escapeChar) (modifierValueLiteral escapeChar)))
    let valueLiteral := Token.mkLiteral valueTokens
    Parser.pure (modSymbols ++ [valueLiteral]))
  let closeBrace ← char '}'
  let innerTokens := concatTokens [
    [Token.mkSymbol openBrace],
    [Token.mkString name],
    modifierTokens.getD [],
    [Token.mkSymbol closeBrace]
  ]
  Parser.pure (Token.mkVariableRef innerTokens)

/-- Parse a variable reference: either $VAR or ${VAR} or ${VAR:-default} etc.
    Corresponds to VariableRefToken.GetParser() -/
def variableRefParser (escapeChar : Char) : Parser Token :=
  or' (bracedVariableRef escapeChar) simpleVariableRef

-- ============================================================
-- Literal string parsers
-- ============================================================

/-- Check if a character is a literal character (non-whitespace, non-escape, non-variable-start,
    and not in the excluded set).
    Corresponds to ParseHelper.LiteralChar() -/
def isLiteralChar (escapeChar : Char) (excludedChars : List Char)
    (excludeVariableRefChars : Bool) (isWhitespaceAllowed : Bool) (c : Char) : Bool :=
  let basic := if isWhitespaceAllowed then
      !isLineTerminator c
    else
      !(c == ' ' || c == '\t' || c == '\x0C' || isLineTerminator c)
  basic &&
  !excludedChars.contains c &&
  c != escapeChar &&
  (!excludeVariableRefChars || !isVariableRefStart c)

/-- Parse a literal character.
    Corresponds to ParseHelper.LiteralChar() -/
def literalChar (escapeChar : Char) (excludedChars : List Char)
    (excludeVariableRefChars : Bool := true)
    (isWhitespaceAllowed : Bool := false) : Parser Char :=
  -- Need to handle the variable ref exclusion properly:
  -- Exclude $ only when it's followed by a letter/digit/{
  fun pos =>
    match pos.current with
    | none => .error "expected literal character" pos
    | some c =>
      if !isLiteralChar escapeChar excludedChars false isWhitespaceAllowed c then
        .error s!"excluded character '{c}'" pos
      else if excludeVariableRefChars && c == '$' then
        -- Check if $ is followed by letter/digit/{
        let pos' := pos.next
        match pos'.current with
        | some c' =>
          if c'.isAlpha || c'.isDigit || c' == '{' then
            .error "variable reference start" pos
          else
            .ok c pos.next
        | none => .ok c pos.next
      else
        .ok c pos.next

/-- Parse a literal string (no spaces, no quotes).
    Corresponds to ParseHelper.LiteralStringWithoutSpaces() -/
def literalStringWithoutSpaces (escapeChar : Char) (excludedChars : List Char)
    (excludeVariableRefChars : Bool := true) : Parser (List Token) := do
  let firstTok ← or'
    (toStringToken (literalChar escapeChar excludedChars excludeVariableRefChars))
    (escapedChar escapeChar)
  let restToks ← many (do
    let lcs ← lineContinuations escapeChar
    let ch ← literalChar escapeChar excludedChars excludeVariableRefChars
    Parser.pure (lcs ++ [Token.mkString (String.ofList [ch])]))
  Parser.pure (collapseStringTokens (firstTok :: restToks.flatten))

/-- Parse a literal string (may include escaped chars).
    Corresponds to ParseHelper.LiteralString() -/
def literalString (escapeChar : Char) (excludedChars : List Char)
    (excludeVariableRefChars : Bool := true) : Parser (List Token) :=
  or' (literalStringWithoutSpaces escapeChar excludedChars excludeVariableRefChars)
      (do let t ← escapedChar escapeChar; Parser.pure [t])

-- ============================================================
-- Value or variable reference parser
-- ============================================================

/-- Parse either a variable reference or a literal value.
    Corresponds to ParseHelper.ValueOrVariableRef() -/
def valueOrVariableRef (escapeChar : Char)
    (valueParsers : Parser (List Token)) : Parser (List Token) :=
  or' (do let v ← variableRefParser escapeChar; Parser.pure [v])
      valueParsers

-- ============================================================
-- Literal with variables (LiteralToken that may contain variable references)
-- ============================================================

/-- Mode for handling whitespace in literals. -/
inductive WhitespaceMode where
  | disallowed
  | allowedInQuotes
  | allowed
  deriving BEq

/-- Parse a literal with variable references (unquoted).
    Corresponds to the non-wrapped branch of ParseHelper.LiteralWithVariablesTokens() -/
partial def literalWithVariablesUnquoted (escapeChar : Char) (excludedChars : List Char)
    (whitespaceMode : WhitespaceMode := .disallowed) : Parser Token := do
  let tokenLists ← many1 (valueOrVariableRef escapeChar
    (if whitespaceMode == .allowed then
      or' (literalString escapeChar excludedChars)
          (or' whitespace (lineContinuations escapeChar))
    else
      literalString escapeChar excludedChars))
  let tokens := collapseStringTokens tokenLists.flatten
  if tokens.isEmpty then
    Parser.fail "expected literal with variables"
  else
    Parser.pure (Token.mkLiteral tokens)

/-- Parse a literal with variables, wrapped in quotes.
    Returns (tokens, quoteChar).
    Corresponds to the wrapped branch of ParseHelper.LiteralWithVariablesTokens() -/
def literalWithVariablesQuoted (escapeChar : Char) (excludedChars : List Char)
    (quoteChar : Char) (whitespaceMode : WhitespaceMode := .disallowed) : Parser Token := do
  let _ ← char quoteChar
  let tokenLists ← many (valueOrVariableRef escapeChar
    (do
      let chars ← many1 (fun pos =>
        match pos.current with
        | none => .error "end of input" pos
        | some c =>
          if c == quoteChar || c == escapeChar || isLineTerminator c then
            .error s!"excluded char '{c}'" pos
          else if isVariableRefStart c then
            -- Check lookahead for variable ref
            let pos' := pos.next
            match pos'.current with
            | some c' =>
              if c'.isAlpha || c'.isDigit || c' == '{' then
                .error "variable reference start" pos
              else .ok c pos.next
            | none => .ok c pos.next
          else if !(whitespaceMode == .allowedInQuotes || whitespaceMode == .allowed) &&
                  (c == ' ' || c == '\t') then
            .error "whitespace not allowed" pos
          else .ok c pos.next)
      Parser.pure [Token.mkString (String.ofList chars)]))
  let _ ← char quoteChar
  let tokens := collapseStringTokens tokenLists.flatten
  Parser.pure (Token.mkLiteral tokens (some ⟨quoteChar⟩))

/-- Parse a literal with variables, optionally wrapped in quotes.
    Returns a LiteralToken.
    Corresponds to ParseHelper.LiteralWithVariables() -/
def literalWithVariables (escapeChar : Char) (excludedChars : List Char := [])
    (whitespaceMode : WhitespaceMode := .disallowed) : Parser Token :=
  or' (literalWithVariablesQuoted escapeChar excludedChars '\'' whitespaceMode)
    (or' (literalWithVariablesQuoted escapeChar excludedChars '"' whitespaceMode)
         (literalWithVariablesUnquoted escapeChar excludedChars whitespaceMode))

-- ============================================================
-- Identifier parsers
-- ============================================================

/-- Parse an identifier string: first char (letter usually) followed by tail chars.
    Corresponds to ParseHelper.IdentifierString() -/
def identifierString (escapeChar : Char)
    (firstCharPred : Char → Bool) (tailCharPred : Char → Bool) : Parser (List Token) := do
  let firstChar ← satisfy firstCharPred "identifier first character"
  let restParts ← many (or'
    (do
      let lcs ← lineContinuations escapeChar
      let c ← satisfy tailCharPred "identifier tail character"
      Parser.pure (lcs ++ [Token.mkString (String.ofList [c])]))
    (do let t ← escapedChar escapeChar; Parser.pure [t]))
  Parser.pure (collapseStringTokens
    (Token.mkString (String.ofList [firstChar]) :: restParts.flatten))

/-- Parse an identifier token with optional quotes.
    Corresponds to ParseHelper.IdentifierTokens() -/
def identifierToken (escapeChar : Char)
    (firstCharPred : Char → Bool) (tailCharPred : Char → Bool) : Parser Token :=
  let parseQuoted (q : Char) : Parser Token := do
    let _ ← char q
    let tokens ← identifierString escapeChar
      (fun c => firstCharPred c && c != q)
      (fun c => tailCharPred c && c != q)
    let _ ← char q
    Parser.pure (Token.mkIdentifier tokens (some ⟨q⟩))
  let parseUnquoted : Parser Token := do
    let tokens ← identifierString escapeChar firstCharPred tailCharPred
    Parser.pure (Token.mkIdentifier tokens)
  or' (parseQuoted '\'') (or' (parseQuoted '"') parseUnquoted)

-- ============================================================
-- Symbol parser
-- ============================================================

/-- Parse a symbol character and return a SymbolToken.
    Corresponds to ParseHelper.Symbol(char) -/
def symbolParser (c : Char) : Parser Token := do
  let _ ← char c
  Parser.pure (Token.mkSymbol c)

-- ============================================================
-- Arg tokens (instruction argument tokenizer)
-- ============================================================

/-- Parse an instruction argument with optional leading whitespace, trailing whitespace,
    and line continuations.
    Corresponds to ParseHelper.ArgTokens() -/
def argTokens (tokenParser : Parser (List Token)) (escapeChar : Char)
    (excludeTrailingWhitespace : Bool := false)
    (excludeLeadingWhitespace : Bool := false) : Parser (List Token) :=
  if excludeTrailingWhitespace then
    if excludeLeadingWhitespace then
      tokenParser
    else do
      let leading ← whitespace
      let tokens ← tokenParser
      Parser.pure (concatTokens [leading, tokens])
  else
    let primaryParser :=
      if excludeLeadingWhitespace then
        tokenParser
      else do
        let leading ← whitespace
        let tokens ← tokenParser
        Parser.pure (concatTokens [leading, tokens])
    -- WithTrailingComments(
    --   from tokens in primaryParser
    --   from trailingWhitespace in (trailing_ws_linecont | ws_newline).Optional()
    --   select concat(tokens, trailingWhitespace))
    do
      let tokens ← primaryParser
      let trailing ← optional (
        or'
          (do
            let tw ← whitespace
            let lc ← lineContinuations escapeChar
            if lc.isEmpty then Parser.fail "expected line continuation"
            else Parser.pure (concatTokens [tw, lc]))
          (do
            let ws ← whitespaceWithoutNewLine
            let nl ← newLine
            Parser.pure (concatOptTokens [ws, some nl])))
      let commentSets ← many commentText
      Parser.pure (concatTokens [tokens, trailing.getD [], commentSets.flatten])

-- ============================================================
-- Token with trailing whitespace
-- ============================================================

/-- Parse a token followed by whitespace.
    Corresponds to ParseHelper.TokenWithTrailingWhitespace() -/
def tokenWithTrailingWhitespace (parser : Parser Token) : Parser (List Token) := do
  let tok ← parser
  let ws ← whitespace
  Parser.pure (tok :: ws)

-- ============================================================
-- Instruction parser
-- ============================================================

/-- Parse instruction name with trailing whitespace and optional line continuations.
    Corresponds to ParseHelper.InstructionNameWithTrailingContent() -/
def instructionNameWithTrailingContent (instructionName : String) (escapeChar : Char) : Parser (List Token) := do
  -- WithTrailingComments(leading + keyword + ws + optional lineContinuations)
  let leading ← whitespace
  let kwTokens ← tokenWithTrailingWhitespace (keywordParser instructionName escapeChar)
  let lc ← optional (lineContinuations escapeChar)
  let commentSets ← many commentText
  Parser.pure (concatTokens [leading, kwTokens, lc.getD [], commentSets.flatten])

/-- Parse a complete instruction: keyword + args.
    Corresponds to ParseHelper.Instruction() -/
def instructionParser (instructionName : String) (escapeChar : Char)
    (argsParser : Parser (List Token)) : Parser (List Token) := do
  let nameTokens ← instructionNameWithTrailingContent instructionName escapeChar
  let argTokens ← argsParser
  Parser.pure (concatTokens [nameTokens, argTokens])

-- ============================================================
-- Generic key-value flag parser (--name=value)
-- ============================================================

/-- Parse a generic key-value flag: `--name=value`.
    The value is parsed via `literalWithVariables` (supports variable substitution).
    Returns a KeyValueToken containing:
      SymbolToken('-'), SymbolToken('-'), KeywordToken(name), SymbolToken('='), LiteralToken(value)
    Corresponds to the C# `KeywordLiteralFlag` pattern.

    This is the shared implementation used by `platformFlagParser` and by
    `Flags.flagParser`. Defined here to avoid circular imports (since it depends
    on `keywordParser` and `literalWithVariables` which live in this file). -/
def flagParser (name : String) (escapeChar : Char) : Parser Token := do
  let dash1 ← char '-'
  let dash2 ← char '-'
  let kw ← keywordParser name escapeChar
  let eq ← char '='
  let value ← literalWithVariables escapeChar
  Parser.pure (Token.mkKeyValue [
    Token.mkSymbol dash1,
    Token.mkSymbol dash2,
    kw,
    Token.mkSymbol eq,
    value
  ])

/-- Parse a --name=value flag where the value is a plain literal (no variable reference parsing).
    Used for flags like --from where variable references are not supported by BuildKit. -/
def flagParserNoVars (name : String) (escapeChar : Char) : Parser Token := do
  let dash1 ← char '-'
  let dash2 ← char '-'
  let kw ← keywordParser name escapeChar
  let eq ← char '='
  let parts ← literalString escapeChar [] (excludeVariableRefChars := false)
  let value := Token.mkLiteral (collapseStringTokens parts)
  Parser.pure (Token.mkKeyValue [
    Token.mkSymbol dash1,
    Token.mkSymbol dash2,
    kw,
    Token.mkSymbol eq,
    value
  ])

-- ============================================================
-- Platform flag parser (--platform=value)
-- ============================================================

/-- Parse a --platform=value flag.
    Delegates to the generic `flagParser` with name "platform".
    Corresponds to PlatformFlag.GetParser() -/
def platformFlagParser (escapeChar : Char) : Parser Token :=
  flagParser "platform" escapeChar

-- ============================================================
-- Stage name parser
-- ============================================================

/-- Parse a stage name: lowercase letter followed by lowercase letters/digits/hyphens/dots/underscores.
    BuildKit requires stage names to match `^[a-z][a-z0-9-_.]*$`.
    Corresponds to StageName.GetParser() -/
def stageNameParser (escapeChar : Char) : Parser Token := do
  let tokens ← identifierString escapeChar
    (fun c => c.isLower)
    (fun c => c.isLower || c.isDigit || c == '_' || c == '-' || c == '.')
  Parser.pure (Token.mkIdentifier tokens)

-- ============================================================
-- Variable (ARG name) parser
-- ============================================================

/-- Parse a variable name (for ARG declarations).
    A variable name is an identifier: letter or underscore, followed by
    letters, digits, or underscores. -/
def variableNameParser (escapeChar : Char) : Parser Token := do
  let tokens ← identifierString escapeChar
    (fun c => c.isAlpha || c == '_')
    (fun c => c.isAlpha || c.isDigit || c == '_')
  Parser.pure (Token.mkIdentifier tokens)

-- ============================================================
-- ARG declaration parser (name[=value])
-- ============================================================

/-- Parse an ARG assignment: = followed by optional literal value with variables.
    Corresponds to ArgDeclaration.GetArgAssignmentParser() -/
def argAssignmentParser (escapeChar : Char) : Parser (List Token) := do
  let lc1 ← lineContinuations escapeChar
  let eqSym ← symbolParser '='
  let lc2 ← lineContinuations escapeChar
  let value ← optional (literalWithVariables escapeChar [] .allowedInQuotes)
  Parser.pure (concatTokens [lc1, [eqSym], lc2, match value with | some v => [v] | none => []])

/-- Parse an ARG declaration: variable_name [= value].
    Corresponds to ArgDeclaration.GetParser() -/
def argDeclarationParser (escapeChar : Char) : Parser Token := do
  let name ← argTokens (do let v ← variableNameParser escapeChar; Parser.pure [v])
    escapeChar (excludeTrailingWhitespace := true)
  let assignment ← optional (argTokens (argAssignmentParser escapeChar)
    escapeChar (excludeTrailingWhitespace := true))
  let children := concatTokens [name, assignment.getD []]
  Parser.pure (Token.mkKeyValue children)

-- ============================================================
-- Shell form command parser (rest-of-line as literal text with variables)
-- ============================================================

/-- Parse a shell form command: everything to end-of-line (or end-of-input) as
    a LiteralToken. This is the "shell form" parser for RUN, CMD, ENTRYPOINT.

    BuildKit treats shell form command text as completely opaque — it passes
    the full command text to `sh -c` without any parsing. So `$VAR` is treated
    as a regular character (not decomposed into a VariableRefToken), and
    whitespace within the command is preserved as part of a single StringToken
    (not split into separate WhitespaceToken children).

    The result is a single LiteralToken containing one StringToken with the
    full command text (plus any LineContinuationToken nodes for line
    continuations within the command).

    Corresponds to the shell-form branch of CommandInstruction.GetCommandParser() -/
partial def shellFormCommand (escapeChar : Char) : Parser (List Token) := do
  -- Parse shell form as opaque text: $ is treated as a regular character.
  -- Each iteration produces either:
  --   a) a non-escape, non-newline character → StringToken, or
  --   b) a line continuation (escape + optional whitespace + newline) → LineContinuationToken, or
  --   c) an escaped char (escape + non-newline char, not a line continuation) → StringToken.
  --
  -- Line continuation must be tried before escaped char so that
  -- `\<spaces><newline>` is recognized as a continuation rather than
  -- `escapedChar` consuming `\<space>` and terminating the instruction.
  let parts ← many1 (
    or' (do
      -- Any non-escape, non-newline character (including $, spaces, tabs)
      let c ← satisfy (fun c => !isLineTerminator c && c != escapeChar)
                       "shell form character"
      Parser.pure (Token.mkString (String.ofList [c])))
    (or'
      -- Line continuation (escape + optional whitespace + newline) — must be
      -- tried first so `\<trailing-spaces><newline>` is not consumed by escapedChar
      (lineContinuationParser escapeChar)
      -- Escaped character, guarded: only match when the escape char is NOT
      -- followed by optional whitespace + newline (which would be a continuation)
      (except (escapedChar escapeChar) (lineContinuationParser escapeChar))))
  -- Collapse adjacent string tokens into a single opaque StringToken.
  -- LineContinuationTokens are preserved as-is.
  let tokens := collapseStringTokens parts
  if tokens.isEmpty then
    Parser.fail "expected shell form command"
  else
    Parser.pure [Token.mkLiteral tokens]

end DockerfileModel.Parser
