/-
  Parser/Heredoc.lean -- Heredoc parser for inline scripts/files in Dockerfiles.

  Heredocs allow multi-line content to be embedded directly in Dockerfile
  instructions like RUN, COPY, and ADD. BuildKit supports this syntax.

  Grammar:
    HeredocRedirect    ::= FileDescriptor? '<<' ChompFlag? HeredocDelimiter
    FileDescriptor     ::= [0-9]+
    ChompFlag          ::= '-'              (strip leading tabs from content)
    HeredocDelimiter   ::= UnquotedWord     (variable expansion enabled in body)
                         | '"' Word '"'     (variable expansion disabled)
                         | "'" Word "'"     (variable expansion disabled)

    HeredocBody        ::= HeredocLine* HeredocEnd
    HeredocLine        ::= <any text> Newline
    HeredocEnd         ::= HeredocDelimiter Newline

  Implementation approach — TWO-PASS:
  1. The heredoc OPENING (<<EOF, <<-EOF, <<"EOF", <<'EOF') is detected during
     instruction line parsing.
  2. The heredoc BODY (everything until the closing delimiter on its own line)
     is consumed as a separate pass.

  Token structure produced:
    Token.aggregate .heredoc [
      Token.mkString "<<",          -- redirect operator
      Token.mkString "-",           -- optional chomp flag
      Token.mkString "EOF",         -- delimiter (or quoted variant)
      Token.mkNewLine "\n",         -- separator
      Token.mkString "line1\n",     -- body content lines
      Token.mkString "line2\n",
      Token.mkString "EOF",         -- closing delimiter
      Token.mkNewLine "\n"          -- trailing newline
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators

namespace DockerfileModel.Parser.Heredoc

open DockerfileModel
open DockerfileModel.Parser
open Parser

-- ============================================================
-- Heredoc marker parser: detects <<[-][']DELIM[']
-- ============================================================

/-- Parse a heredoc redirect marker: <<[-][']DELIM[']
    Returns: (delimiter : String, chomp : Bool, quoted : Bool)

    The marker appears on the instruction line itself. Examples:
      <<EOF        — unquoted, no chomp
      <<-EOF       — unquoted, with chomp (strip leading tabs)
      <<"EOF"      — double-quoted, no variable expansion in body
      <<'EOF'      — single-quoted, no variable expansion in body
      <<-"EOF"     — double-quoted with chomp -/
def heredocMarkerParser : Parser (String × Bool × Bool) := do
  -- Parse the << operator
  let _ ← char '<'
  let _ ← char '<'
  -- Parse optional chomp flag (-)
  let chompOpt ← optional (char '-')
  let chomp := chompOpt.isSome
  -- Skip optional whitespace between << and delimiter
  let _ ← many (satisfy (fun c => c == ' ' || c == '\t') "whitespace")
  -- Parse the delimiter: quoted or unquoted
  let result ← or'
    -- Double-quoted delimiter
    (do
      let _ ← char '"'
      let delim ← many1Chars (satisfy (fun c => c != '"' && !isLineTerminator c) "delimiter char")
      let _ ← char '"'
      Parser.pure (delim, chomp, true))
    (or'
      -- Single-quoted delimiter
      (do
        let _ ← char '\''
        let delim ← many1Chars (satisfy (fun c => c != '\'' && !isLineTerminator c) "delimiter char")
        let _ ← char '\''
        Parser.pure (delim, chomp, true))
      -- Unquoted delimiter
      (do
        let delim ← many1Chars (satisfy (fun c =>
          c != ' ' && c != '\t' && !isLineTerminator c && c != '<') "delimiter char")
        Parser.pure (delim, chomp, false)))
  Parser.pure result

-- ============================================================
-- Heredoc body parser: consume lines until closing delimiter
-- ============================================================

/-- Check if a line consists only of the delimiter (with optional leading whitespace
    when chomp is enabled). Returns true if this is the closing delimiter line. -/
private def isClosingLine (line : String) (delimiter : String) (chomp : Bool) : Bool :=
  if chomp then
    -- Strip leading tabs and check
    let stripped := String.ofList (line.toList.dropWhile (· == '\t'))
    stripped == delimiter
  else
    line == delimiter

/-- Parse heredoc body: consume complete lines until the closing delimiter appears
    alone on its own line.

    delimiter: the closing delimiter string
    chomp: if true, strip leading tabs from each body line

    Returns: Token with kind .heredoc containing the body text as primitive string tokens.

    The body includes everything from the first line after the marker up to (and
    including) the closing delimiter line. Lines are stored as string tokens. -/
partial def heredocBodyParser (delimiter : String) (chomp : Bool) : Parser Token :=
  fun pos =>
    let rec consumeLines (acc : List Token) (curPos : Position) : ParseResult Token :=
      -- Try to read a complete line
      let lineStart := curPos
      let rec readLine (chars : List Char) (p : Position) : (String × Position × Bool) :=
        match p.current with
        | none =>
          -- End of input: whatever we have is the last line (no newline)
          (String.ofList chars.reverse, p, false)
        | some '\n' =>
          (String.ofList (chars.reverse ++ ['\n']), p.next, true)
        | some '\r' =>
          let p' := p.next
          match p'.current with
          | some '\n' =>
            (String.ofList (chars.reverse ++ ['\r', '\n']), p'.next, true)
          | _ =>
            (String.ofList (chars.reverse ++ ['\r']), p', true)
        | some c =>
          readLine (c :: chars) p.next
      let (lineText, nextPos, hasNewline) := readLine [] curPos
      -- Check if this line (without trailing newline) is the closing delimiter
      let lineContent := if hasNewline then
          -- Strip trailing newline for comparison
          let chars := lineText.toList
          if lineText.endsWith "\r\n" then
            String.ofList (chars.dropLast.dropLast)
          else
            String.ofList chars.dropLast
        else lineText
      if isClosingLine lineContent delimiter chomp then
        -- This is the closing delimiter line. Build the heredoc token.
        let closingTokens := [Token.mkString lineContent]
        let nlToken := if hasNewline then
            let nlStr := if lineText.endsWith "\r\n" then "\r\n"
                         else if lineText.endsWith "\n" then "\n"
                         else ""
            if nlStr.isEmpty then [] else [Token.mkNewLine nlStr]
          else []
        let allTokens := acc.reverse ++ closingTokens ++ nlToken
        .ok (Token.mkHeredoc allTokens) nextPos
      else if !hasNewline && lineText.isEmpty then
        -- End of input without finding closing delimiter
        .error s!"heredoc: closing delimiter '{delimiter}' not found" lineStart
      else if !hasNewline then
        -- End of input mid-line without finding closing delimiter
        .error s!"heredoc: closing delimiter '{delimiter}' not found" lineStart
      else
        -- Regular body line: apply chomp if needed and accumulate
        let bodyLine := if chomp then
            -- Strip leading tabs from the content
            String.ofList (lineText.toList.dropWhile (· == '\t'))
          else lineText
        consumeLines (Token.mkString bodyLine :: acc) nextPos
    consumeLines [] pos

-- ============================================================
-- Combined heredoc parser: marker + body
-- ============================================================

/-- Parse a complete heredoc: the opening marker on the current line, then consume
    the body lines after a newline.

    Returns a list of tokens representing the heredoc construct:
    - String tokens for the marker components (<<, chomp flag, delimiter)
    - The heredoc body as a .heredoc aggregate token

    This is intended to be called from instruction parsers (RUN, COPY, ADD) when
    they detect a `<<` sequence. -/
partial def heredocParser : Parser (List Token) := do
  let (delimiter, chomp, quoted) ← heredocMarkerParser
  -- Build tokens for the marker itself
  let markerTokens : List Token := [
    Token.mkString "<<"
  ] ++ (if chomp then [Token.mkString "-"] else [])
    ++ (if quoted then
          -- Represent the quoted delimiter
          [Token.mkString s!"'{delimiter}'"]  -- simplified: store quote representation
        else
          [Token.mkString delimiter])
  -- The body starts after the next newline, but the instruction parser
  -- handles consuming the rest of the instruction line. The body parser
  -- should be called after that newline.
  let bodyToken ← heredocBodyParser delimiter chomp
  Parser.pure (markerTokens ++ [bodyToken])

-- ============================================================
-- Heredoc-aware instruction argument parser
-- ============================================================

/-- Parse a heredoc opening on an instruction line: <<[-]DELIMITER followed by
    the rest of the line and newline, then the heredoc body.

    This parser handles the full sequence:
    1. Parse the heredoc marker (<<DELIM)
    2. Consume rest of instruction line (optional trailing content)
    3. Consume newline
    4. Parse heredoc body until closing delimiter

    Returns a flat list of tokens for embedding in instruction token lists. -/
partial def heredocInstructionArg : Parser (List Token) := do
  let (delimiter, chomp, _quoted) ← heredocMarkerParser
  -- Build marker tokens
  let markerStr := "<<" ++ (if chomp then "-" else "") ++ delimiter
  let markerTokens := [Token.mkString markerStr]
  -- Consume rest of instruction line (e.g., destination path for COPY)
  let restOfLine ← many (satisfy (fun c => !isLineTerminator c) "rest of line char")
  let restTokens := if restOfLine.isEmpty then []
    else [Token.mkWhitespace " ", Token.mkString (String.ofList restOfLine)]
  -- Consume the newline
  let nl ← lineEnd
  let nlToken := Token.mkNewLine nl
  -- Parse heredoc body
  let bodyToken ← heredocBodyParser delimiter chomp
  Parser.pure (markerTokens ++ restTokens ++ [nlToken, bodyToken])

/-- Parse a heredoc opening for file-transfer instructions (COPY, ADD).
    These expect: <<DELIMITER [whitespace] destination_path
    followed by the heredoc body.

    Returns: (heredoc tokens, destination tokens) -/
partial def heredocWithDestination : Parser (List Token) := do
  let (delimiter, chomp, _quoted) ← heredocMarkerParser
  -- Build marker tokens
  let markerStr := "<<" ++ (if chomp then "-" else "") ++ delimiter
  let markerTokens := [Token.mkString markerStr]
  -- Parse whitespace before destination
  let wsChars ← many (satisfy (fun c => c == ' ' || c == '\t') "whitespace")
  let wsTokens := if wsChars.isEmpty then []
    else [Token.mkWhitespace (String.ofList wsChars)]
  -- Parse destination path (rest of line)
  let destChars ← many (satisfy (fun c => !isLineTerminator c) "destination char")
  let destTokens := if destChars.isEmpty then []
    else [Token.mkLiteral [Token.mkString (String.ofList destChars)]]
  -- Consume newline
  let nl ← lineEnd
  let nlToken := Token.mkNewLine nl
  -- Parse heredoc body
  let bodyToken ← heredocBodyParser delimiter chomp
  Parser.pure (markerTokens ++ wsTokens ++ destTokens ++ [nlToken, bodyToken])

end DockerfileModel.Parser.Heredoc
