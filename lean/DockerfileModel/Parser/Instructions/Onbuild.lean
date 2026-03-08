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
import DockerfileModel.Parser.Instructions.Arg
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
open Workdir Stopsignal Cmd Entrypoint Shell User Expose Volume Env Label
open Run Copy Add Healthcheck

-- ============================================================
-- Trigger instruction dispatch parser
-- ============================================================

/-- Peek the trigger keyword without consuming input.
    Reads leading whitespace then the first alphabetic word, returning it
    uppercased for case-insensitive matching. -/
private def peekTriggerKeyword : Parser String :=
  lookAhead (do
    let _ ← many (satisfy (fun c => c == ' ' || c == '\t') "whitespace")
    let word ← many1Chars letter
    Parser.pure word.toUpper)

/-- Parse a trigger instruction by peeking the keyword and dispatching directly
    to the corresponding instruction parser.

    This mirrors C#'s `Instruction.CreateInstruction()` which detects the keyword
    and calls the appropriate instruction-specific parser. By peeking the keyword
    first (without consuming input) and then invoking the one matching parser,
    we get committed-choice semantics: if the keyword matches but the arguments
    are invalid, the real parse error is reported instead of falling through to
    later alternatives and surfacing a misleading error.

    Of the 18 instruction types, 15 are included here.
    Three are excluded because BuildKit explicitly rejects them as ONBUILD triggers:
    ONBUILD (no chaining), FROM, and MAINTAINER.

    Returns an InstructionToken wrapping the fully parsed trigger instruction. -/
partial def triggerInstructionParser (escapeChar : Char) : Parser Token := do
  let keyword ← peekTriggerKeyword
  let tokens ← match keyword with
    | "ARG"         => argInstructionParser escapeChar
    | "WORKDIR"     => workdirInstructionParser escapeChar
    | "STOPSIGNAL"  => stopsignalInstructionParser escapeChar
    | "CMD"         => cmdInstructionParser escapeChar
    | "ENTRYPOINT"  => entrypointInstructionParser escapeChar
    | "SHELL"       => shellInstructionParser escapeChar
    | "USER"        => userInstructionParser escapeChar
    | "EXPOSE"      => exposeInstructionParser escapeChar
    | "VOLUME"      => volumeInstructionParser escapeChar
    | "ENV"         => envInstructionParser escapeChar
    | "LABEL"       => labelInstructionParser escapeChar
    | "RUN"         => runInstructionParser escapeChar
    | "COPY"        => copyInstructionParser escapeChar
    | "ADD"         => addInstructionParser escapeChar
    | "HEALTHCHECK" => healthcheckInstructionParser escapeChar
    | "ONBUILD"     => Parser.fail "ONBUILD ONBUILD is not allowed"
    | "FROM"        => Parser.fail "ONBUILD FROM is not allowed"
    | "MAINTAINER"  => Parser.fail "ONBUILD MAINTAINER is not allowed"
    | other         => Parser.fail s!"unknown or unsupported ONBUILD trigger instruction: {other}"
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
