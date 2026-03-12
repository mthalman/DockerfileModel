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

  Token structure produced (matching C#):

  Marker (unquoted):
    Token.aggregate .construct [
      Token.mkSymbol '<',
      Token.mkSymbol '<',
      Token.mkIdentifier [Token.mkString "EOF"]
    ]

  Marker (chomp):
    Token.aggregate .construct [
      Token.mkSymbol '<',
      Token.mkSymbol '<',
      Token.mkSymbol '-',
      Token.mkIdentifier [Token.mkString "EOF"]
    ]

  Marker (quoted):
    Token.aggregate .construct [
      Token.mkSymbol '<',
      Token.mkSymbol '<',
      Token.mkSymbol '"',
      Token.mkIdentifier [Token.mkString "EOF"],
      Token.mkSymbol '"'
    ]

  Body:
    Token.aggregate .construct [
      Token.mkString "body content...",
      Token.mkIdentifier [Token.mkString "EOF"],
      Token.mkNewLine "\n"
    ]

  Body (chomp, with tab prefix on delimiter line):
    Token.aggregate .construct [
      Token.mkString "body content...",
      Token.mkString "\t",
      Token.mkIdentifier [Token.mkString "EOF"],
      Token.mkNewLine "\n"
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
-- Produces structured tokens matching C# HeredocMarkerToken
-- ============================================================

/-- Parse a heredoc redirect marker: <<[-][']DELIM[']
    Returns: (delimiter : String, chomp : Bool, quoteChar : Option Char, markerToken : Token)

    The marker token is a construct aggregate containing:
      symbol(<), symbol(<), [symbol(-)], [symbol(quote)], identifier(delim), [symbol(quote)]

    The marker appears on the instruction line itself. Examples:
      <<EOF        — unquoted, no chomp
      <<-EOF       — unquoted, with chomp (strip leading tabs)
      <<"EOF"      — double-quoted, no variable expansion in body
      <<'EOF'      — single-quoted, no variable expansion in body
      <<-"EOF"     — double-quoted with chomp -/
def heredocMarkerParser : Parser (String × Bool × Option Char × Token) := do
  -- Parse the << operator
  let lt1 ← char '<'
  let lt2 ← char '<'
  -- Parse optional chomp flag (-)
  let chompOpt ← optional (char '-')
  let chomp := chompOpt.isSome
  -- Skip optional whitespace between << and delimiter
  let _ ← many (satisfy (fun c => c == ' ' || c == '\t') "whitespace")
  -- Parse the delimiter: quoted or unquoted
  let (delim, quoteChar) ← or'
    -- Double-quoted delimiter
    (do
      let q ← char '"'
      let d ← many1Chars (satisfy (fun c => c != '"' && !isLineTerminator c) "delimiter char")
      let _ ← char '"'
      Parser.pure (d, some q))
    (or'
      -- Single-quoted delimiter
      (do
        let q ← char '\''
        let d ← many1Chars (satisfy (fun c => c != '\'' && !isLineTerminator c) "delimiter char")
        let _ ← char '\''
        Parser.pure (d, some q))
      -- Unquoted delimiter
      (do
        let d ← many1Chars (satisfy (fun c =>
          c != ' ' && c != '\t' && !isLineTerminator c && c != '<') "delimiter char")
        Parser.pure (d, none)))
  -- Build the marker token as a construct
  let markerChildren : List Token :=
    [Token.mkSymbol lt1, Token.mkSymbol lt2] ++
    (if chomp then match chompOpt with
      | some c => [Token.mkSymbol c]
      | none => []
    else []) ++
    (match quoteChar with
      | some q => [Token.mkSymbol q, Token.mkIdentifier [Token.mkString delim], Token.mkSymbol q]
      | none => [Token.mkIdentifier [Token.mkString delim]])
  let markerToken := Token.mkConstruct markerChildren
  Parser.pure (delim, chomp, quoteChar, markerToken)

-- ============================================================
-- Heredoc body parser: consume lines until closing delimiter
-- ============================================================

/-- Check if a line consists only of the delimiter (with optional leading tabs
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
    chomp: if true, the closing delimiter may have leading tabs (for matching)
           but body content is preserved as-is (no tab stripping)

    Returns: Token with kind .construct containing:
      - A single StringToken with all body content concatenated
      - An optional StringToken for tab prefix on delimiter line (chomp mode)
      - An IdentifierToken wrapping the closing delimiter name
      - An optional NewLineToken for the trailing newline

    This matches C#'s HeredocBodyToken structure. -/
partial def heredocBodyParser (delimiter : String) (chomp : Bool) : Parser Token :=
  fun pos =>
    let rec consumeLines (bodyAcc : String) (curPos : Position) : ParseResult Token :=
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
        -- This is the closing delimiter line. Build the body construct token.
        let bodyChildren : List Token :=
          -- Body content as a single string (only if non-empty)
          (if bodyAcc.isEmpty then [] else [Token.mkString bodyAcc]) ++
          -- For chomp mode with tab prefix on delimiter line, emit prefix as StringToken
          (if chomp && lineContent.length > delimiter.length then
            let tabPrefix := String.ofList (lineContent.toList.take (lineContent.length - delimiter.length))
            [Token.mkString tabPrefix]
          else []) ++
          -- Closing delimiter as IdentifierToken
          [Token.mkIdentifier [Token.mkString delimiter]] ++
          -- Trailing newline
          (if hasNewline then
            let nlStr := if lineText.endsWith "\r\n" then "\r\n"
                         else if lineText.endsWith "\n" then "\n"
                         else ""
            if nlStr.isEmpty then [] else [Token.mkNewLine nlStr]
          else [])
        .ok (Token.mkConstruct bodyChildren) nextPos
      else if !hasNewline && lineText.isEmpty then
        -- End of input without finding closing delimiter
        .error s!"heredoc: closing delimiter '{delimiter}' not found" lineStart
      else if !hasNewline then
        -- End of input mid-line without finding closing delimiter
        .error s!"heredoc: closing delimiter '{delimiter}' not found" lineStart
      else
        -- Regular body line: accumulate raw content (no tab stripping)
        consumeLines (bodyAcc ++ lineText) nextPos
    consumeLines "" pos

-- ============================================================
-- Heredoc-aware instruction argument parser
-- ============================================================

/-- Parse a heredoc opening on an instruction line: <<[-]DELIMITER followed by
    the rest of the line and newline, then the heredoc body.

    This parser handles the full sequence:
    1. Parse the heredoc marker (<<DELIM) → construct token
    2. Consume rest of instruction line (optional trailing content)
    3. Consume newline
    4. Parse heredoc body until closing delimiter → construct token

    Returns a flat list of tokens for embedding in instruction token lists. -/
partial def heredocInstructionArg : Parser (List Token) := do
  let (delimiter, chomp, _quoteChar, markerToken) ← heredocMarkerParser
  -- Consume rest of instruction line (e.g., nothing for RUN)
  let restOfLine ← many (satisfy (fun c => !isLineTerminator c) "rest of line char")
  let restTokens := if restOfLine.isEmpty then []
    else [Token.mkString (String.ofList restOfLine)]
  -- Consume the newline
  let nl ← lineEnd
  let nlToken := Token.mkNewLine nl
  -- Parse heredoc body
  let bodyToken ← heredocBodyParser delimiter chomp
  Parser.pure ([markerToken] ++ restTokens ++ [nlToken, bodyToken])

/-- Parse a heredoc opening for file-transfer instructions (COPY, ADD).
    These expect: <<DELIMITER [whitespace] destination_path
    followed by the heredoc body.

    Returns a flat list of tokens. -/
partial def heredocWithDestination : Parser (List Token) := do
  let (delimiter, chomp, _quoteChar, markerToken) ← heredocMarkerParser
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
  Parser.pure ([markerToken] ++ wsTokens ++ destTokens ++ [nlToken, bodyToken])

end DockerfileModel.Parser.Heredoc
