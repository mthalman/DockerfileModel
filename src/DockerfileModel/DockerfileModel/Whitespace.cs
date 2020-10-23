using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;

namespace DockerfileModel
{
    public class Whitespace : DockerfileConstruct
    {
        private Whitespace(string value)
            : base(value, GetParser())
        {
        }

        public string? Value
        {
            get => ValueToken?.Value;
            set
            {
                WhitespaceToken? valueToken = ValueToken;
                if (valueToken != null && value is not null)
                {
                    valueToken.Value = value;
                }
                else
                {
                    ValueToken = value is null ? null : new WhitespaceToken(value!);
                }
            }
        }

        public WhitespaceToken? ValueToken
        {
            get => Tokens.OfType<WhitespaceToken>().FirstOrDefault();
            set
            {
                SetToken(ValueToken, value,
                    addToken: token => TokenList.Insert(0, token));
            }
        }

        public string? NewLine
        {
            get => NewLineToken?.Value;
            set
            {
                NewLineToken? newLine = NewLineToken;
                if (newLine != null && value is not null)
                {
                    newLine.Value = value;
                }
                else
                {
                    NewLineToken = String.IsNullOrEmpty(value) ? null : new NewLineToken(value!);
                }
            }
        }

        public NewLineToken? NewLineToken
        {
            get => Tokens.OfType<NewLineToken>().FirstOrDefault();
            set
            {
                SetToken(ValueToken, value);
            }
        }

        public override ConstructType Type => ConstructType.Whitespace;

        public static Whitespace Create(string value) =>
            new Whitespace(value);

        public static bool IsWhitespace(string value) =>
            GetParser().TryParse(value).WasSuccessful;

        public static Parser<IEnumerable<Token>> GetParser() =>
            ParseHelper.Whitespace().End();
    }
}
