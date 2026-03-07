/-
  Parser/Instructions/Onbuild.lean -- ONBUILD instruction parser.

  Parses the ONBUILD instruction:
    ONBUILD TriggerInstruction

  After parsing the ONBUILD keyword, the rest of the line is captured as
  raw text (a LiteralToken). The trigger instruction is NOT recursively
  parsed — it's treated as opaque text.

  Validation: The trigger must not start with a restricted keyword
  (case-insensitive): ONBUILD (no chaining), FROM, MAINTAINER.

  Token structure produced:
    InstructionToken [
      WhitespaceToken?,           -- leading whitespace
      KeywordToken("ONBUILD"),    -- instruction keyword
      WhitespaceToken(" "),       -- separator
      LiteralToken(trigger)       -- trigger instruction text
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers

namespace DockerfileModel.Parser.Instructions.Onbuild

open DockerfileModel
open DockerfileModel.Parser
open Parser

-- ============================================================
-- Trigger instruction validation
-- ============================================================

/-- Check if a trigger instruction text starts with a restricted keyword.
    Restricted triggers: ONBUILD (no chaining), FROM, MAINTAINER. -/
def isRestrictedTrigger (text : String) : Bool :=
  let upper := text.trimAscii.toString.toUpper
  upper.startsWith "ONBUILD" || upper.startsWith "FROM" || upper.startsWith "MAINTAINER"

-- ============================================================
-- ONBUILD args parser
-- ============================================================

/-- Parse the arguments of an ONBUILD instruction: rest of line as literal text.
    Validates that the trigger is not a restricted keyword.
    Corresponds to OnBuildInstruction.GetArgsParser() -/
partial def onbuildArgsParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    -- Parse rest of line as literal text (shell form captures everything)
    let triggerTokens ← shellFormCommand escapeChar
    -- Extract the trigger text for validation
    let triggerText := String.join (triggerTokens.map Token.toString)
    -- Validate: reject restricted triggers
    if isRestrictedTrigger triggerText then
      Parser.fail s!"ONBUILD does not support {triggerText.trimAscii.toString.toUpper.takeWhile (!·.isWhitespace)} as a trigger instruction"
    else
      Parser.pure triggerTokens) escapeChar

-- ============================================================
-- ONBUILD instruction parser
-- ============================================================

/-- Parse a complete ONBUILD instruction.
    Corresponds to OnBuildInstruction.GetInnerParser() -/
def onbuildInstructionParser (escapeChar : Char) : Parser (List Token) :=
  instructionParser "ONBUILD" escapeChar (onbuildArgsParser escapeChar)

/-- Parse an ONBUILD instruction and produce an Instruction value. -/
def parseOnbuildInstruction (escapeChar : Char := '\\') : Parser Instruction := do
  let tokens ← onbuildInstructionParser escapeChar
  Parser.pure {
    name := .onBuild,
    token := Token.mkInstruction tokens
  }

/-- Parse ONBUILD instruction from text, returning an Instruction. -/
def parseOnbuild (text : String) (escapeChar : Char := '\\') : Option Instruction :=
  (parseOnbuildInstruction escapeChar).tryParse text

end DockerfileModel.Parser.Instructions.Onbuild
