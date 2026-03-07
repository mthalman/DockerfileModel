/-
  Main.lean — CLI entry point for differential testing.

  Reads all of stdin, detects instruction type from first keyword (case-insensitive),
  calls the appropriate parser, and outputs canonical JSON to stdout.

  Exit code 0 on success, 1 on parse failure (with error to stderr).

  Usage:
    echo "FROM alpine" | DockerfileModelDiffTest
    echo "ARG MY_VAR=hello" | DockerfileModelDiffTest
-/

import DockerfileModel.Json
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
import DockerfileModel.Parser.Instructions.Onbuild

open DockerfileModel
open DockerfileModel.Parser.Instructions
open Maintainer Workdir Stopsignal Cmd Entrypoint Shell User Expose Volume Env Label
open Run Copy Add Healthcheck Onbuild

/-- Extract the first whitespace-delimited word from a string. -/
def firstWord (s : String) : String :=
  let trimmed := s.trimAsciiStart.toString
  let chars := trimmed.toList
  let word := chars.takeWhile (fun c => !c.isWhitespace)
  String.ofList word

/-- Read all of stdin into a string. -/
def readAllStdin : IO String := do
  let stdin ← IO.getStdin
  let mut result := ""
  let mut done := false
  while !done do
    let line ← stdin.getLine
    if line.isEmpty then
      done := true
    else
      result := result ++ line
  return result

/-- Helper: dispatch a parse function and output JSON or error. -/
def dispatchParse (name : String) (result : Option Instruction) : IO UInt32 :=
  match result with
  | some inst => do
    IO.println (Json.Token.toJson inst.token)
    return 0
  | none => do
    IO.eprintln s!"Parse error: failed to parse {name} instruction"
    return 1

def main : IO UInt32 := do
  let input ← readAllStdin
  let keyword := (firstWord input).toUpper
  match keyword with
  | "FROM"       => dispatchParse "FROM" (parseFrom input)
  | "ARG"        => dispatchParse "ARG" (parseArg input)
  | "MAINTAINER" => dispatchParse "MAINTAINER" (parseMaintainer input)
  | "WORKDIR"    => dispatchParse "WORKDIR" (parseWorkdir input)
  | "STOPSIGNAL" => dispatchParse "STOPSIGNAL" (parseStopsignal input)
  | "CMD"        => dispatchParse "CMD" (parseCmd input)
  | "ENTRYPOINT" => dispatchParse "ENTRYPOINT" (parseEntrypoint input)
  | "SHELL"      => dispatchParse "SHELL" (parseShell input)
  | "USER"       => dispatchParse "USER" (parseUser input)
  | "EXPOSE"     => dispatchParse "EXPOSE" (parseExpose input)
  | "VOLUME"     => dispatchParse "VOLUME" (parseVolume input)
  | "ENV"        => dispatchParse "ENV" (parseEnv input)
  | "LABEL"      => dispatchParse "LABEL" (parseLabel input)
  | "RUN"        => dispatchParse "RUN" (parseRun input)
  | "COPY"       => dispatchParse "COPY" (parseCopy input)
  | "ADD"        => dispatchParse "ADD" (parseAdd input)
  | "HEALTHCHECK" => dispatchParse "HEALTHCHECK" (parseHealthcheck input)
  | "ONBUILD"    => dispatchParse "ONBUILD" (parseOnbuild input)
  | _ =>
    IO.eprintln s!"Unknown instruction type: {keyword}"
    return 1
