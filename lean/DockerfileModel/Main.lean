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

open DockerfileModel
open DockerfileModel.Parser.Instructions

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

def main : IO UInt32 := do
  let input ← readAllStdin
  let keyword := (firstWord input).toUpper
  match keyword with
  | "FROM" =>
    match parseFrom input with
    | some inst =>
      IO.println (Json.Token.toJson inst.token)
      return 0
    | none =>
      IO.eprintln s!"Parse error: failed to parse FROM instruction"
      return 1
  | "ARG" =>
    match parseArg input with
    | some inst =>
      IO.println (Json.Token.toJson inst.token)
      return 0
    | none =>
      IO.eprintln s!"Parse error: failed to parse ARG instruction"
      return 1
  | _ =>
    IO.eprintln s!"Unknown instruction type: {keyword}"
    return 1
