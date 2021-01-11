using System.Text;

namespace Valleysoft.DockerfileModel
{
    public class ResolutionOptions
    {
        public bool UpdateInline { get; set; }

        public bool RemoveEscapeCharacters { get; set; }

        internal string FormatValue(char escapeChar, string value)
        {
            if (RemoveEscapeCharacters)
            {
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < value.Length; i++)
                {
                    if (value[i] == escapeChar)
                    {
                        if (i < value.Length - 1 && value[i + 1] == escapeChar)
                        {
                            builder.Append(escapeChar);
                            i++;
                        }
                        continue;
                    }
                    else
                    {
                        builder.Append(value[i]);
                    }
                }

                return builder.ToString();
            }

            return value;
        }
    }
}
