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
import Std.Data.HashMap

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

/-- Keep JSON-path parsing independent of modifier nesting depth. -/
def jsonVolumeVariableParser (escapeChar : Char) (text : String) : Parser Token := Id.run do
  let mut candidates : List Position := []
  let mut position := Position.ofString text
  while !position.atEnd do
    if position.current == some escapeChar then
      position := position.next.next
    else
      if position.current == some '$' then
        match position.next.current with
        | some next =>
          if next.isAlpha || next.isDigit || next == '_' || next == '{' then
            candidates := position :: candidates
        | none => Pure.pure ()
      position := position.next
  let mut parsed : Std.HashMap Nat (ParseResult Token) := {}
  for candidate in candidates do
    let cached : Parser Token := fun pos =>
      (parsed[pos.offset.byteIdx]?).getD (.error "expected variable reference" pos)
    parsed := parsed.insert candidate.offset.byteIdx
      (variableRefParser escapeChar (some cached) candidate)
  return fun pos =>
    (parsed[pos.offset.byteIdx]?).getD (.error "expected variable reference" pos)

/-- JSON backslashes remain escapes even when Dockerfile continuations use backticks. -/
def jsonVolumePathParser (referenceParser : Parser Token) : Parser Token := do
  let _ ← char '"'
  let children ← many (or' referenceParser
    (or'
      (do let escaped ← jsonEscapeSequence; Parser.pure (Token.mkString escaped))
      (or'
        (do
          let text ← many1Chars (satisfy (fun c => c != '"' && c != '\\' && c != '$')
            "JSON string character")
          Parser.pure (Token.mkString text))
        (toStringToken (char '$')))))
  let _ ← char '"'
  Parser.pure (Token.mkLiteral (collapseStringTokens children) (some ⟨'"'⟩))

/-- BuildKit's VolumeCommand.Expand expands JSON paths too. Validate JSON syntax
    before recognizing variable references, without changing exec-command parsing. -/
def jsonVolumePathsParser (escapeChar : Char) : Parser (List Token) := do
  let tokens ← maybeJsonArrayParser (volumePathsParser escapeChar) escapeChar
  if !tokens.any (fun token => token == Token.mkSymbol '[') then
    return tokens
  tokens.mapM fun token =>
    match token with
    | .aggregate .literal _ _ =>
      let text := Token.toString token
      let referenceParser := jsonVolumeVariableParser escapeChar text
      match (jsonVolumePathParser referenceParser).run text with
      | .ok path remaining =>
        if remaining.atEnd then Parser.pure path
        else Parser.fail "unconsumed variable-aware VOLUME path"
      | .error _ _ => Parser.fail "invalid variable-aware VOLUME path"
    | _ => Parser.pure token

/-- Parse the arguments of a VOLUME instruction: exec form or space-separated paths.
    Corresponds to VolumeInstruction.GetArgsParser() -/
partial def volumeArgsParser (escapeChar : Char) : Parser (List Token) :=
  argTokens (jsonVolumePathsParser escapeChar) escapeChar
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
