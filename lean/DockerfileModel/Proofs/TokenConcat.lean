/-
  Proofs/TokenConcat.lean — First formal proof: toString == concat children.

  This proves the fundamental token concatenation property:
  For an aggregate token without special overrides (not a VariableRefToken,
  no quote wrapping), toString is exactly the concatenation of children's
  toString results.

  This corresponds to the C# implementation:
  ```csharp
  // AggregateToken.GetUnderlyingValue
  protected override string GetUnderlyingValue(TokenStringOptions options) {
      return String.Concat(
          Tokens.Select(token => token.ToString(options)));
  }

  // Token.ToString (when not IQuotableToken or quotes excluded)
  public string ToString(TokenStringOptions options) {
      string value = GetUnderlyingValue(options);
      // No quote wrapping for non-IQuotableToken
      return value;
  }
  ```

  The proof establishes that our Lean model faithfully captures this behavior.
-/

import DockerfileModel.Token
import DockerfileModel.Dockerfile

namespace DockerfileModel

/--
  **Core theorem**: For any aggregate token that is NOT a VariableRefToken
  and has no quote info, its `toString` is exactly `String.join` of the
  children's `toString` values.

  This mirrors the C# behavior where `AggregateToken.GetUnderlyingValue`
  concatenates all children via `String.Concat(Tokens.Select(t => t.ToString()))`,
  and `Token.ToString` returns the underlying value directly when the token
  does not implement `IQuotableToken` (or quotes are excluded).
-/
theorem token_toString_aggregate (kind : AggregateKind) (tokens : List Token)
    (hkind : kind ≠ .variableRef) :
    Token.toString (.aggregate kind tokens none) =
    String.join (tokens.map Token.toString) := by
  unfold Token.toString
  cases kind <;> simp_all

/--
  Specialized version for each non-variableRef aggregate kind.
  These are simpler to state and useful as rewrite lemmas.
-/
theorem token_toString_keyword (tokens : List Token) :
    Token.toString (.aggregate .keyword tokens none) =
    String.join (tokens.map Token.toString) := by
  unfold Token.toString; rfl

theorem token_toString_literal (tokens : List Token) :
    Token.toString (.aggregate .literal tokens none) =
    String.join (tokens.map Token.toString) := by
  unfold Token.toString; rfl

theorem token_toString_comment (tokens : List Token) :
    Token.toString (.aggregate .comment tokens none) =
    String.join (tokens.map Token.toString) := by
  unfold Token.toString; rfl

theorem token_toString_instruction_kind (tokens : List Token) :
    Token.toString (.aggregate .instruction tokens none) =
    String.join (tokens.map Token.toString) := by
  unfold Token.toString; rfl

/--
  **VariableRefToken theorem**: For a VariableRefToken without quote info,
  its `toString` prepends "$" before concatenating children.

  This mirrors the C# override:
  ```csharp
  protected override string GetUnderlyingValue(TokenStringOptions options) {
      return $"${base.GetUnderlyingValue(options)}";
  }
  ```
-/
theorem token_toString_variableRef (tokens : List Token) :
    Token.toString (.aggregate .variableRef tokens none) =
    "$" ++ String.join (tokens.map Token.toString) := by
  unfold Token.toString; rfl

/--
  **IQuotableToken theorem**: For an aggregate token with quote info
  that is NOT a VariableRefToken, its `toString` wraps the concatenated
  children value in the quote character.

  This mirrors the C# behavior:
  ```csharp
  if (!options.ExcludeQuotes && this is IQuotableToken quotableToken) {
      return $"{quotableToken.QuoteChar}{value}{quotableToken.QuoteChar}";
  }
  ```
-/
theorem token_toString_quoted (kind : AggregateKind) (tokens : List Token) (qi : QuoteInfo)
    (hkind : kind ≠ .variableRef) :
    Token.toString (.aggregate kind tokens (some qi)) =
    String.singleton qi.quoteChar ++ String.join (tokens.map Token.toString) ++
    String.singleton qi.quoteChar := by
  unfold Token.toString
  cases kind <;> simp_all

/--
  **Primitive token theorem**: For a primitive token, `toString` returns
  its value directly.

  This mirrors `PrimitiveToken.GetUnderlyingValue`:
  ```csharp
  protected override string GetUnderlyingValue(TokenStringOptions options) => Value;
  ```
-/
theorem token_toString_primitive (kind : PrimitiveKind) (value : String) :
    Token.toString (.primitive kind value) = value := by
  unfold Token.toString; rfl

/--
  **Dockerfile toString theorem**: A Dockerfile's `toString` is the concatenation
  of all its constructs' `toString` values.

  This mirrors the C# behavior where Dockerfile items are all DockerfileConstruct
  instances (which are AggregateTokens), and their concatenation produces the
  full Dockerfile text.
-/
theorem dockerfile_toString_concat (items : List DockerfileConstruct) :
    Dockerfile.toString { items := items } =
    String.join (items.map DockerfileConstruct.toString) := by
  unfold Dockerfile.toString; rfl

/--
  **VariableRefToken quoted theorem**: For a VariableRefToken with quote info,
  its `toString` wraps the "$"-prefixed concatenation in quote characters.
-/
theorem token_toString_variableRef_quoted (tokens : List Token) (qi : QuoteInfo) :
    Token.toString (.aggregate .variableRef tokens (some qi)) =
    String.singleton qi.quoteChar ++ ("$" ++ String.join (tokens.map Token.toString)) ++
    String.singleton qi.quoteChar := by
  unfold Token.toString; rfl

end DockerfileModel
