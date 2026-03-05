using FsCheck;
using FsCheck.Fluent;
using Valleysoft.DockerfileModel.Tests.Generators;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>
/// Property-based tests using FsCheck generators from DockerfileArbitraries.
/// Each test verifies round-trip fidelity: Parse(text).ToString() == text.
///
/// Uses Gen.Sample() to draw random values from each generator, then asserts
/// round-trip preservation for every sample.
/// </summary>
public class PropertyTests
{
    private const int SampleCount = 200;
    private const int SampleSize = 50;

    /// <summary>
    /// Helper: sample values from a generator and assert a property holds for each.
    /// </summary>
    private static void AssertProperty(Gen<string> gen, Action<string> assertion)
    {
        IReadOnlyList<string> samples = gen.Sample(SampleSize, SampleCount);
        foreach (string text in samples)
        {
            assertion(text);
        }
    }

    // ──────────────────────────────────────────────
    // FROM instruction round-trip
    // ──────────────────────────────────────────────

    [Fact]
    public void FromInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.FromInstruction(), text =>
        {
            FromInstruction parsed = FromInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    // ──────────────────────────────────────────────
    // All other instruction round-trips
    // ──────────────────────────────────────────────

    [Fact]
    public void RunInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.RunInstruction(), text =>
        {
            RunInstruction parsed = RunInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void CmdInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.CmdInstruction(), text =>
        {
            CmdInstruction parsed = CmdInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void EntrypointInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.EntrypointInstruction(), text =>
        {
            EntrypointInstruction parsed = EntrypointInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void CopyInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.CopyInstruction(), text =>
        {
            CopyInstruction parsed = CopyInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void AddInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.AddInstruction(), text =>
        {
            AddInstruction parsed = AddInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void EnvInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.EnvInstruction(), text =>
        {
            EnvInstruction parsed = EnvInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void ArgInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.ArgInstruction(), text =>
        {
            ArgInstruction parsed = ArgInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void ExposeInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.ExposeInstruction(), text =>
        {
            ExposeInstruction parsed = ExposeInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void HealthCheckInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.HealthCheckInstruction(), text =>
        {
            HealthCheckInstruction parsed = HealthCheckInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void LabelInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.LabelInstruction(), text =>
        {
            LabelInstruction parsed = LabelInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void MaintainerInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.MaintainerInstruction(), text =>
        {
            MaintainerInstruction parsed = MaintainerInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void OnBuildInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.OnBuildInstruction(), text =>
        {
            OnBuildInstruction parsed = OnBuildInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void StopSignalInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.StopSignalInstruction(), text =>
        {
            StopSignalInstruction parsed = StopSignalInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void UserInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.UserInstruction(), text =>
        {
            UserInstruction parsed = UserInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void VolumeInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.VolumeInstruction(), text =>
        {
            VolumeInstruction parsed = VolumeInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void WorkdirInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.WorkdirInstruction(), text =>
        {
            WorkdirInstruction parsed = WorkdirInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void ShellInstruction_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.ShellInstruction(), text =>
        {
            ShellInstruction parsed = ShellInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    // ──────────────────────────────────────────────
    // Dockerfile-level round-trip
    // ──────────────────────────────────────────────

    [Fact]
    public void Dockerfile_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.ValidDockerfile(), text =>
        {
            Dockerfile parsed = Dockerfile.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    // ──────────────────────────────────────────────
    // Variable reference round-trip
    // ──────────────────────────────────────────────

    [Fact]
    public void VariableRef_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.VariableRef(), text =>
        {
            var parsed = Valleysoft.DockerfileModel.Tokens.VariableRefToken.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    // ──────────────────────────────────────────────
    // Line continuation round-trips
    // ──────────────────────────────────────────────

    [Fact]
    public void FromWithLineContinuation_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.FromWithLineContinuation(), text =>
        {
            FromInstruction parsed = FromInstruction.Parse(text);
            Assert.Equal(text, parsed.ToString());
        });
    }

    [Fact]
    public void FromWithBacktickContinuation_RoundTrips()
    {
        AssertProperty(DockerfileArbitraries.FromWithBacktickContinuation(), text =>
        {
            FromInstruction parsed = FromInstruction.Parse(text, '`');
            Assert.Equal(text, parsed.ToString());
        });
    }

    // ──────────────────────────────────────────────
    // P0-4: Token tree consistency property
    // For every AggregateToken in a parsed tree,
    // ToString() == string.Concat(children.Select(c => c.ToString()))
    // ──────────────────────────────────────────────

    /// <summary>
    /// Recursively walks the token tree and asserts the structural invariant:
    /// at every AggregateToken node, ToString() equals the concatenation of its children's ToString(),
    /// accounting for known decorations:
    /// - VariableRefToken overrides GetUnderlyingValue to prepend "$"
    /// - IQuotableToken wraps the value in quote characters
    /// </summary>
    private static void AssertTokenTreeConsistency(Token token)
    {
        if (token is AggregateToken aggregate)
        {
            string fromChildren = string.Concat(aggregate.Tokens.Select(t => t.ToString()));
            string expected = aggregate.ToString();

            // VariableRefToken adds a "$" prefix to its children concatenation
            if (aggregate is VariableRefToken)
            {
                fromChildren = "$" + fromChildren;
            }

            // IQuotableToken wraps the underlying value with quote characters
            if (aggregate is IQuotableToken quotable && quotable.QuoteChar.HasValue)
            {
                fromChildren = $"{quotable.QuoteChar}{fromChildren}{quotable.QuoteChar}";
            }

            Assert.Equal(expected, fromChildren);

            // Recurse into children
            foreach (Token child in aggregate.Tokens)
            {
                AssertTokenTreeConsistency(child);
            }
        }
    }

    [Fact]
    public void TokenTreeConsistency_Dockerfile()
    {
        AssertProperty(DockerfileArbitraries.ValidDockerfile(), text =>
        {
            Dockerfile parsed = Dockerfile.Parse(text);
            foreach (DockerfileConstruct construct in parsed.Items)
            {
                AssertTokenTreeConsistency(construct);
            }
        });
    }

    [Fact]
    public void TokenTreeConsistency_FromInstruction()
    {
        AssertProperty(DockerfileArbitraries.FromInstruction(), text =>
        {
            FromInstruction parsed = FromInstruction.Parse(text);
            AssertTokenTreeConsistency(parsed);
        });
    }

    [Fact]
    public void TokenTreeConsistency_RunInstruction()
    {
        AssertProperty(DockerfileArbitraries.RunInstruction(), text =>
        {
            RunInstruction parsed = RunInstruction.Parse(text);
            AssertTokenTreeConsistency(parsed);
        });
    }

    [Fact]
    public void TokenTreeConsistency_CopyInstruction()
    {
        AssertProperty(DockerfileArbitraries.CopyInstruction(), text =>
        {
            CopyInstruction parsed = CopyInstruction.Parse(text);
            AssertTokenTreeConsistency(parsed);
        });
    }

    [Fact]
    public void TokenTreeConsistency_AddInstruction()
    {
        AssertProperty(DockerfileArbitraries.AddInstruction(), text =>
        {
            AddInstruction parsed = AddInstruction.Parse(text);
            AssertTokenTreeConsistency(parsed);
        });
    }

    [Fact]
    public void TokenTreeConsistency_HealthCheckInstruction()
    {
        AssertProperty(DockerfileArbitraries.HealthCheckInstruction(), text =>
        {
            HealthCheckInstruction parsed = HealthCheckInstruction.Parse(text);
            AssertTokenTreeConsistency(parsed);
        });
    }

    [Fact]
    public void TokenTreeConsistency_VariableRef()
    {
        AssertProperty(DockerfileArbitraries.VariableRef(), text =>
        {
            var parsed = VariableRefToken.Parse(text);
            AssertTokenTreeConsistency(parsed);
        });
    }

    // ──────────────────────────────────────────────
    // P0-5: Variable resolution non-mutation property
    // ResolveVariables() without UpdateInline must not
    // change the model's ToString() output.
    // ──────────────────────────────────────────────

    [Fact]
    public void VariableResolution_DoesNotMutateModel()
    {
        AssertProperty(DockerfileArbitraries.DockerfileWithVariables(), text =>
        {
            Dockerfile parsed = Dockerfile.Parse(text);
            string beforeResolve = parsed.ToString();

            // Resolve with default options (UpdateInline = false)
            parsed.ResolveVariables();

            string afterResolve = parsed.ToString();
            Assert.Equal(beforeResolve, afterResolve);
        });
    }

    [Fact]
    public void VariableResolution_WithOverrides_DoesNotMutateModel()
    {
        AssertProperty(DockerfileArbitraries.DockerfileWithVariables(), text =>
        {
            Dockerfile parsed = Dockerfile.Parse(text);
            string beforeResolve = parsed.ToString();

            // Resolve with overrides but still UpdateInline = false
            var overrides = new Dictionary<string, string> { { "anyvar", "anyval" } };
            parsed.ResolveVariables(overrides);

            string afterResolve = parsed.ToString();
            Assert.Equal(beforeResolve, afterResolve);
        });
    }

    [Fact]
    public void VariableResolution_ExplicitFalseUpdateInline_DoesNotMutateModel()
    {
        AssertProperty(DockerfileArbitraries.DockerfileWithVariables(), text =>
        {
            Dockerfile parsed = Dockerfile.Parse(text);
            string beforeResolve = parsed.ToString();

            // Explicitly set UpdateInline = false
            var options = new ResolutionOptions { UpdateInline = false };
            parsed.ResolveVariables(options: options);

            string afterResolve = parsed.ToString();
            Assert.Equal(beforeResolve, afterResolve);
        });
    }

    // ──────────────────────────────────────────────
    // P0-6: Modifier semantics properties
    // Test all 6 variable modifier behaviors at the
    // VariableRefToken level.
    // ──────────────────────────────────────────────

    /// <summary>
    /// Helper: builds a Dockerfile with FROM, optionally a stage-level ARG declaring a variable,
    /// and a LABEL referencing the variable with the given modifier syntax.
    /// Resolves the LABEL and returns the resolved string.
    /// </summary>
    /// <param name="declareArg">If true, declares the variable via ARG (making it "set" with null value).
    /// If false, the variable is never declared (truly "unset").</param>
    private static string ResolveModifierDockerfile(
        string varName, string modifier, string modValue,
        Dictionary<string, string> argOverrides = null,
        bool declareArg = true)
    {
        string argLine = declareArg ? $"ARG {varName}\n" : "";
        string dockerfile = $"FROM alpine\n{argLine}LABEL key=${{{varName}{modifier}{modValue}}}";
        Dockerfile parsed = Dockerfile.Parse(dockerfile);

        var label = parsed.Items.OfType<LabelInstruction>().First();
        return parsed.ResolveVariables(label, argOverrides);
    }

    [Fact]
    public void ModifierSemantics_ColonDash_ReturnsDefaultWhenUnset()
    {
        // ${VAR:-default} — returns default when VAR is unset
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            string result = ResolveModifierDockerfile(varName, ":-", modValue);
            // VAR is unset (no override, ARG has no default) => returns modValue
            Assert.Equal($"LABEL key={modValue}", result);
        }
    }

    [Fact]
    public void ModifierSemantics_ColonDash_ReturnsDefaultWhenEmpty()
    {
        // ${VAR:-default} — returns default when VAR is empty
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            var overrides = new Dictionary<string, string> { { varName, "" } };
            string result = ResolveModifierDockerfile(varName, ":-", modValue, overrides);
            Assert.Equal($"LABEL key={modValue}", result);
        }
    }

    [Fact]
    public void ModifierSemantics_ColonDash_ReturnsValueWhenSet()
    {
        // ${VAR:-default} — returns VAR's value when VAR is set and non-empty
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            var overrides = new Dictionary<string, string> { { varName, "realval" } };
            string result = ResolveModifierDockerfile(varName, ":-", modValue, overrides);
            Assert.Equal("LABEL key=realval", result);
        }
    }

    [Fact]
    public void ModifierSemantics_Dash_ReturnsDefaultWhenUnset()
    {
        // ${VAR-default} — returns default when VAR is truly unset (not in dictionary)
        // The non-colon "-" modifier only treats "not in dictionary" as unset.
        // Declaring ARG x (no default) puts x in stageArgs with null value, which counts as "set".
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            string result = ResolveModifierDockerfile(varName, "-", modValue, declareArg: false);
            Assert.Equal($"LABEL key={modValue}", result);
        }
    }

    [Fact]
    public void ModifierSemantics_Dash_ReturnsEmptyWhenSetEmpty()
    {
        // ${VAR-default} — returns empty when VAR is set (even if empty)
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            var overrides = new Dictionary<string, string> { { varName, "" } };
            string result = ResolveModifierDockerfile(varName, "-", modValue, overrides);
            Assert.Equal("LABEL key=", result);
        }
    }

    [Fact]
    public void ModifierSemantics_ColonPlus_ReturnsAltWhenSetNonEmpty()
    {
        // ${VAR:+alt} — returns alt when VAR is set AND non-empty
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            var overrides = new Dictionary<string, string> { { varName, "something" } };
            string result = ResolveModifierDockerfile(varName, ":+", modValue, overrides);
            Assert.Equal($"LABEL key={modValue}", result);
        }
    }

    [Fact]
    public void ModifierSemantics_ColonPlus_ReturnsNullWhenUnset()
    {
        // ${VAR:+alt} — returns null/empty when VAR is unset
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            string result = ResolveModifierDockerfile(varName, ":+", modValue);
            Assert.Equal("LABEL key=", result);
        }
    }

    [Fact]
    public void ModifierSemantics_ColonPlus_ReturnsNullWhenEmpty()
    {
        // ${VAR:+alt} — returns null/empty when VAR is set but empty
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            var overrides = new Dictionary<string, string> { { varName, "" } };
            string result = ResolveModifierDockerfile(varName, ":+", modValue, overrides);
            Assert.Equal("LABEL key=", result);
        }
    }

    [Fact]
    public void ModifierSemantics_Plus_ReturnsAltWhenSet()
    {
        // ${VAR+alt} — returns alt when VAR is set (even if empty)
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            var overrides = new Dictionary<string, string> { { varName, "" } };
            string result = ResolveModifierDockerfile(varName, "+", modValue, overrides);
            Assert.Equal($"LABEL key={modValue}", result);
        }
    }

    [Fact]
    public void ModifierSemantics_Plus_ReturnsNullWhenUnset()
    {
        // ${VAR+alt} — returns null/empty when VAR is truly unset (not in dictionary)
        // The non-colon "+" modifier only treats "not in dictionary" as unset.
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            string result = ResolveModifierDockerfile(varName, "+", modValue, declareArg: false);
            Assert.Equal("LABEL key=", result);
        }
    }

    [Fact]
    public void ModifierSemantics_ColonQuestion_ThrowsWhenUnset()
    {
        // ${VAR:?err} — throws when VAR is unset
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            Assert.Throws<VariableSubstitutionException>(() =>
                ResolveModifierDockerfile(varName, ":?", modValue));
        }
    }

    [Fact]
    public void ModifierSemantics_ColonQuestion_ThrowsWhenEmpty()
    {
        // ${VAR:?err} — throws when VAR is set but empty
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            var overrides = new Dictionary<string, string> { { varName, "" } };
            Assert.Throws<VariableSubstitutionException>(() =>
                ResolveModifierDockerfile(varName, ":?", modValue, overrides));
        }
    }

    [Fact]
    public void ModifierSemantics_ColonQuestion_SucceedsWhenSetNonEmpty()
    {
        // ${VAR:?err} — returns value when VAR is set and non-empty
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            var overrides = new Dictionary<string, string> { { varName, "realval" } };
            string result = ResolveModifierDockerfile(varName, ":?", modValue, overrides);
            Assert.Equal("LABEL key=realval", result);
        }
    }

    [Fact]
    public void ModifierSemantics_Question_ThrowsWhenUnset()
    {
        // ${VAR?err} — throws when VAR is truly unset (not in dictionary)
        // The non-colon "?" modifier only treats "not in dictionary" as unset.
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            Assert.Throws<VariableSubstitutionException>(() =>
                ResolveModifierDockerfile(varName, "?", modValue, declareArg: false));
        }
    }

    [Fact]
    public void ModifierSemantics_Question_SucceedsWhenSetEmpty()
    {
        // ${VAR?err} — returns empty when VAR is set but empty (does NOT throw)
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            var overrides = new Dictionary<string, string> { { varName, "" } };
            string result = ResolveModifierDockerfile(varName, "?", modValue, overrides);
            Assert.Equal("LABEL key=", result);
        }
    }

    [Fact]
    public void ModifierSemantics_Question_SucceedsWhenSetNonEmpty()
    {
        // ${VAR?err} — returns value when VAR is set and non-empty
        var samples = DockerfileArbitraries.VariableModifierComponents().Sample(SampleSize, 50);
        foreach (var (varName, _, modValue) in samples)
        {
            var overrides = new Dictionary<string, string> { { varName, "realval" } };
            string result = ResolveModifierDockerfile(varName, "?", modValue, overrides);
            Assert.Equal("LABEL key=realval", result);
        }
    }

    // ──────────────────────────────────────────────
    // P0-7: Parse isolation property
    // Parsing instruction X followed by Y produces
    // the same tokens for X as parsing X alone.
    // ──────────────────────────────────────────────

    [Fact]
    public void ParseIsolation_FirstInstructionUnchanged()
    {
        // Generate pairs of instructions
        var pairGen =
            from x in DockerfileArbitraries.SingleBodyInstruction()
            from y in DockerfileArbitraries.SingleBodyInstruction()
            select (x, y);

        IReadOnlyList<(string x, string y)> samples = pairGen.Sample(SampleSize, SampleCount);
        foreach (var (instrX, instrY) in samples)
        {
            // Parse X alone (using internal CreateInstruction via InternalsVisibleTo)
            string xAlone = Instruction.CreateInstruction(instrX, Dockerfile.DefaultEscapeChar).ToString();

            // Parse a Dockerfile containing both X and Y
            string combined = $"FROM alpine\n{instrX}\n{instrY}";
            Dockerfile df = Dockerfile.Parse(combined);

            // The first non-FROM instruction should match X parsed alone
            var instructions = df.Items.OfType<Instruction>().ToList();
            // instructions[0] is FROM, instructions[1] is X, instructions[2] is Y
            Assert.True(instructions.Count >= 2,
                $"Expected at least 2 instructions in combined Dockerfile, got {instructions.Count}");

            // The instruction text includes the trailing \n when parsed as part of a Dockerfile.
            // Trim the trailing \n for comparison against the standalone parse (which has no trailing \n).
            string xInDockerfile = instructions[1].ToString().TrimEnd('\n');
            Assert.Equal(xAlone, xInDockerfile);
        }
    }

    [Fact]
    public void ParseIsolation_SecondInstructionUnchanged()
    {
        // Verify the second instruction is also unaffected by the first
        var pairGen =
            from x in DockerfileArbitraries.SingleBodyInstruction()
            from y in DockerfileArbitraries.SingleBodyInstruction()
            select (x, y);

        IReadOnlyList<(string x, string y)> samples = pairGen.Sample(SampleSize, SampleCount);
        foreach (var (instrX, instrY) in samples)
        {
            // Parse Y alone
            string yAlone = Instruction.CreateInstruction(instrY, Dockerfile.DefaultEscapeChar).ToString();

            // Parse a Dockerfile containing both X and Y
            string combined = $"FROM alpine\n{instrX}\n{instrY}";
            Dockerfile df = Dockerfile.Parse(combined);

            var instructions = df.Items.OfType<Instruction>().ToList();
            Assert.True(instructions.Count >= 3,
                $"Expected at least 3 instructions in combined Dockerfile, got {instructions.Count}");

            // Last instruction is Y — no trailing \n (it's the last line)
            string yInDockerfile = instructions[2].ToString();
            Assert.Equal(yAlone, yInDockerfile);
        }
    }
}
