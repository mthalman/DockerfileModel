/-
  Main.lean — CLI entry point for differential testing.

  In one-shot mode, reads stdin and outputs canonical JSON. In --batch mode,
  reads tab-delimited request frames and writes one response frame per request.

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

/-- Convert a parser result to canonical JSON or a stable error. -/
def dispatchResult (name : String) (result : Option Instruction) : Except String String :=
  match result with
  | some inst => .ok (Json.Token.toJson inst.token)
  | none => .error s!"Parse error: failed to parse {name} instruction"

/-- Parse an instruction and return canonical JSON. -/
def parseInput (input : String) (escapeChar : Char) : Except String String :=
  let keyword := (firstWord input).toUpper
  match keyword with
  | "FROM"       => dispatchResult "FROM" (parseFrom input escapeChar)
  | "ARG"        => dispatchResult "ARG" (parseArg input escapeChar)
  | "MAINTAINER" => dispatchResult "MAINTAINER" (parseMaintainer input escapeChar)
  | "WORKDIR"    => dispatchResult "WORKDIR" (parseWorkdir input escapeChar)
  | "STOPSIGNAL" => dispatchResult "STOPSIGNAL" (parseStopsignal input escapeChar)
  | "CMD"        => dispatchResult "CMD" (parseCmd input escapeChar)
  | "ENTRYPOINT" => dispatchResult "ENTRYPOINT" (parseEntrypoint input escapeChar)
  | "SHELL"      => dispatchResult "SHELL" (parseShell input escapeChar)
  | "USER"       => dispatchResult "USER" (parseUser input escapeChar)
  | "EXPOSE"     => dispatchResult "EXPOSE" (parseExpose input escapeChar)
  | "VOLUME"     => dispatchResult "VOLUME" (parseVolume input escapeChar)
  | "ENV"        => dispatchResult "ENV" (parseEnv input escapeChar)
  | "LABEL"      => dispatchResult "LABEL" (parseLabel input escapeChar)
  | "RUN"        => dispatchResult "RUN" (parseRun input escapeChar)
  | "COPY"       => dispatchResult "COPY" (parseCopy input escapeChar)
  | "ADD"        => dispatchResult "ADD" (parseAdd input escapeChar)
  | "HEALTHCHECK" => dispatchResult "HEALTHCHECK" (parseHealthcheck input escapeChar)
  | "ONBUILD"    => dispatchResult "ONBUILD" (parseOnbuild input escapeChar)
  | _ =>
    if keyword.isEmpty then
      .error "Parse error: failed to detect instruction type"
    else
      .error s!"Unknown instruction type: {keyword}"

/-- Parse --escape flag from command-line arguments. Returns the escape char (default: backslash). -/
def parseEscapeArg (args : List String) : Char :=
  match args with
  | "--escape" :: val :: _ =>
    match val.toList with
    | [c] => c
    | _ => '\\'
  | _ :: rest => parseEscapeArg rest
  | [] => '\\'

def base64Char (value : Nat) : Char :=
  if value < 26 then
    Char.ofNat ('A'.toNat + value)
  else if value < 52 then
    Char.ofNat ('a'.toNat + value - 26)
  else if value < 62 then
    Char.ofNat ('0'.toNat + value - 52)
  else if value == 62 then '+'
  else '/'

def base64Value (char : Char) : Option Nat :=
  let value := char.toNat
  if 'A'.toNat ≤ value && value ≤ 'Z'.toNat then
    some (value - 'A'.toNat)
  else if 'a'.toNat ≤ value && value ≤ 'z'.toNat then
    some (value - 'a'.toNat + 26)
  else if '0'.toNat ≤ value && value ≤ '9'.toNat then
    some (value - '0'.toNat + 52)
  else if char == '+' then some 62
  else if char == '/' then some 63
  else none

def encodeBase64Bytes : List UInt8 → List Char
  | first :: second :: third :: rest =>
    let a := first.toNat
    let b := second.toNat
    let c := third.toNat
    base64Char (a / 4) ::
      base64Char ((a % 4) * 16 + b / 16) ::
      base64Char ((b % 16) * 4 + c / 64) ::
      base64Char (c % 64) ::
      encodeBase64Bytes rest
  | first :: second :: [] =>
    let a := first.toNat
    let b := second.toNat
    [base64Char (a / 4),
      base64Char ((a % 4) * 16 + b / 16),
      base64Char ((b % 16) * 4),
      '=']
  | first :: [] =>
    let a := first.toNat
    [base64Char (a / 4), base64Char ((a % 4) * 16), '=', '=']
  | [] => []

def encodeBase64 (value : String) : String :=
  String.ofList (encodeBase64Bytes value.toUTF8.toList)

def decodeQuartet (a b c d : Char) : Option (List UInt8) := do
  let first ← base64Value a
  let second ← base64Value b
  if c == '=' then
    if d == '=' then
      return [UInt8.ofNat (first * 4 + second / 16)]
    else
      none
  else
    let third ← base64Value c
    let firstByte := UInt8.ofNat (first * 4 + second / 16)
    let secondByte := UInt8.ofNat ((second % 16) * 16 + third / 4)
    if d == '=' then
      return [firstByte, secondByte]
    else
      let fourth ← base64Value d
      return [
        firstByte,
        secondByte,
        UInt8.ofNat ((third % 4) * 64 + fourth)]

def decodeBase64Chars : List Char → Option (List UInt8)
  | [] => some []
  | a :: b :: c :: d :: rest => do
    let bytes ← decodeQuartet a b c d
    if (c == '=' || d == '=') && !rest.isEmpty then
      none
    else
      let remaining ← decodeBase64Chars rest
      return bytes ++ remaining
  | _ => none

def decodeBase64 (value : String) : Option String := do
  let bytes ← decodeBase64Chars value.toList
  String.fromUTF8? ⟨bytes.toArray⟩

def writeFrame (requestId status body : String) : IO Unit := do
  let stdout ← IO.getStdout
  stdout.putStrLn s!"{requestId}\t{status}\t{encodeBase64 body}"
  stdout.flush

def stripFrameLineEnding (line : String) : String :=
  let chars := line.toList
  if line.endsWith "\r\n" then
    String.ofList chars.dropLast.dropLast
  else if line.endsWith "\n" || line.endsWith "\r" then
    String.ofList chars.dropLast
  else
    line

example : stripFrameLineEnding "5\t92\t\n" = "5\t92\t" := by native_decide

def processBatchLine (line : String) : IO Unit := do
  match (stripFrameLineEnding line).splitOn "\t" with
  | [requestId, escapeCode, payload] =>
    match escapeCode.toNat?, decodeBase64 payload with
    | some code, some input =>
      match parseInput input (Char.ofNat code) with
      | .ok json => writeFrame requestId "ok" json
      | .error message =>
        let status := if message.startsWith "Parse error:" then "parse-error" else "error"
        writeFrame requestId status message
    | _, _ => writeFrame requestId "error" "Invalid request encoding"
  | requestId :: _ => writeFrame requestId "error" "Malformed request frame"
  | [] => writeFrame "?" "error" "Malformed request frame"

def runBatch : IO UInt32 := do
  let stdin ← IO.getStdin
  let mut done := false
  while !done do
    let line ← stdin.getLine
    if line.isEmpty then
      done := true
    else
      processBatchLine line
  return 0

def runOneShot (args : List String) : IO UInt32 := do
  let escapeChar := parseEscapeArg args
  let input ← readAllStdin
  match parseInput input escapeChar with
  | .ok json =>
    IO.println json
    return 0
  | .error message =>
    IO.eprintln message
    return 1

def main (args : List String) : IO UInt32 :=
  if args.contains "--batch" then runBatch else runOneShot args
