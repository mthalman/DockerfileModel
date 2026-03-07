/-
  Parser/Instructions/From.lean -- FROM instruction parser.

  Parses the FROM instruction:
    FROM [--platform=<platform>] <image>[:<tag>|@<digest>] [AS <name>]

  This mirrors FromInstruction.cs:
  - GetInnerParser: Instruction("FROM", escapeChar, GetArgsParser(escapeChar))
  - GetArgsParser: platform? imageName stageName?
  - GetPlatformParser: ArgTokens(PlatformFlag.GetParser().AsEnumerable())
  - GetImageNameParser: ArgTokens(LiteralWithVariables().AsEnumerable())
  - GetStageNameParser: ArgTokens(AS keyword) + ArgTokens(StageName)

  The parser produces a Token tree matching the C# token structure:
    InstructionToken [
      WhitespaceToken?,       -- leading whitespace
      KeywordToken("FROM"),   -- instruction keyword
      WhitespaceToken(" "),   -- separator
      PlatformFlag?,          -- optional --platform=value (KeyValueToken)
      WhitespaceToken?,
      LiteralToken(imageName), -- image name (may contain variable refs)
      WhitespaceToken?,
      KeywordToken("AS")?,    -- optional AS keyword
      WhitespaceToken?,
      IdentifierToken(name)?, -- optional stage name
    ]
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
-- FROM args sub-parsers
-- ============================================================

/-- Parse the optional --platform=value flag for FROM.
    Corresponds to FromInstruction.GetPlatformParser() -/
def fromPlatformParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    let flag ← platformFlagParser escapeChar
    Parser.pure [flag]) escapeChar

/-- Parse the image name in a FROM instruction.
    Corresponds to FromInstruction.GetImageNameParser() -/
def fromImageNameParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    let lit ← literalWithVariables escapeChar
    Parser.pure [lit]) escapeChar

/-- Parse the optional "AS <stagename>" clause.
    Corresponds to FromInstruction.GetStageNameParser() -/
def fromStageNameParser (escapeChar : Char) : Parser (List Token) := do
  let asKeyword ← argTokens (do
    let kw ← keywordParser "AS" escapeChar
    Parser.pure [kw]) escapeChar
  let name ← argTokens (do
    let sn ← stageNameParser escapeChar
    Parser.pure [sn]) escapeChar
  Parser.pure (concatTokens [asKeyword, name])

-- ============================================================
-- FROM args parser
-- ============================================================

/-- Parse the arguments of a FROM instruction: [platform] imageName [AS stageName].
    Corresponds to FromInstruction.GetArgsParser() -/
def fromArgsParser (escapeChar : Char) : Parser (List Token) := do
  let platform ← Parser.optional (fromPlatformParser escapeChar)
  let imageName ← fromImageNameParser escapeChar
  let stageName ← Parser.optional (fromStageNameParser escapeChar)
  -- Require end of input (corresponds to .End() in C#)
  Parser.pure (concatTokens [
    platform.getD [],
    imageName,
    stageName.getD []
  ])

-- ============================================================
-- FROM instruction parser
-- ============================================================

/-- Parse a complete FROM instruction.
    Corresponds to FromInstruction.GetInnerParser() -/
def fromInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "FROM" escapeChar (fromArgsParser escapeChar)

/-- Parse a FROM instruction and produce an Instruction value. -/
def parseFromInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← fromInstructionParser escapeChar
  Parser.pure {
    name := .from,
    token := Token.mkInstruction tokens
  }

/-- Parse FROM instruction from text, returning an Instruction. -/
def parseFrom (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseFromInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions
