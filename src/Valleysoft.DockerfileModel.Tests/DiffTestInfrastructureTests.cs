using Valleysoft.DockerfileModel.DiffTest;

namespace Valleysoft.DockerfileModel.Tests;

public class DiffTestInfrastructureTests
{
    [Fact]
    public void Generate_IsDeterministicAndCarriesReplayMetadata()
    {
        IReadOnlyList<DiffCase> first = InputGenerator.Generate(100, seed: 1234);
        IReadOnlyList<DiffCase> second = InputGenerator.Generate(100, seed: 1234);

        Assert.Equal(first, second);
        Assert.All(first, testCase =>
        {
            Assert.Equal(DiffCaseSource.Generated, testCase.Source);
            Assert.NotNull(testCase.Generator);
            Assert.Equal(1234, testCase.Seed);
            Assert.NotNull(testCase.CaseIndex);
        });
    }

    [Fact]
    public void GeneratorGamma_IsOddAndUniquePerCase()
    {
        ulong[] values = Enumerable.Range(0, 1000)
            .Select(caseIndex => InputGenerator.CreateGamma(7, caseIndex))
            .ToArray();

        Assert.All(values, value => Assert.Equal(1UL, value & 1UL));
        Assert.Equal(values.Length, values.Distinct().Count());
    }

    [Fact]
    public void ReplayCommand_ResolvesBothPathsAgainstCurrentDirectory()
    {
        string currentDirectory = Environment.CurrentDirectory;
        string projectPath = Path.Combine(
            currentDirectory,
            "src",
            "Valleysoft.DockerfileModel.DiffTest",
            "Valleysoft.DockerfileModel.DiffTest.csproj");
        string leanCliPath = Path.Combine("..", "lean", "DockerfileModelDiffTest");

        string command = ReplayCommand.Create(
            Case("replay", "FROM alpine"),
            leanCliPath,
            projectPath);

        Assert.Contains(
            $"--project \"{Path.GetFullPath(projectPath)}\"",
            command,
            StringComparison.Ordinal);
        Assert.Contains(
            $"--lean-cli \"{Path.GetFullPath(leanCliPath)}\"",
            command,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayCommand_DiscoversProjectFromCiWorkingDirectory()
    {
        string corpusDirectory = RegressionCorpus.ResolveDefaultDirectory();
        string projectDirectory = Directory.GetParent(corpusDirectory)!.FullName;
        string srcDirectory = Directory.GetParent(projectDirectory)!.FullName;

        string? projectPath = ReplayCommand.ResolveProjectPath(new[] { srcDirectory });

        Assert.Equal(
            Path.Combine(
                projectDirectory,
                "Valleysoft.DockerfileModel.DiffTest.csproj"),
            projectPath);
    }

    [Fact]
    public void ReplayCommand_FallsBackToCompiledAssemblyWhenProjectIsUnavailable()
    {
        string missingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"DockerfileModel-Missing-{Guid.NewGuid():N}");

        string command = ReplayCommand.Create(
            Case("replay", "FROM alpine"),
            "DockerfileModelDiffTest",
            projectSearchPaths: new[] { missingDirectory });

        Assert.StartsWith(
            $"dotnet \"{typeof(DiffTestRunner).Assembly.Location}\" --replay",
            command,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunBatchAsync_BoundsConcurrencyAndPreservesOrder()
    {
        SharedParserState state = new();
        DiffTestRunner runner = new(() => new FakeParser(state, delay: TimeSpan.FromMilliseconds(20)));
        DiffCase[] cases = Enumerable.Range(0, 12)
            .Select(index => Case($"case-{index}", $"FROM alpine:{index}"))
            .ToArray();

        IReadOnlyList<DiffResult> results = await runner.RunBatchAsync(cases, workerCount: 3);

        Assert.Equal(cases.Select(testCase => testCase.Id), results.Select(result => result.Case.Id));
        Assert.Equal(3, state.Created);
        Assert.InRange(state.MaximumConcurrency, 2, 3);
    }

    [Fact]
    public async Task FailureMinimizer_PreservesMismatchCategory()
    {
        SharedParserState state = new();
        FailureMinimizer minimizer = new(() => new FakeParser(state));
        DiffCase testCase = Case("failure", "RUN prefix bad suffix") with
        {
            InstructionType = "RUN"
        };
        DiffResult original = await new DiffTestRunner(() => new FakeParser(state))
            .RunSingleAsync(testCase);

        DiffResult minimized = await minimizer.MinimizeAsync(original);

        Assert.Equal(DiffOutcomeKind.JsonMismatch, original.Outcome);
        Assert.Equal(original.Outcome, minimized.Outcome);
        Assert.Contains("bad", minimized.Case.Input, StringComparison.Ordinal);
        Assert.True(minimized.Case.Input.Length < original.Case.Input.Length);
    }

    [Fact]
    public async Task RunSingleAsync_BothParsersRejectInput_ReturnsMatch()
    {
        DiffCase testCase = Case("invalid", "FROM ");
        DiffTestRunner runner = new(() => new RejectingParser());

        DiffResult result = await runner.RunSingleAsync(testCase);

        Assert.Equal(DiffOutcomeKind.Match, result.Outcome);
    }

    [Fact]
    public async Task RunSingleAsync_UnsupportedInstructionType_ReturnsParserCrash()
    {
        DiffCase testCase = Case("invalid-type", "BOGUS value") with
        {
            InstructionType = "BOGUS"
        };
        DiffTestRunner runner = new(() => new RejectingParser());

        DiffResult result = await runner.RunSingleAsync(testCase);

        Assert.Equal(DiffOutcomeKind.CSharpParserCrash, result.Outcome);
        Assert.Contains("ArgumentException", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunSingleAsync_NestedVolumeBrackets_AreNotTreatedAsKnownCrash()
    {
        const string input = "VOLUME [[]]";
        DiffCase testCase = Case("nested-volume", input) with
        {
            InstructionType = "VOLUME"
        };
        DiffTestRunner runner = new(() => new SelectiveParser(input));

        DiffResult result = await runner.RunSingleAsync(testCase);

        Assert.Equal(DiffOutcomeKind.CSharpParseError, result.Outcome);
    }

    [Fact]
    public async Task RunBatchAsync_CancellationStopsTheBatch()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        DiffTestRunner runner = new(() => new CancelingParser());
        DiffCase[] cases = Enumerable.Range(0, 3)
            .Select(index => Case($"case-{index}", "FROM alpine"))
            .ToArray();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunBatchAsync(cases, workerCount: 1, cancellation.Token));
    }

    [Fact]
    public async Task LeanProcessWorker_StartupFailureReportsUnderlyingCause()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-lean-{Guid.NewGuid():N}.exe");
        await using LeanProcessWorker worker = new(missingPath);

        LeanInfrastructureException exception =
            await Assert.ThrowsAsync<LeanInfrastructureException>(
                () => worker.ParseAsync(
                    "FROM alpine",
                    '\\',
                    CancellationToken.None));

        Assert.Contains("Failed to start Lean CLI", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void LeanProcessWorker_RequestFailureReportsUnderlyingCause()
    {
        TimeoutException timeout = new("The request exceeded 30 seconds.");

        LeanInfrastructureException exception =
            LeanProcessWorker.CreateRequestFailure(37, timeout);

        Assert.Equal(
            "Lean batch request 37 failed: TimeoutException: " +
            "The request exceeded 30 seconds.",
            exception.Message);
        Assert.Same(timeout, exception.InnerException);
    }

    [Fact]
    public void LeanProcessWorker_ExitedFailureReportsExitCodeAndStderr()
    {
        LeanInfrastructureException exception =
            LeanProcessWorker.CreateExitedException(
                "Lean batch process closed stdout.",
                exitCode: -1073741511,
                stderr: "loader failure");

        Assert.Equal(
            "Lean batch process closed stdout. Exit code: -1073741511. loader failure",
            exception.Message);
    }

    [Fact]
    public async Task FailureMinimizer_CSharpErrorRequiresLeanToAcceptCandidate()
    {
        const string input = "FROM --platform= alpine:latest AS build stage";
        DiffCase testCase = Case("csharp-error", input);
        DiffTestRunner runner = new(() => new SelectiveParser(input));
        DiffResult original = await runner.RunSingleAsync(testCase);
        FailureMinimizer minimizer = new(() => new SelectiveParser(input));

        DiffResult minimized = await minimizer.MinimizeAsync(original);

        Assert.Equal(DiffOutcomeKind.CSharpParseError, original.Outcome);
        Assert.Equal(original.Outcome, minimized.Outcome);
        Assert.Equal(input, minimized.Case.Input);
        Assert.NotEqual("FROM ", minimized.Case.Input);
    }

    [Fact]
    public void FailureMinimizer_LineCandidatesPreserveInstructionAndLineEndings()
    {
        string[] candidates = FailureMinimizer
            .GetCandidates("RUN first\r\nbad\r\nthird")
            .ToArray();

        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate =>
        {
            Assert.StartsWith("RUN", candidate, StringComparison.Ordinal);
            Assert.True(char.IsWhiteSpace(candidate[3]));
            string withoutCrLf = candidate.Replace("\r\n", "");
            Assert.DoesNotContain('\r', withoutCrLf);
            Assert.DoesNotContain('\n', withoutCrLf);
        });
        Assert.DoesNotContain("RUNbad\r\nthird", candidates);
    }

    [Fact]
    public void FailureMinimizer_LargeInputHonorsCandidateBudget()
    {
        string input = "RUN " + new string('a', 100_000);

        string[] candidates = FailureMinimizer
            .GetCandidates(input, maximumCandidates: 10)
            .ToArray();

        Assert.NotEmpty(candidates);
        Assert.True(candidates.Length <= 10);
        Assert.All(candidates, candidate => Assert.True(candidate.Length < input.Length));
    }

    [Fact]
    public void FailureMinimizer_RepetitiveInputSamplesBeyondDuplicatePrefixCandidates()
    {
        string input =
            "RUN " + new string('a', 2_000) + "bad" + new string('a', 100_000);

        string[] candidates = FailureMinimizer
            .GetCandidates(input, maximumCandidates: 10)
            .ToArray();

        Assert.Contains(
            candidates,
            candidate =>
                candidate.Contains("bad", StringComparison.Ordinal) &&
                candidate.Length < input.Length);
    }

    [Fact]
    public void RegressionCorpus_PromotionIsIdempotentAndLoadIsOrdered()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"DockerfileModel-DiffTest-{Guid.NewGuid():N}");

        try
        {
            RegressionCorpus corpus = new(directory);
            DiffCase from = Case("generated-from", "FROM alpine");
            DiffCase run = Case("generated-run", "RUN echo hello") with
            {
                InstructionType = "RUN"
            };

            string firstPath = corpus.Promote(run);
            string secondPath = corpus.Promote(from);
            Assert.Equal(firstPath, corpus.Promote(run));

            IReadOnlyList<DiffCase> loaded = corpus.Load();
            Assert.Equal(2, loaded.Count);
            Assert.Equal(
                new[] { (Path: firstPath, Input: run.Input), (Path: secondPath, Input: from.Input) }
                    .OrderBy(item => item.Path, StringComparer.Ordinal)
                    .Select(item => item.Input),
                loaded.Select(testCase => testCase.Input));
            Assert.All(loaded, testCase =>
            {
                Assert.Equal(DiffCaseSource.Regression, testCase.Source);
                Assert.Null(testCase.Generator);
                Assert.Null(testCase.Seed);
                Assert.Null(testCase.CaseIndex);
            });
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void RegressionCorpus_MissingRequiredFieldReportsFixturePath()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"DockerfileModel-DiffTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "invalid.json");
        File.WriteAllText(path, """{"id":"invalid","instructionType":"FROM"}""");

        try
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => new RegressionCorpus(directory).Load());

            Assert.Contains(path, exception.Message, StringComparison.Ordinal);
            Assert.Contains("inputBase64", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RegressionCorpus_RejectsUnsupportedInstructionType()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"DockerfileModel-DiffTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "invalid.json");
        File.WriteAllText(
            path,
            """
            {
              "id": "invalid",
              "instructionType": "..\\escaped",
              "inputBase64": "Qk9HVVMgdmFsdWU=",
              "escapeCharacter": "\\"
            }
            """);

        try
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => new RegressionCorpus(directory).Load());

            Assert.Contains("unsupported instructionType", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RegressionCorpus_PromotionRejectsUnsupportedInstructionType()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"DockerfileModel-DiffTest-{Guid.NewGuid():N}");
        RegressionCorpus corpus = new(directory);
        DiffCase testCase = Case("invalid", "BOGUS value") with
        {
            InstructionType = "..\\escaped"
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => corpus.Promote(testCase));

        Assert.Contains("unsupported instruction type", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(directory));
    }

    private static DiffCase Case(string id, string input) =>
        new(id, DiffCaseSource.Generated, "FROM", input, '\\', "test", 42, 0);

    private sealed class SharedParserState
    {
        private int _active;
        private int _created;
        private int _maximumConcurrency;

        public int Created => _created;

        public int MaximumConcurrency => _maximumConcurrency;

        public void CreatedParser() => Interlocked.Increment(ref _created);

        public void Enter()
        {
            int active = Interlocked.Increment(ref _active);
            int maximum;
            while (active > (maximum = _maximumConcurrency))
            {
                if (Interlocked.CompareExchange(
                    ref _maximumConcurrency,
                    active,
                    maximum) == maximum)
                {
                    break;
                }
            }
        }

        public void Exit() => Interlocked.Decrement(ref _active);
    }

    private sealed class FakeParser : ILeanParser
    {
        private readonly SharedParserState _state;
        private readonly TimeSpan _delay;

        public FakeParser(SharedParserState state, TimeSpan? delay = null)
        {
            _state = state;
            _delay = delay ?? TimeSpan.Zero;
            _state.CreatedParser();
        }

        public async Task<string> ParseAsync(
            string input,
            char escapeChar,
            CancellationToken cancellationToken)
        {
            _state.Enter();
            try
            {
                await Task.Delay(_delay, cancellationToken);
                if (input.Contains("bad", StringComparison.Ordinal))
                {
                    return "{}";
                }

                string instruction = input.TrimStart().Split(' ', '\t', '\r', '\n')[0];
                return DiffTestRunner.ParseCSharp(instruction, input, escapeChar);
            }
            finally
            {
                _state.Exit();
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RejectingParser : ILeanParser
    {
        public Task<string> ParseAsync(
            string input,
            char escapeChar,
            CancellationToken cancellationToken) =>
            throw new LeanParseException("Rejected by Lean.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CancelingParser : ILeanParser
    {
        public Task<string> ParseAsync(
            string input,
            char escapeChar,
            CancellationToken cancellationToken) =>
            Task.FromCanceled<string>(cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SelectiveParser : ILeanParser
    {
        private readonly string _acceptedInput;

        public SelectiveParser(string acceptedInput)
        {
            _acceptedInput = acceptedInput;
        }

        public Task<string> ParseAsync(
            string input,
            char escapeChar,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(input, _acceptedInput, StringComparison.Ordinal))
            {
                throw new LeanParseException("Rejected by Lean.");
            }

            return Task.FromResult("{}");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
