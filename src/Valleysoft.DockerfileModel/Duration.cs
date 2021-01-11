using System;
using System.Text;
using Sprache;

namespace Valleysoft.DockerfileModel
{
    public class Duration
    {
        public Duration(TimeSpan timeSpan)
        {
            TimeSpan = timeSpan;
        }

        public TimeSpan TimeSpan { get; set; }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();

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
            Parser<TimeSpan> parser =
                from hr in DurationSegment("h").Optional()
                from min in DurationSegment("m").Optional()
                from sec in DurationSegment("s").Optional()
                from ms in DurationSegment("ms").Optional()
                select
                    TimeSpan.FromHours(hr.GetOrDefault()) +
                    TimeSpan.FromMinutes(min.GetOrDefault()) +
                    TimeSpan.FromSeconds(sec.GetOrDefault()) +
                    TimeSpan.FromMilliseconds(ms.GetOrDefault());

            return new Duration(parser.Parse(text));
        }

        private static Parser<double> DurationSegment(string unit) =>
            from val in Sprache.Parse.Identifier(Sprache.Parse.Digit, Sprache.Parse.Digit.Or(Sprache.Parse.Char('.')))
            from unitParser in Sprache.Parse.String(unit)
            select double.Parse(val);
    }
}
