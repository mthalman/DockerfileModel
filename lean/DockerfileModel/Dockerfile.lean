/-
  Dockerfile.lean — Top-level Dockerfile structure.

  This mirrors the C# Dockerfile class:
    - A Dockerfile is a list of DockerfileConstruct items.
    - DockerfileConstruct has four types: Instruction, Comment, ParserDirective, Whitespace.
    - Dockerfile.ToString() concatenates all constructs' ToString() results.

  In C#:
  ```csharp
  public class Dockerfile : IConstructContainer {
      public IList<DockerfileConstruct> Items { get; }
      // ToString inherited via items
  }

  public abstract class DockerfileConstruct : AggregateToken {
      public abstract ConstructType Type { get; }
  }

  public enum ConstructType { Instruction, Comment, ParserDirective, Whitespace }
  ```
-/

import DockerfileModel.Token
import DockerfileModel.Instruction

namespace DockerfileModel

/-- The type of a Dockerfile construct, mirroring C# ConstructType enum. -/
inductive ConstructType where
  | instruction
  | comment
  | parserDirective
  | whitespace
  deriving Repr, BEq, Inhabited, DecidableEq

/--
  A Dockerfile construct — one top-level element in a Dockerfile.

  Each construct is a tagged token: the ConstructType tag identifies what kind
  of element it is, and the Token holds the full token tree.

  In C#, DockerfileConstruct extends AggregateToken, so every construct IS a
  token. We model this by wrapping a Token with a type tag.
-/
structure DockerfileConstruct where
  type : ConstructType
  token : Token
  deriving Repr, BEq

namespace DockerfileConstruct

/-- Convert a construct to its string representation via its token. -/
def toString (c : DockerfileConstruct) : String :=
  Token.toString c.token

instance : ToString DockerfileConstruct where
  toString := DockerfileConstruct.toString

/-- Create an instruction construct from an Instruction. -/
def fromInstruction (inst : Instruction) : DockerfileConstruct :=
  { type := .instruction, token := inst.token }

/-- Create a comment construct. -/
def mkComment (token : Token) : DockerfileConstruct :=
  { type := .comment, token := token }

/-- Create a parser directive construct. -/
def mkParserDirective (token : Token) : DockerfileConstruct :=
  { type := .parserDirective, token := token }

/-- Create a whitespace construct. -/
def mkWhitespace (token : Token) : DockerfileConstruct :=
  { type := .whitespace, token := token }

end DockerfileConstruct

/--
  A Dockerfile is a list of constructs.

  In C#, Dockerfile.ToString() is inherited from the fact that a Dockerfile
  contains a list of DockerfileConstruct items, each of which is an AggregateToken.
  The constructs' ToString() results are concatenated.
-/
structure Dockerfile where
  items : List DockerfileConstruct
  deriving Repr, BEq

namespace Dockerfile

/-- The default escape character. -/
def defaultEscapeChar : Char := '\\'

/--
  Convert a Dockerfile to its string representation.
  This is the concatenation of all constructs' toString results,
  preserving character-for-character fidelity — the core invariant
  of the Valleysoft.DockerfileModel library.
-/
def toString (df : Dockerfile) : String :=
  String.join (df.items.map DockerfileConstruct.toString)

instance : ToString Dockerfile where
  toString := Dockerfile.toString

/-- Create an empty Dockerfile. -/
def empty : Dockerfile :=
  { items := [] }

/-- Get all instruction constructs from a Dockerfile. -/
def instructions (df : Dockerfile) : List DockerfileConstruct :=
  df.items.filter (fun c => c.type == .instruction)

/-- Get all comment constructs from a Dockerfile. -/
def comments (df : Dockerfile) : List DockerfileConstruct :=
  df.items.filter (fun c => c.type == .comment)

/-- Get all parser directive constructs from a Dockerfile. -/
def parserDirectives (df : Dockerfile) : List DockerfileConstruct :=
  df.items.filter (fun c => c.type == .parserDirective)

end Dockerfile

end DockerfileModel
