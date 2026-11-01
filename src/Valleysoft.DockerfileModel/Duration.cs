using System.Globalization;
using System.Text;

namespace Valleysoft.DockerfileModel;
 
public class Duration
{
    public Duration(TimeSpan timeSpan)
    {
        TimeSpan = timeSpan;
    }

    public TimeSpan TimeSpan { get; set; }

    public override string ToString()
    {
        StringBuilder builder = new();

        int hours = TimeSpan.Hours;
        if (TimeSpan.Days > 0)
        {
            hours += TimeSpan.Days * 24;
        }

        if (hours > 0)
        {
            builder.Append($"{hours}h");
        }
        if (TimeSpan.Minutes > 0)
        {
            builder.Append($"{TimeSpan.Minutes}m");
        }
        if (TimeSpan.Seconds > 0)
        {
            builder.Append($"{TimeSpan.Seconds}s");
        }
        long millisecondTicks = TimeSpan.Ticks % TimeSpan.TicksPerSecond;
        if (millisecondTicks > 0)
        {
            decimal milliseconds = (decimal)millisecondTicks / TimeSpan.TicksPerMillisecond;
            builder.Append(milliseconds.ToString("0.####", CultureInfo.InvariantCulture));
            builder.Append("ms");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses a complete duration using optional hours, minutes, seconds, and milliseconds,
    /// in that order. Numeric values use an invariant decimal point; empty text represents zero.
    /// Fractional milliseconds retain precision down to a <see cref="TimeSpan"/> tick.
    /// </summary>
    /// <exception cref="ParseException">The text has invalid units, unit ordering, or trailing content.</exception>
    /// <exception cref="FormatException">A numeric segment is malformed.</exception>
    public static Duration Parse(string text)
    {
        TextParser<TimeSpan> parser =
            from hr in DurationSegment("h").Try().Optional()
            from min in DurationSegment("m").Try().Optional()
            from sec in DurationSegment("s").Try().Optional()
            from ms in DurationSegmentText("ms")
                .Select(val => decimal.Parse(val, CultureInfo.InvariantCulture)).Try().Optional()
            select
                TimeSpan.FromHours(hr ?? 0) +
                TimeSpan.FromMinutes(min ?? 0) +
                TimeSpan.FromSeconds(sec ?? 0) +
                TimeSpan.FromTicks((long)((ms ?? 0) * TimeSpan.TicksPerMillisecond));

        return new Duration(parser.AtEnd().Parse(text));
    }

    private static TextParser<double> DurationSegment(string unit) =>
        DurationSegmentText(unit).Select(val => double.Parse(val, CultureInfo.InvariantCulture));

    private static TextParser<string> DurationSegmentText(string unit) =>
        from val in NativeParsers.Identifier(Character.Digit, Character.Digit.Try().Or(Character.EqualTo('.')))
        from unitParser in Span.EqualTo(unit)
        from unitEnd in Superpower.Parse.Not(Character.Letter)
        select val;
}
