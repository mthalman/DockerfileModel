﻿using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class RunInstruction : InstructionBase
    {
        private RunInstruction(string text, char escapeChar)
            : base(text, GetParser(escapeChar))
        {
        }

        public RunCommand Command
        {
            get => this.Tokens.OfType<RunCommand>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(Command, value);
            }
        }

        public IEnumerable<MountFlag> MountFlags => Tokens.OfType<MountFlag>();

        public static RunInstruction Parse(string text, char escapeChar) =>
            new RunInstruction(text, escapeChar);

        public static Parser<IEnumerable<Token>> GetParser(char escapeChar) =>
            Instruction("RUN", escapeChar,
                GetArgsParser(escapeChar));

        public static RunInstruction Create(string command) =>
            Parse($"RUN {command}", Instruction.DefaultEscapeChar);

        public static RunInstruction Create(IEnumerable<string> commands) =>
            Parse($"RUN {ExecFormRunCommand.FormatCommands(commands)}", Instruction.DefaultEscapeChar);

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
        {
            // Do not resolve variables for the command of a RUN instruction. It is shell/runtime-specific.
            return ToString();
        }

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            from mounts in ArgTokens(MountFlag.GetParser(escapeChar).AsEnumerable(), escapeChar).Many()
            from whitespace in Whitespace()
            from command in ArgTokens(GetCommandParser(escapeChar).AsEnumerable(), escapeChar)
            select ConcatTokens(
                mounts.Flatten(), whitespace, command);

        private static Parser<RunCommand> GetCommandParser(char escapeChar) =>
            ExecFormRunCommand.GetParser(escapeChar)
                .Cast<ExecFormRunCommand, RunCommand>()
                .XOr(ShellFormRunCommand.GetParser(escapeChar));
    }
}