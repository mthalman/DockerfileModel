/-
  Parser/Instructions/Add.lean -- ADD instruction parser.

  Parses the ADD instruction:
    ADD [AddFlags] FileArgs

  AddFlags (optional, any order):
    --chown=UserGroupSpec   — string flag via flagParser "chown"
    --chmod=PermissionSpec  — string flag via flagParser "chmod"
    --link                  — boolean flag via booleanFlagParser "link"
    --keep-git-dir          — boolean flag via booleanFlagParser "keep-git-dir"
    --checksum=ChecksumVal  — string flag via flagParser "checksum"
    --unpack                — boolean flag via booleanFlagParser "unpack"
    --exclude=GlobPattern   — string flag via flagParser "exclude" (repeatable)

  FileArgs: try exec form (JSON array) first, fall back to space-separated
  source(s) + destination as literal tokens. Same pattern as COPY.

  Token structure produced:
    InstructionToken [
      WhitespaceToken?,          -- leading whitespace
      KeywordToken("ADD"),       -- instruction keyword
      WhitespaceToken(" "),      -- separator
      KeyValueToken(--chown=..), -- optional flags
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

namespace DockerfileModel.Parser.Instructions.Add

open DockerfileModel
open DockerfileModel.Parser
open Parser
open DockerfileModel.Parser.ExecForm
open DockerfileModel.Parser.Flags
open DockerfileModel.Parser.Heredoc

-- ============================================================
-- ADD flag parsers
-- ============================================================

/-- Parse a single ADD flag: --chown, --chmod, --link, --keep-git-dir,
    --checksum, --unpack, or --exclude.
    Returns the flag token wrapped in argTokens. -/
private def addFlagParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    let flag ← or' (flagParser "chown" escapeChar)
               (or' (flagParser "chmod" escapeChar)
               (or' (booleanFlagParser "link" escapeChar)
               (or' (booleanFlagParser "keep-git-dir" escapeChar)
               (or' (flagParser "checksum" escapeChar)
               (or' (booleanFlagParser "unpack" escapeChar)
                    (flagParser "exclude" escapeChar))))))
    Parser.pure [flag]) escapeChar

-- ============================================================
-- File transfer args (same pattern as COPY)
-- ============================================================

/-- Parse space-separated file arguments (source(s) + destination).
    Each literal token is separated by whitespace. -/
private partial def spaceSeparatedFileArgs (escapeChar : Char) : Parser (List Token) := do
  let first ← literalWithVariables escapeChar
  let rest ← many (do
    let ws ← whitespace
    if ws.isEmpty then Parser.fail "expected whitespace between file args"
    let lc ← lineContinuations escapeChar
    let wsAfterLc ← if lc.isEmpty then Parser.pure ([] : List Token)
                     else do
                       let w ← whitespace
                       Parser.pure w
    let arg ← literalWithVariables escapeChar
    Parser.pure (concatTokens [ws, lc, wsAfterLc, [arg]]))
  Parser.pure (concatTokens [[first], rest.flatten])

-- ============================================================
-- ADD args parser
-- ============================================================

/-- Parse the arguments of an ADD instruction: [flags] (heredoc | exec form | space-separated).
    For heredoc, syntax is: ADD <<EOF /path\ncontent\nEOF
    Corresponds to AddInstruction.GetArgsParser() -/
partial def addArgsParser (escapeChar : Char) : Parser (List Token) := do
  let flags ← many (addFlagParser escapeChar)
  let fileArgs ← argTokens
    (or' heredocWithDestination
      (or' (jsonArrayParser escapeChar) (spaceSeparatedFileArgs escapeChar)))
    escapeChar
  Parser.pure (concatTokens [flags.flatten, fileArgs])

-- ============================================================
-- ADD instruction parser
-- ============================================================

/-- Parse a complete ADD instruction.
    Corresponds to AddInstruction.GetInnerParser() -/
def addInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "ADD" escapeChar (addArgsParser escapeChar)

/-- Parse an ADD instruction and produce an Instruction value. -/
def parseAddInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← addInstructionParser escapeChar
  Parser.pure {
    name := .add,
    token := Token.mkInstruction tokens
  }

/-- Parse ADD instruction from text, returning an Instruction. -/
def parseAdd (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseAddInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Add
