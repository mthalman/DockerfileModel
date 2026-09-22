using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Valleysoft.DockerfileModel.Parsing;

/// <summary>Represents a parser. Modeled after the real Valleysoft.DockerfileModel.Parsing API so existing grammar code compiles unchanged against Superpower.</summary>
public delegate IResult<T> Parser<out T>(IInput input);

public interface IInput : IEquatable<IInput>
{
    IInput Advance();
    string Source { get; }
    char Current { get; }
    bool AtEnd { get; }
    int Position { get; }
    int Line { get; }
    int Column { get; }
    IDictionary<object, object> Memos { get; }
}

public class Input : IInput
{
    private readonly string _source;
    private readonly int _position;
    private readonly int _line;
    private readonly int _column;

    public IDictionary<object, object> Memos { get; }

    public Input(string source) : this(source, 0) { }

    internal Input(string source, int position, int line = 1, int column = 1)
    {
        _source = source;
        _position = position;
        _line = line;
        _column = column;
        Memos = new Dictionary<object, object>();
    }

    public IInput Advance()
    {
        if (AtEnd) throw new InvalidOperationException("The input is already at the end of the source.");
        return new Input(_source, _position + 1, Current == '\n' ? _line + 1 : _line, Current == '\n' ? 1 : _column + 1);
    }

    public string Source => _source;
    public char Current => _source[_position];
    public bool AtEnd => _position == _source.Length;
    public int Position => _position;
    public int Line => _line;
    public int Column => _column;

    public override string ToString() => $"Line {_line}, Column {_column}";

    public override int GetHashCode() => unchecked(((_source?.GetHashCode() ?? 0) * 397) ^ _position);

    public override bool Equals(object? obj) => Equals(obj as IInput);

    public bool Equals(IInput? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(_source, other.Source) && _position == other.Position;
    }
}

public interface IResult<out T>
{
    T Value { get; }
    bool WasSuccessful { get; }
    string Message { get; }
    IEnumerable<string> Expectations { get; }
    IInput Remainder { get; }
}

public static class Result
{
    public static IResult<T> Success<T>(T value, IInput remainder) => new Result<T>(value, remainder);

    public static IResult<T> Failure<T>(IInput remainder, string message, IEnumerable<string> expectations) =>
        new Result<T>(remainder, message, expectations);
}

internal class Result<T> : IResult<T>
{
    private readonly T _value;
    private readonly IInput _remainder;
    private readonly bool _wasSuccessful;
    private readonly string _message;
    private readonly IEnumerable<string> _expectations;

    public Result(T value, IInput remainder)
    {
        _value = value;
        _remainder = remainder;
        _wasSuccessful = true;
        _message = "";
        _expectations = Enumerable.Empty<string>();
    }

    public Result(IInput remainder, string message, IEnumerable<string> expectations)
    {
        _value = default!;
        _remainder = remainder;
        _wasSuccessful = false;
        _message = message;
        _expectations = expectations;
    }

    public T Value => WasSuccessful ? _value : throw new InvalidOperationException("No value can be computed.");
    public bool WasSuccessful => _wasSuccessful;
    public string Message => _message;
    public IEnumerable<string> Expectations => _expectations;
    public IInput Remainder => _remainder;

    public override string ToString()
    {
        if (WasSuccessful) return $"Successful parsing of {Value}.";

        string expMsg = "";
        if (Expectations.Any())
            expMsg = " expected " + Expectations.Aggregate((e1, e2) => e1 + " or " + e2);

        string recentlyConsumed = CalculateRecentlyConsumed();
        return $"Parsing failure: {Message};{expMsg} ({Remainder}); recently consumed: {recentlyConsumed}";
    }

    private string CalculateRecentlyConsumed()
    {
        const int windowSize = 10;
        int totalConsumedChars = Remainder.Position;
        int windowStart = totalConsumedChars - windowSize;
        windowStart = windowStart < 0 ? 0 : windowStart;
        int numberOfRecentlyConsumedChars = totalConsumedChars - windowStart;
        return Remainder.Source.Substring(windowStart, numberOfRecentlyConsumedChars);
    }
}

internal static class ResultHelper
{
    public static IResult<U> IfSuccess<T, U>(this IResult<T> result, Func<IResult<T>, IResult<U>> next) =>
        result.WasSuccessful ? next(result) : Result.Failure<U>(result.Remainder, result.Message, result.Expectations);

    public static IResult<T> IfFailure<T>(this IResult<T> result, Func<IResult<T>, IResult<T>> next) =>
        result.WasSuccessful ? result : next(result);
}

internal static class StringExtensions
{
    public static IEnumerable<char> ToEnumerable(this string @this)
    {
        for (int i = 0; i < @this.Length; ++i)
        {
            yield return @this[i];
        }
    }

    public static string Join<T>(string separator, IEnumerable<T> values) =>
        string.Join(separator, values.Select(v => v?.ToString()).ToArray());
}

public class Position : IEquatable<Position>
{
    public Position(int pos, int line, int column)
    {
        Pos = pos;
        Line = line;
        Column = column;
    }

    public static Position FromInput(IInput input) => new(input.Position, input.Line, input.Column);

    public int Pos { get; }
    public int Line { get; }
    public int Column { get; }

    public override bool Equals(object? obj) => Equals(obj as Position);

    public bool Equals(Position? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Pos == other.Pos && Line == other.Line && Column == other.Column;
    }

    public static bool operator ==(Position? left, Position? right) => Equals(left, right);
    public static bool operator !=(Position? left, Position? right) => !Equals(left, right);

    public override int GetHashCode()
    {
        int h = 31;
        h = h * 13 + Pos;
        h = h * 13 + Line;
        h = h * 13 + Column;
        return h;
    }

    public override string ToString() => $"Line {Line}, Column {Column}";
}

public class ParseException : Exception
{
    public ParseException() => Position = new Position(0, 1, 1);

    public ParseException(string message) : base(message) => Position = new Position(0, 1, 1);

    public ParseException(string message, Position? position) : base(message) => Position = position ?? new Position(0, 1, 1);

    public ParseException(string message, Exception innerException) : base(message, innerException) => Position = new Position(0, 1, 1);

    public Position Position { get; }
}

public interface IOption<out T>
{
    bool IsEmpty { get; }
    bool IsDefined { get; }
    T GetOrDefault();
    T Get();
}

public static class OptionExtensions
{
    public static T GetOrElse<T>(this IOption<T> option, T defaultValue) => option.IsEmpty ? defaultValue : option.Get();

    public static IOption<U> Select<T, U>(this IOption<T> option, Func<T, U> map) =>
        option.IsDefined ? new Some<U>(map(option.Get())) : new None<U>();

    public static IOption<V> SelectMany<T, U, V>(this IOption<T> option, Func<T, IOption<U>> bind, Func<T, U, V> project)
    {
        if (option.IsEmpty) return new None<V>();
        T t = option.Get();
        return bind(t).Select(u => project(t, u));
    }

    public static IOption<U> SelectMany<T, U>(this IOption<T> option, Func<T, IOption<U>> bind) =>
        option.SelectMany(bind, (_, x) => x);
}

internal abstract class AbstractOption<T> : IOption<T>
{
    public abstract bool IsEmpty { get; }
    public bool IsDefined => !IsEmpty;
    public T GetOrDefault() => IsEmpty ? default! : Get();
    public abstract T Get();
}

internal sealed class Some<T> : AbstractOption<T>
{
    private readonly T _value;
    public Some(T value) => _value = value;
    public override bool IsEmpty => false;
    public override T Get() => _value;
}

internal sealed class None<T> : AbstractOption<T>
{
    public override bool IsEmpty => true;
    public override T Get() => throw new InvalidOperationException("Option is empty.");
}

public static partial class Parse
{
    public const string LeftRecursionErrorMessage = "Left recursion in the grammar.";

    public static Parser<char> Char(Func<char, bool> predicate, string description)
    {
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));
        if (description is null) throw new ArgumentNullException(nameof(description));

        return i =>
        {
            if (i.AtEnd)
                return Result.Failure<char>(i, "Unexpected end of input reached", new[] { description });

            if (!predicate(i.Current))
                return Result.Failure<char>(i, $"unexpected '{i.Current}'", new[] { description });

            Superpower.Model.Result<char> result = SuperpowerBackend.Match(
                SuperpowerBackend.Matching(predicate, description),
                i);

            if (result.HasValue)
            {
                return Result.Success(result.Value, i.Advance());
            }

            return Result.Failure<char>(i, result.ErrorMessage ?? string.Empty, new[] { description });
        };
    }

    public static Parser<char> CharExcept(Func<char, bool> predicate, string description) =>
        Char(c => !predicate(c), "any character except " + description);

    public static Parser<char> Char(char c) => Char(ch => c == ch, char.ToString(c));

    public static Parser<char> Chars(params char[] c) => Char(c.Contains, StringExtensions.Join("|", c));

    public static Parser<char> Chars(string c) => Char(c.ToEnumerable().Contains, StringExtensions.Join("|", c.ToEnumerable()));

    public static Parser<char> CharExcept(char c) => CharExcept(ch => c == ch, char.ToString(c));

    public static Parser<char> CharExcept(IEnumerable<char> c)
    {
        char[] chars = c as char[] ?? c.ToArray();
        return CharExcept(chars.Contains, StringExtensions.Join("|", chars));
    }

    public static Parser<char> CharExcept(string c) =>
        CharExcept(c.ToEnumerable().Contains, StringExtensions.Join("|", c.ToEnumerable()));

    public static Parser<char> IgnoreCase(char c) => Char(ch => char.ToUpperInvariant(c) == char.ToUpperInvariant(ch), char.ToString(c));

    public static Parser<IEnumerable<char>> IgnoreCase(string s)
    {
        if (s is null) throw new ArgumentNullException(nameof(s));

        return s
            .ToEnumerable()
            .Select(IgnoreCase)
            .Aggregate(Return(Enumerable.Empty<char>()), (a, p) => a.Concat(p.Once()))
            .Named(s);
    }

    public static readonly Parser<char> AnyChar = Char(_ => true, "any character");
    public static readonly Parser<char> WhiteSpace = Char(char.IsWhiteSpace, "whitespace");
    public static readonly Parser<char> Digit = Char(char.IsDigit, "digit");
    public static readonly Parser<char> Letter = Char(char.IsLetter, "letter");
    public static readonly Parser<char> LetterOrDigit = Char(char.IsLetterOrDigit, "letter or digit");
    public static readonly Parser<char> Lower = Char(char.IsLower, "lowercase letter");
    public static readonly Parser<char> Upper = Char(char.IsUpper, "uppercase letter");
    public static readonly Parser<char> Numeric = Char(char.IsNumber, "numeric character");

    /// <summary>Matches a single carriage-return, line-terminator character. Not part of Valleysoft.DockerfileModel.Parsing's real API; added for line-ending detection.</summary>
    public static readonly Parser<char> LineTerminator = Char(c => c is '\r' or '\n', "line terminator");

    /// <summary>
    /// Matches a full line ending ("\r\n" or "\n") and yields the exact matched text, so callers can
    /// round-trip the original newline style. Not part of Valleysoft.DockerfileModel.Parsing's real API.
    /// </summary>
    public static readonly Parser<string> LineEnd =
        (from cr in Char('\r') from lf in Char('\n') select "\r\n").Or(Char('\n').Select(c => c.ToString()));

    public static Parser<IEnumerable<char>> String(string s)
    {
        if (s is null) throw new ArgumentNullException(nameof(s));

        return s
            .ToEnumerable()
            .Select(Char)
            .Aggregate(Return(Enumerable.Empty<char>()), (a, p) => a.Concat(p.Once()))
            .Named(s);
    }

    public static Parser<object> Not<T>(this Parser<T> parser)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));

        return i =>
        {
            IResult<T> result = parser(i);
            if (result.WasSuccessful)
            {
                string msg = $"`{StringExtensions.Join(", ", result.Expectations)}' was not expected";
                return Result.Failure<object>(i, msg, Array.Empty<string>());
            }
            return Result.Success<object>(null!, i);
        };
    }

    public static Parser<U> Then<T, U>(this Parser<T> first, Func<T, Parser<U>> second)
    {
        if (first is null) throw new ArgumentNullException(nameof(first));
        if (second is null) throw new ArgumentNullException(nameof(second));

        return i => first(i).IfSuccess(s => second(s.Value)(s.Remainder));
    }

    public static Parser<IEnumerable<T>> Many<T>(this Parser<T> parser)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));

        return i =>
        {
            IInput remainder = i;
            List<T> result = new();
            IResult<T> r = parser(i);

            while (r.WasSuccessful)
            {
                if (remainder.Equals(r.Remainder)) break;

                result.Add(r.Value);
                remainder = r.Remainder;
                r = parser(remainder);
            }

            return Result.Success<IEnumerable<T>>(result, remainder);
        };
    }

    public static Parser<IEnumerable<T>> XMany<T>(this Parser<T> parser)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));
        return parser.Many().Then(m => parser.Once().XOr(Return(m)));
    }

    public static Parser<IEnumerable<T>> AtLeastOnce<T>(this Parser<T> parser)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));
        return parser.Once().Then(t1 => parser.Many().Select(ts => t1.Concat(ts)));
    }

    public static Parser<IEnumerable<T>> XAtLeastOnce<T>(this Parser<T> parser)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));
        return parser.Once().Then(t1 => parser.XMany().Select(ts => t1.Concat(ts)));
    }

    public static Parser<T> End<T>(this Parser<T> parser)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));

        return i => parser(i).IfSuccess(s =>
            s.Remainder.AtEnd
                ? s
                : Result.Failure<T>(s.Remainder, $"unexpected '{s.Remainder.Current}'", new[] { "end of input" }));
    }

    public static Parser<U> Select<T, U>(this Parser<T> parser, Func<T, U> convert)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));
        if (convert is null) throw new ArgumentNullException(nameof(convert));
        return parser.Then(t => Return(convert(t)));
    }

    public static Parser<T> Token<T>(this Parser<T> parser)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));

        return from leading in WhiteSpace.Many()
               from item in parser
               from trailing in WhiteSpace.Many()
               select item;
    }

    public static Parser<T> Ref<T>(Func<Parser<T>> reference)
    {
        if (reference is null) throw new ArgumentNullException(nameof(reference));

        Parser<T>? p = null;

        return i =>
        {
            p ??= reference();

            if (i.Memos.ContainsKey(p))
            {
                IResult<T> pResult = (IResult<T>)i.Memos[p];
                if (pResult.WasSuccessful) return pResult;

                if (!pResult.WasSuccessful && pResult.Message == LeftRecursionErrorMessage)
                    throw new ParseException(pResult.ToString() ?? string.Empty);
            }

            i.Memos[p] = Result.Failure<T>(i, LeftRecursionErrorMessage, Array.Empty<string>());
            IResult<T> result = p(i);
            i.Memos[p] = result;
            return result;
        };
    }

    public static Parser<string> Text(this Parser<IEnumerable<char>> characters) =>
        characters.Select(chs => new string(chs.ToArray()));

    public static Parser<T> Or<T>(this Parser<T> first, Parser<T> second)
    {
        if (first is null) throw new ArgumentNullException(nameof(first));
        if (second is null) throw new ArgumentNullException(nameof(second));

        return i =>
        {
            IResult<T> fr = first(i);
            if (!fr.WasSuccessful)
            {
                return second(i).IfFailure(sf => DetermineBestError(fr, sf));
            }

            if (fr.Remainder.Equals(i))
                return second(i).IfFailure(_ => fr);

            return fr;
        };
    }

    public static Parser<T> Named<T>(this Parser<T> parser, string name)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));
        if (name is null) throw new ArgumentNullException(nameof(name));

        return i => parser(i).IfFailure(f => f.Remainder.Equals(i) ?
            Result.Failure<T>(f.Remainder, f.Message, new[] { name }) :
            f);
    }

    public static Parser<T> XOr<T>(this Parser<T> first, Parser<T> second)
    {
        if (first is null) throw new ArgumentNullException(nameof(first));
        if (second is null) throw new ArgumentNullException(nameof(second));

        return i =>
        {
            IResult<T> fr = first(i);
            if (!fr.WasSuccessful)
            {
                if (!fr.Remainder.Equals(i)) return fr;
                return second(i).IfFailure(sf => DetermineBestError(fr, sf));
            }

            if (fr.Remainder.Equals(i))
                return second(i).IfFailure(_ => fr);

            return fr;
        };
    }

    private static IResult<T> DetermineBestError<T>(IResult<T> firstFailure, IResult<T> secondFailure)
    {
        if (secondFailure.Remainder.Position > firstFailure.Remainder.Position)
            return secondFailure;

        if (secondFailure.Remainder.Position == firstFailure.Remainder.Position)
            return Result.Failure<T>(firstFailure.Remainder, firstFailure.Message,
                firstFailure.Expectations.Union(secondFailure.Expectations));

        return firstFailure;
    }

    public static Parser<IEnumerable<T>> Once<T>(this Parser<T> parser)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));
        return parser.Select(r => (IEnumerable<T>)new[] { r });
    }

    public static Parser<IEnumerable<T>> Concat<T>(this Parser<IEnumerable<T>> first, Parser<IEnumerable<T>> second)
    {
        if (first is null) throw new ArgumentNullException(nameof(first));
        if (second is null) throw new ArgumentNullException(nameof(second));
        return first.Then(f => second.Select(f.Concat));
    }

    public static Parser<T> Return<T>(T value) => i => Result.Success(value, i);

    public static Parser<U> Return<T, U>(this Parser<T> parser, U value)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));
        return parser.Select(_ => value);
    }

    public static Parser<T> Except<T, U>(this Parser<T> parser, Parser<U> except)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));
        if (except is null) throw new ArgumentNullException(nameof(except));

        return i =>
        {
            IResult<U> r = except(i);
            if (r.WasSuccessful)
                return Result.Failure<T>(i, "Excepted parser succeeded.", new[] { "other than the excepted input" });
            return parser(i);
        };
    }

    public static Parser<IEnumerable<T>> Until<T, U>(this Parser<T> parser, Parser<U> until) =>
        parser.Except(until).Many().Then(until.Return);

    public static Parser<T> Where<T>(this Parser<T> parser, Func<T, bool> predicate)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));

        return i => parser(i).IfSuccess(s =>
            predicate(s.Value) ? s : Result.Failure<T>(i, $"Unexpected {s.Value}.", Array.Empty<string>()));
    }

    public static Parser<V> SelectMany<T, U, V>(this Parser<T> parser, Func<T, Parser<U>> selector, Func<T, U, V> projector)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));
        if (selector is null) throw new ArgumentNullException(nameof(selector));
        if (projector is null) throw new ArgumentNullException(nameof(projector));

        return parser.Then(t => selector(t).Select(u => projector(t, u)));
    }

    /// <summary>
    /// Parses a sequence delimited by <paramref name="first"/> and <paramref name="rest"/> character classes.
    /// Not part of Valleysoft.DockerfileModel.Parsing's real API; added to support keyword/identifier-style grammars used throughout this library.
    /// </summary>
    public static Parser<string> Identifier(Parser<char> first, Parser<char> rest) =>
        from leading in first
        from trailing in rest.Many()
        select leading + string.Concat(trailing);

    public static readonly Parser<string> Number = Numeric.AtLeastOnce().Text();

    private static Parser<string> DecimalWithoutLeadingDigits(CultureInfo? ci = null) =>
        from nothing in Return("")
        from dot in String((ci ?? CultureInfo.CurrentCulture).NumberFormat.NumberDecimalSeparator).Text()
        from fraction in Number
        select dot + fraction;

    private static Parser<string> DecimalWithLeadingDigits(CultureInfo? ci = null) =>
        Number.Then(n => DecimalWithoutLeadingDigits(ci).XOr(Return("")).Select(f => n + f));

    public static readonly Parser<string> Decimal = DecimalWithLeadingDigits().XOr(DecimalWithoutLeadingDigits());

    public static readonly Parser<string> DecimalInvariant = DecimalWithLeadingDigits(CultureInfo.InvariantCulture)
        .XOr(DecimalWithoutLeadingDigits(CultureInfo.InvariantCulture));
}

public static partial class Parse
{
    public static Parser<IOption<T>> Optional<T>(this Parser<T> parser)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));

        return i =>
        {
            IResult<T> pr = parser(i);
            return pr.WasSuccessful
                ? Result.Success<IOption<T>>(new Some<T>(pr.Value), pr.Remainder)
                : Result.Success<IOption<T>>(new None<T>(), i);
        };
    }

    public static Parser<IOption<T>> XOptional<T>(this Parser<T> parser)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));

        return i =>
        {
            IResult<T> result = parser(i);

            if (result.WasSuccessful)
                return Result.Success<IOption<T>>(new Some<T>(result.Value), result.Remainder);

            if (result.Remainder.Equals(i))
                return Result.Success<IOption<T>>(new None<T>(), i);

            return Result.Failure<IOption<T>>(result.Remainder, result.Message, result.Expectations);
        };
    }

    /// <summary>
    /// Constructs a non-consuming lookahead version of the given parser: it never fails and never
    /// advances the input, regardless of whether the wrapped parser matched.
    /// </summary>
    public static Parser<IOption<T>> Preview<T>(this Parser<T> parser)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));

        return i =>
        {
            IResult<T> result = parser(i);
            return result.WasSuccessful
                ? Result.Success<IOption<T>>(new Some<T>(result.Value), i)
                : Result.Success<IOption<T>>(new None<T>(), i);
        };
    }
}

public static partial class Parse
{
    public static Parser<IEnumerable<T>> DelimitedBy<T, U>(this Parser<T> parser, Parser<U> delimiter) =>
        DelimitedBy(parser, delimiter, null, null);

    public static Parser<IEnumerable<T>> DelimitedBy<T, U>(this Parser<T> parser, Parser<U> delimiter, int? minimumCount, int? maximumCount)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));
        if (delimiter is null) throw new ArgumentNullException(nameof(delimiter));

        return from head in parser.Once()
               from tail in
                   (from separator in delimiter
                    from item in parser
                    select item).Repeat(minimumCount - 1, maximumCount - 1)
               select head.Concat(tail);
    }

    public static Parser<IEnumerable<T>> XDelimitedBy<T, U>(this Parser<T> itemParser, Parser<U> delimiter)
    {
        if (itemParser is null) throw new ArgumentNullException(nameof(itemParser));
        if (delimiter is null) throw new ArgumentNullException(nameof(delimiter));

        return from head in itemParser.Once()
               from tail in
                   (from separator in delimiter
                    from item in itemParser
                    select item).XMany()
               select head.Concat(tail);
    }

    public static Parser<IEnumerable<T>> Repeat<T>(this Parser<T> parser, int count) => Repeat(parser, count, count);

    public static Parser<IEnumerable<T>> Repeat<T>(this Parser<T> parser, int? minimumCount, int? maximumCount)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));

        return i =>
        {
            IInput remainder = i;
            List<T> result = new();
            int count = 0;

            IResult<T> r = parser(remainder);
            while (r.WasSuccessful && (maximumCount is null || count < maximumCount.Value))
            {
                count++;
                result.Add(r.Value);
                remainder = r.Remainder;
                r = parser(remainder);
            }

            if (minimumCount.HasValue && count < minimumCount.Value)
            {
                string what = r.Remainder.AtEnd ? "end of input" : r.Remainder.Current.ToString();
                string msg = $"Unexpected '{what}'";
                string exp;
                if (minimumCount == maximumCount)
                    exp = $"'{StringExtensions.Join(", ", r.Expectations)}' {minimumCount.Value} times, but found {count}";
                else if (maximumCount is null)
                    exp = $"'{StringExtensions.Join(", ", r.Expectations)}' minimum {minimumCount.Value} times, but found {count}";
                else
                    exp = $"'{StringExtensions.Join(", ", r.Expectations)}' between {minimumCount.Value} and {maximumCount.Value} times, but found {count}";

                return Result.Failure<IEnumerable<T>>(i, $"{msg}; expected {exp}", r.Expectations);
            }

            return Result.Success<IEnumerable<T>>(result, remainder);
        };
    }
}

public static class ParserExtensions
{
    public static IResult<T> TryParse<T>(this Parser<T> parser, string input)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));
        if (input is null) throw new ArgumentNullException(nameof(input));
        return parser(new Input(input));
    }

    public static IResult<T> TryParse<T>(this Parser<T> parser, IInput input)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));
        if (input is null) throw new ArgumentNullException(nameof(input));
        return parser(input);
    }

    public static T Parse<T>(this Parser<T> parser, string input)
    {
        if (parser is null) throw new ArgumentNullException(nameof(parser));
        if (input is null) throw new ArgumentNullException(nameof(input));

        IResult<T> result = parser.TryParse(input);
        if (result.WasSuccessful) return result.Value;

        throw new ParseException(result.ToString() ?? string.Empty, Position.FromInput(result.Remainder));
    }
}
