using System.Text;

namespace Valleysoft.DockerfileModel;

/// <summary>Controls variable substitution and the formatting of returned resolved text.</summary>
public class ResolutionOptions
{
    /// <summary>Gets or sets whether resolved variable-reference tokens are replaced in the model. Defaults to false.</summary>
    /// <remarks>
    /// Document resolution may also process preceding ARG declarations. This does not write supplied overrides
    /// into declaration defaults or expand runtime commands.
    /// </remarks>
    public bool UpdateInline { get; set; }

    /// <summary>Gets or sets whether escape characters are removed from returned resolved text. Defaults to false.</summary>
    /// <remarks>
    /// A doubled escape character becomes one; other escape characters are omitted. This formatting option
    /// alone does not mutate tokens and is not a general shell or JSON unescaper.
    /// </remarks>
    public bool RemoveEscapeCharacters { get; set; }

    internal string FormatValue(char escapeChar, string value)
    {
        if (RemoveEscapeCharacters)
        {
            StringBuilder builder = new();
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
