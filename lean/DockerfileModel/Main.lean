/-
  Main.lean — CLI entry point for differential testing.

  Reads all of stdin, detects instruction type from first keyword (case-insensitive),
  calls the appropriate parser, and outputs canonical JSON to stdout.

  Exit code 0 on success, 1 on parse failure (with error to stderr).

  Usage:
    echo "FROM alpine" | DockerfileModelDiffTest
    echo "ARG MY_VAR=hello" | DockerfileModelDiffTest
    echo "FROM alpine" | DockerfileModelDiffTest --escape `
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

/-- Parse --escape flag from command-line arguments. Returns the escape char (default: backslash). -/
def parseEscapeArg (args : List String) : Char :=
  match args with
  | "--escape" :: val :: _ =>
    match val.toList with
    | [c] => c
    | _ => '\\'
  | _ :: rest => parseEscapeArg rest
  | [] => '\\'

def main (args : List String) : IO UInt32 := do
  let escapeChar := parseEscapeArg args
  let input ← readAllStdin
  let keyword := (firstWord input).toUpper
  match keyword with
  | "FROM"       => dispatchParse "FROM" (parseFrom input escapeChar)
  | "ARG"        => dispatchParse "ARG" (parseArg input escapeChar)
  | "MAINTAINER" => dispatchParse "MAINTAINER" (parseMaintainer input escapeChar)
  | "WORKDIR"    => dispatchParse "WORKDIR" (parseWorkdir input escapeChar)
  | "STOPSIGNAL" => dispatchParse "STOPSIGNAL" (parseStopsignal input escapeChar)
  | "CMD"        => dispatchParse "CMD" (parseCmd input escapeChar)
  | "ENTRYPOINT" => dispatchParse "ENTRYPOINT" (parseEntrypoint input escapeChar)
  | "SHELL"      => dispatchParse "SHELL" (parseShell input escapeChar)
  | "USER"       => dispatchParse "USER" (parseUser input escapeChar)
  | "EXPOSE"     => dispatchParse "EXPOSE" (parseExpose input escapeChar)
  | "VOLUME"     => dispatchParse "VOLUME" (parseVolume input escapeChar)
  | "ENV"        => dispatchParse "ENV" (parseEnv input escapeChar)
  | "LABEL"      => dispatchParse "LABEL" (parseLabel input escapeChar)
  | "RUN"        => dispatchParse "RUN" (parseRun input escapeChar)
  | "COPY"       => dispatchParse "COPY" (parseCopy input escapeChar)
  | "ADD"        => dispatchParse "ADD" (parseAdd input escapeChar)
  | "HEALTHCHECK" => dispatchParse "HEALTHCHECK" (parseHealthcheck input escapeChar)
  | "ONBUILD"    => dispatchParse "ONBUILD" (parseOnbuild input escapeChar)
  | _ =>
    IO.eprintln s!"Unknown instruction type: {keyword}"
    return 1
