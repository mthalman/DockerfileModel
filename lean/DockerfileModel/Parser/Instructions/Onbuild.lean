/-
  Parser/Instructions/Onbuild.lean -- ONBUILD instruction parser.

  Parses the ONBUILD instruction:
    ONBUILD TriggerInstruction

  After parsing the ONBUILD keyword, the trigger instruction is RECURSIVELY
  parsed as a full instruction using the same dispatch as top-level
  instructions. This matches BuildKit's `parseSubCommand` which calls
  `newNodeFromLine(rest, d)` to fully parse the trigger.

  The result is:
    ONBUILD InstructionToken [ ... trigger instruction tokens ... ]

  For example, `ONBUILD RUN echo hello` produces:
    InstructionToken [
      KeywordToken("ONBUILD"),
      WhitespaceToken(" "),
      InstructionToken [
        KeywordToken("RUN"),
        WhitespaceToken(" "),
        LiteralToken("echo hello")
      ]
    ]

  Token structure produced:
    InstructionToken [
      WhitespaceToken?,           -- leading whitespace
      KeywordToken("ONBUILD"),    -- instruction keyword
      WhitespaceToken(" "),       -- separator
      InstructionToken(trigger)   -- recursively parsed trigger instruction
    ]
-/

import DockerfileModel.Token
import DockerfileModel.Instruction
import DockerfileModel.Parser.Basic
import DockerfileModel.Parser.Combinators
import DockerfileModel.Parser.DockerfileParsers
import DockerfileModel.Parser.Instructions.From
import DockerfileModel.Parser.Instructions.Arg
import DockerfileModel.Parser.Instructions.Maintainer
import DockerfileModel.Parser.Instructions.Workdir
import DockerfileModel.Parser.Instructions.Stopsignal
import DockerfileModel.Parser.Instructions.Cmd
import DockerfileModel.Parser.Instructions.Entrypoint
import DockerfileModel.Parser.Instructions.Shell
import DockerfileModel.Parser.Instructions.User
import DockerfileModel.Parser.Instructions.Expose
import DockerfileModel.Parser.Instructions.Volume
import DockerfileModel.Parser.Instructions.Env
import DockerfileModel.Parser.Instructions.Label
import DockerfileModel.Parser.Instructions.Run
import DockerfileModel.Parser.Instructions.Copy
import DockerfileModel.Parser.Instructions.Add
import DockerfileModel.Parser.Instructions.Healthcheck

namespace DockerfileModel.Parser.Instructions.Onbuild

open DockerfileModel
open DockerfileModel.Parser
open DockerfileModel.Parser.Instructions
open Parser
open Maintainer Workdir Stopsignal Cmd Entrypoint Shell User Expose Volume Env Label
open Run Copy Add Healthcheck

-- ============================================================
-- Trigger instruction dispatch parser
-- ============================================================

/-- Parse a trigger instruction by dispatching to all known instruction parsers.
    This mirrors C#'s `Instruction.CreateInstruction()` which detects the keyword
    and calls the appropriate instruction-specific parser.

    Each instruction parser starts with its keyword, so `or'` naturally dispatches
    based on the first token. All 18 instruction types are included — C# does not
    restrict triggers at parse time (validation happens at a higher layer).

    Returns an InstructionToken wrapping the fully parsed trigger instruction. -/
partial def triggerInstructionParser (escapeChar : Char) : Parser Token := do
  let tokens ←
    or' (fromInstructionParser escapeChar)
    (or' (argInstructionParser escapeChar)
    (or' (maintainerInstructionParser escapeChar)
    (or' (workdirInstructionParser escapeChar)
    (or' (stopsignalInstructionParser escapeChar)
    (or' (cmdInstructionParser escapeChar)
    (or' (entrypointInstructionParser escapeChar)
    (or' (shellInstructionParser escapeChar)
    (or' (userInstructionParser escapeChar)
    (or' (exposeInstructionParser escapeChar)
    (or' (volumeInstructionParser escapeChar)
    (or' (envInstructionParser escapeChar)
    (or' (labelInstructionParser escapeChar)
    (or' (runInstructionParser escapeChar)
    (or' (copyInstructionParser escapeChar)
    (or' (addInstructionParser escapeChar)
         (healthcheckInstructionParser escapeChar))))))))))))))))
  Parser.pure (Token.mkInstruction tokens)

-- ============================================================
-- ONBUILD args parser
-- ============================================================

/-- Parse the arguments of an ONBUILD instruction: a recursively parsed trigger
    instruction.
    Corresponds to OnBuildInstruction.GetArgsParser() -/
partial def onbuildArgsParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (do
    let triggerToken ← triggerInstructionParser escapeChar
    Parser.pure [triggerToken]) escapeChar

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
