/-
  Parser/Instructions/Volume.lean -- VOLUME instruction parser.

  Parses the VOLUME instruction:
    VOLUME (ExecForm | Path [Path ...])

  ExecForm: VOLUME ["/data", "/var/log"]
  ShellForm: VOLUME /data /var/log

  The parser tries exec form (JSON array) first, then falls back to
  space-separated literal paths.

  Token structure produced (exec form):
    InstructionToken [
      WhitespaceToken?,          -- leading whitespace
      KeywordToken("VOLUME"),    -- instruction keyword
      WhitespaceToken(" "),      -- separator
      SymbolToken('['),          -- JSON array tokens
      LiteralToken("..."),
      ...
      SymbolToken(']')
    ]

  Token structure produced (shell form):
    InstructionToken [
      WhitespaceToken?,          -- leading whitespace
      KeywordToken("VOLUME"),    -- instruction keyword
      WhitespaceToken(" "),      -- separator
      LiteralToken(path1),       -- first path
      WhitespaceToken?,          -- separator
      LiteralToken(path2),       -- additional path
      ...
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers
import DockerfileModel.Parser.ExecForm

namespace DockerfileModel.Parser.Instructions.Volume

open DockerfileModel
open DockerfileModel.Parser
open Parser
open DockerfileModel.Parser.ExecForm

-- ============================================================
-- Volume paths parser (shell form)
-- ============================================================

/-- Parse one or more space-separated paths for the shell form of VOLUME. -/
def volumePathsParser (escapeChar : Char) : Parser (List Token) := do
  let firstPath ← argTokens (do
    let lit ← literalWithVariables escapeChar
    Parser.pure [lit]) escapeChar
  let restPaths ← many (argTokens (do
    let lit ← literalWithVariables escapeChar
    Parser.pure [lit]) escapeChar)
  Parser.pure (concatTokens (firstPath :: restPaths))

-- ============================================================
-- VOLUME args parser
-- ============================================================

/-- Parse the arguments of a VOLUME instruction: exec form or space-separated paths.
    Corresponds to VolumeInstruction.GetArgsParser() -/
partial def volumeArgsParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (or' (jsonArrayParser escapeChar) (volumePathsParser escapeChar)) escapeChar
    (excludeLeadingWhitespace := true)

-- ============================================================
-- VOLUME instruction parser
-- ============================================================

/-- Parse a complete VOLUME instruction.
    Corresponds to VolumeInstruction.GetInnerParser() -/
def volumeInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "VOLUME" escapeChar (volumeArgsParser escapeChar)

/-- Parse a VOLUME instruction and produce an Instruction value. -/
def parseVolumeInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← volumeInstructionParser escapeChar
  Parser.pure {
    name := .volume,
    token := Token.mkInstruction tokens
  }

/-- Parse VOLUME instruction from text, returning an Instruction. -/
def parseVolume (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseVolumeInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Volume
