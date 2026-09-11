namespace Valleysoft.DockerfileModel.DiffTest;

public sealed class FailureMinimizer
{
    private readonly Func<ILeanParser> _parserFactory;
    private readonly int _maximumEvaluations;

    public FailureMinimizer(
        Func<ILeanParser> parserFactory,
        int maximumEvaluations = 250)
    {
        _parserFactory = parserFactory;
        _maximumEvaluations = maximumEvaluations;
    }

    public async Task<DiffResult> MinimizeAsync(
        DiffResult original,
        CancellationToken cancellationToken = default)
    {
        if (original.Match || original.Outcome == DiffOutcomeKind.InfrastructureError)
        {
            return original;
        }

        await using ILeanParser parser = _parserFactory();
        DiffResult best = original;
        HashSet<string> evaluated = new(StringComparer.Ordinal)
        {
            original.Case.Input
        };
        int evaluations = 0;

        while (evaluations < _maximumEvaluations)
        {
            DiffResult? improvement = null;
            int remainingEvaluations = _maximumEvaluations - evaluations;
            foreach (string candidateInput in GetCandidates(
                best.Case.Input,
                remainingEvaluations))
            {
                if (!evaluated.Add(candidateInput))
                {
                    continue;
                }

                evaluations++;
                DiffCase candidate = best.Case with { Input = candidateInput };
                DiffResult result = await DiffTestRunner.RunSingleAsync(
                    parser,
                    candidate,
                    cancellationToken);

                if (result.Outcome == original.Outcome)
                {
                    improvement = result;
                    break;
                }

                if (evaluations >= _maximumEvaluations)
                {
                    break;
                }
            }

            if (improvement is null)
            {
                break;
            }

            best = improvement;
        }

        return best;
    }

    internal static IEnumerable<string> GetCandidates(
        string input,
        int maximumCandidates = 250)
    {
        if (maximumCandidates <= 0)
        {
            yield break;
        }

        int keywordEnd = input.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
        if (keywordEnd <= 0)
        {
            yield break;
        }

        string keyword = input[..keywordEnd];
        string arguments = input[keywordEnd..];
        HashSet<string> candidates = new(StringComparer.Ordinal);
        (bool allowsCrLf, bool allowsLf, bool allowsCr) = GetNewlineStyles(input);
        int yielded = 0;

        foreach (string candidate in new[]
        {
            keyword + arguments.TrimEnd(),
            keyword + " " + arguments.Trim(),
            keyword + " " + CollapseWhitespace(arguments.Trim())
        })
        {
            if (TryAdd(candidate))
            {
                yield return candidate;
                yielded++;
            }

            if (yielded >= maximumCandidates)
            {
                yield break;
            }
        }

        IReadOnlyList<string> lines = SplitLinesPreservingEndings(arguments);
        if (lines.Count > 1)
        {
            int lineCandidateBudget = Math.Max(1, maximumCandidates / 2);
            foreach (int index in GetSampledOffsets(
                lines.Count - 1,
                lineCandidateBudget))
            {
                string remaining = string.Concat(lines.Where((_, i) => i != index));
                if (remaining.Length > 0 && !char.IsWhiteSpace(remaining[0]))
                {
                    remaining = " " + remaining;
                }

                string candidate = keyword + remaining;
                if (TryAdd(candidate))
                {
                    yield return candidate;
                    yielded++;
                }

                if (yielded >= maximumCandidates)
                {
                    yield break;
                }
            }
        }

        string trimmedArguments = arguments.Trim();
        List<int> chunkSizes = new();
        for (int chunkSize = HighestPowerOfTwo(trimmedArguments.Length);
            chunkSize > 0;
            chunkSize /= 2)
        {
            chunkSizes.Add(chunkSize);
        }

        int chunkAttemptBudget = maximumCandidates * 4;
        int attemptsPerChunkSize = chunkSizes.Count == 0
            ? 0
            : Math.Max(1, chunkAttemptBudget / chunkSizes.Count);
        foreach (int chunkSize in chunkSizes)
        {
            int maximumStart = trimmedArguments.Length - chunkSize;
            foreach (int start in GetSampledOffsets(
                maximumStart,
                attemptsPerChunkSize))
            {
                string simplified =
                    trimmedArguments.Remove(start, chunkSize).Trim();
                string candidate = keyword + " " + simplified;
                if (TryAdd(candidate))
                {
                    yield return candidate;
                    yielded++;
                }

                if (yielded >= maximumCandidates)
                {
                    yield break;
                }
            }
        }

        bool TryAdd(string candidate)
        {
            return candidate.Length > keyword.Length &&
                candidate.Length < input.Length &&
                char.IsWhiteSpace(candidate[keyword.Length]) &&
                UsesOnlyNewlineStyles(candidate, allowsCrLf, allowsLf, allowsCr) &&
                candidates.Add(candidate);
        }
    }

    private static (bool CrLf, bool Lf, bool Cr) GetNewlineStyles(string value)
    {
        bool crLf = false;
        bool lf = false;
        bool cr = false;
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '\r')
            {
                if (index + 1 < value.Length && value[index + 1] == '\n')
                {
                    crLf = true;
                    index++;
                }
                else
                {
                    cr = true;
                }
            }
            else if (value[index] == '\n')
            {
                lf = true;
            }
        }

        return (crLf, lf, cr);
    }

    private static bool UsesOnlyNewlineStyles(
        string value,
        bool allowsCrLf,
        bool allowsLf,
        bool allowsCr)
    {
        (bool crLf, bool lf, bool cr) = GetNewlineStyles(value);
        return (!crLf || allowsCrLf) &&
            (!lf || allowsLf) &&
            (!cr || allowsCr);
    }

    private static IReadOnlyList<string> SplitLinesPreservingEndings(string value)
    {
        List<string> lines = new();
        int start = 0;
        for (int index = 0; index < value.Length; index++)
        {
            int terminatorLength = value[index] switch
            {
                '\r' when index + 1 < value.Length && value[index + 1] == '\n' => 2,
                '\r' or '\n' => 1,
                _ => 0
            };
            if (terminatorLength == 0)
            {
                continue;
            }

            int end = index + terminatorLength;
            lines.Add(value[start..end]);
            start = end;
            index = end - 1;
        }

        if (start < value.Length)
        {
            lines.Add(value[start..]);
        }

        return lines;
    }

    private static string CollapseWhitespace(string value)
    {
        Span<char> buffer = value.Length <= 512
            ? stackalloc char[value.Length]
            : new char[value.Length];
        int length = 0;
        bool inWhitespace = false;
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!inWhitespace)
                {
                    buffer[length++] = ' ';
                    inWhitespace = true;
                }
            }
            else
            {
                buffer[length++] = character;
                inWhitespace = false;
            }
        }

        return new string(buffer[..length]);
    }

    private static int HighestPowerOfTwo(int value)
    {
        int result = 1;
        while (result <= value / 2)
        {
            result *= 2;
        }

        return value == 0 ? 0 : result;
    }

    private static IEnumerable<int> GetSampledOffsets(
        int maximumInclusive,
        int maximumSamples)
    {
        int sampleCount = Math.Min(maximumSamples, maximumInclusive + 1);
        if (sampleCount <= 0)
        {
            yield break;
        }

        if (sampleCount == 1)
        {
            yield return maximumInclusive;
            yield break;
        }

        for (int sample = 0; sample < sampleCount; sample++)
        {
            yield return (int)((long)sample * maximumInclusive / (sampleCount - 1));
        }
    }
}
