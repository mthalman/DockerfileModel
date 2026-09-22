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
        if (TimeSpan.Milliseconds > 0)
        {
            builder.Append($"{TimeSpan.Milliseconds}ms");
        }

        return builder.ToString();
    }

    public static Duration Parse(string text)
    {
        TextParser<TimeSpan> parser =
            from hr in DurationSegment("h").Try().Optional()
            from min in DurationSegment("m").Try().Optional()
            from sec in DurationSegment("s").Try().Optional()
            from ms in DurationSegment("ms").Try().Optional()
            select
                TimeSpan.FromHours(hr ?? 0) +
                TimeSpan.FromMinutes(min ?? 0) +
                TimeSpan.FromSeconds(sec ?? 0) +
                TimeSpan.FromMilliseconds(ms ?? 0);

        return new Duration(parser.Parse(text));
    }

    private static TextParser<double> DurationSegment(string unit) =>
        from val in NativeParsers.Identifier(Character.Digit, Character.Digit.Try().Or(Character.EqualTo('.')))
        from unitParser in Span.EqualTo(unit)
        select double.Parse(val);
}
