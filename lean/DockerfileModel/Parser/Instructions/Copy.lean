/-
  Parser/Instructions/Copy.lean -- COPY instruction parser.

  Parses the COPY instruction:
    COPY [CopyFlags] FileArgs

  CopyFlags (optional, any order):
    --from=StageOrImageOrContext — string flag via flagParserNoVars "from" (no variable expansion)
    --chown=UserGroupSpec       — string flag via flagParser "chown"
    --chmod=PermissionSpec      — string flag via flagParser "chmod"
    --link                      — boolean flag via booleanFlagParser "link"
    --parents                   — boolean flag via booleanFlagParser "parents"
    --exclude=GlobPattern       — string flag via flagParser "exclude" (repeatable)

  FileArgs: try exec form (JSON array) first, fall back to space-separated
  source(s) + destination as literal tokens.

  Token structure produced:
    InstructionToken [
      WhitespaceToken?,           -- leading whitespace
      KeywordToken("COPY"),       -- instruction keyword
      WhitespaceToken(" "),       -- separator
      KeyValueToken(--from=...),  -- optional flags
      ...
      LiteralToken(src) | SymbolToken('[') -- file args
      ...
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers
import DockerfileModel.Parser.ExecForm
import DockerfileModel.Parser.Flags
import DockerfileModel.Parser.Heredoc

namespace DockerfileModel.Parser.Instructions.Copy

open DockerfileModel
open DockerfileModel.Parser
open Parser
open DockerfileModel.Parser.ExecForm
open DockerfileModel.Parser.Flags
open DockerfileModel.Parser.Heredoc

-- ============================================================
-- COPY flag parsers
-- ============================================================

/-- Parse a single COPY flag: --from, --chown, --chmod, --link, --parents, or --exclude.
    Returns the flag token wrapped in argTokens. -/
private def copyFlagParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    let flag ← or' (flagParserNoVars "from" escapeChar)
               (or' (flagParser "chown" escapeChar)
               (or' (flagParser "chmod" escapeChar)
               (or' (booleanFlagParser "link" escapeChar)
               (or' (booleanFlagParser "parents" escapeChar)
                    (flagParser "exclude" escapeChar)))))
    Parser.pure [flag]) escapeChar

-- ============================================================
-- File transfer args (shared pattern for COPY and ADD)
-- ============================================================

/-- Parse space-separated file arguments (source(s) + destination).
    Each literal token is separated by whitespace. The last token is the
    destination; all preceding are sources.
    Falls back from exec form to this space-separated form.
    Uses WhitespaceMode.AllowedInQuotes so that quoted paths like "my file.txt"
    are parsed as a single literal token with quoteChar set, matching C# behavior. -/
private partial def spaceSeparatedFileArgs (escapeChar : Char) : Parser (List Token) := do
  -- Parse at least one literal argument
  let first ← literalWithVariables escapeChar (whitespaceMode := .allowedInQuotes)
  -- Parse additional space-separated arguments
  let rest ← many (do
    let ws ← whitespace
    if ws.isEmpty then Parser.fail "expected whitespace between file args"
    let lc ← lineContinuations escapeChar
    let wsAfterLc ← if lc.isEmpty then Parser.pure ([] : List Token)
                     else do
                       let w ← whitespace
                       Parser.pure w
    let arg ← literalWithVariables escapeChar (whitespaceMode := .allowedInQuotes)
    Parser.pure (concatTokens [ws, lc, wsAfterLc, [arg]]))
  Parser.pure (concatTokens [[first], rest.flatten])

-- ============================================================
-- COPY args parser
-- ============================================================

/-- Parse the arguments of a COPY instruction: [flags] (heredoc | exec form | space-separated).
    For heredoc, syntax is: COPY <<EOF /path\ncontent\nEOF
    Corresponds to CopyInstruction.GetArgsParser() -/
partial def copyArgsParser (escapeChar : Char) : Parser (List Token) := do
  -- Parse 0+ flags in any order
  let flags ← many (copyFlagParser escapeChar)
  -- Then parse file args: heredoc with destination, exec form, or space-separated
  let fileArgs ← argTokens
    (or' heredocWithDestination
      (or' (jsonArrayParser escapeChar) (spaceSeparatedFileArgs escapeChar)))
    escapeChar
  Parser.pure (concatTokens [flags.flatten, fileArgs])

-- ============================================================
-- COPY instruction parser
-- ============================================================

/-- Parse a complete COPY instruction.
    Corresponds to CopyInstruction.GetInnerParser() -/
def copyInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "COPY" escapeChar (copyArgsParser escapeChar)

/-- Parse a COPY instruction and produce an Instruction value. -/
def parseCopyInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← copyInstructionParser escapeChar
  Parser.pure {
    name := .copy,
    token := Token.mkInstruction tokens
  }

/-- Parse COPY instruction from text, returning an Instruction. -/
def parseCopy (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseCopyInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Copy
