﻿using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class MountFlag : KeyValueToken<KeywordToken, Mount>
    {
        public MountFlag(Mount mount)
            : base(new KeywordToken("mount"), mount, isFlag: true)
        {
        }

        internal MountFlag(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static MountFlag Parse(string text,
            char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(text, Keyword("mount", escapeChar), MountParser(escapeChar), tokens => new MountFlag(tokens), escapeChar: escapeChar);

        public static Parser<MountFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            GetParser(Keyword("mount", escapeChar), MountParser(escapeChar), tokens => new MountFlag(tokens), escapeChar: escapeChar);

        private static Parser<Mount> MountParser(char escapeChar) =>
            SecretMount.GetParser(escapeChar);
    }
}
