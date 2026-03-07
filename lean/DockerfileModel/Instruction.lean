/-
  Instruction.lean — Model of all 18 Dockerfile instruction types.

  This mirrors the C# instruction hierarchy:
    Instruction (abstract : DockerfileConstruct)
      ├── FromInstruction
      ├── RunInstruction
      ├── CmdInstruction
      ├── EntrypointInstruction
      ├── CopyInstruction
      ├── AddInstruction
      ├── EnvInstruction
      ├── ArgInstruction
      ├── ExposeInstruction
      ├── VolumeInstruction
      ├── UserInstruction
      ├── WorkdirInstruction
      ├── LabelInstruction
      ├── StopSignalInstruction
      ├── HealthCheckInstruction
      ├── ShellInstruction
      ├── MaintainerInstruction
      └── OnBuildInstruction

  Each instruction in C# extends Instruction, which extends DockerfileConstruct,
  which extends AggregateToken. So every instruction is ultimately an aggregate
  token containing a keyword token followed by argument tokens.
-/

import DockerfileModel.Token

namespace DockerfileModel

/-- All 18 Dockerfile instruction types, matching the C# class hierarchy. -/
inductive InstructionName where
  | from
  | run
  | cmd
  | entrypoint
  | copy
  | add
  | env
  | arg
  | expose
  | volume
  | user
  | workdir
  | label
  | stopSignal
  | healthCheck
  | shell
  | maintainer
  | onBuild
  deriving Repr, BEq, Inhabited, DecidableEq

namespace InstructionName

/-- Convert an instruction name to its Dockerfile keyword string. -/
def toKeyword : InstructionName → String
  | .from => "FROM"
  | .run => "RUN"
  | .cmd => "CMD"
  | .entrypoint => "ENTRYPOINT"
  | .copy => "COPY"
  | .add => "ADD"
  | .env => "ENV"
  | .arg => "ARG"
  | .expose => "EXPOSE"
  | .volume => "VOLUME"
  | .user => "USER"
  | .workdir => "WORKDIR"
  | .label => "LABEL"
  | .stopSignal => "STOPSIGNAL"
  | .healthCheck => "HEALTHCHECK"
  | .shell => "SHELL"
  | .maintainer => "MAINTAINER"
  | .onBuild => "ONBUILD"

instance : ToString InstructionName where
  toString := InstructionName.toKeyword

/-- All instruction names as a list. -/
def all : List InstructionName :=
  [.from, .run, .cmd, .entrypoint, .copy, .add, .env, .arg,
   .expose, .volume, .user, .workdir, .label, .stopSignal,
   .healthCheck, .shell, .maintainer, .onBuild]

/-- There are exactly 18 instruction types. -/
theorem all_length : all.length = 18 := by native_decide

end InstructionName

/--
  An Instruction pairs an instruction name with a token representation.

  In C#, an instruction IS an AggregateToken (it inherits from DockerfileConstruct
  which inherits from AggregateToken). The token contains the keyword, whitespace,
  and argument tokens as children.

  Here we keep the instruction name separate for easy pattern matching, while the
  token field holds the full token tree (including the keyword token itself).
-/
structure Instruction where
  name : InstructionName
  token : Token
  deriving Repr, BEq

namespace Instruction

/-- Convert an instruction to its string representation via its token. -/
def toString (inst : Instruction) : String :=
  Token.toString inst.token

instance : ToString Instruction where
  toString := Instruction.toString

/--
  Create an instruction token from a name and argument tokens.
  The result is an aggregate instruction token containing:
  [KeywordToken(name), WhitespaceToken(" "), ...argTokens]
-/
def mkSimple (name : InstructionName) (argTokens : List Token) : Instruction :=
  let keywordToken := Token.mkKeyword [Token.mkString name.toKeyword]
  let wsToken := Token.mkWhitespace " "
  let allChildren := keywordToken :: wsToken :: argTokens
  { name := name, token := Token.mkInstruction allChildren }

end Instruction

end DockerfileModel
