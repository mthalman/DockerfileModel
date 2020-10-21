﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DockerfileModel.Tokens;

namespace DockerfileModel
{
    public class Dockerfile : IConstructContainer
    {
        public Dockerfile(IEnumerable<DockerfileConstruct> items)
        {
            this.Items = items;
        }

        public IEnumerable<DockerfileConstruct> Items { get; }

        public char EscapeChar =>
            Items
                .OfType<ParserDirective>()
                .FirstOrDefault(directive => directive.DirectiveName == ParserDirective.EscapeDirective)
                ?.DirectiveValue[0] ?? Instruction.DefaultEscapeChar;

        public static Dockerfile Parse(string text) =>
            DockerfileParser.ParseContent(text);

        public string ResolveVariables<TInstruction>(
            TInstruction instruction,
            IDictionary<string, string?>? argValues = null,
            ResolutionOptions? options = null)
            where TInstruction : InstructionBase
        {
            bool foundInstruction = false;

            return ResolveVariables(
                argValues,
                stagesView =>
                {
                    Stage stage = stagesView.Stages
                        .FirstOrDefault(stage => stage.FromInstruction == instruction || stage.Items.Contains(instruction));

                    if (stage is null)
                    {
                        throw new ArgumentException(
                            $"Instruction '{instruction}' is not contained in this Dockerfile.", nameof(instruction));
                    }

                    return new Stage[] { stage };
                },
                currentInstruction =>
                {
                    if (foundInstruction)
                    {
                        return false;
                    }

                    if (currentInstruction == instruction)
                    {
                        foundInstruction = true;
                    }

                    return currentInstruction is ArgInstruction || foundInstruction;
                },
                options);
        }

        public string ResolveVariables(IDictionary<string, string?>? variableOverrides = null, ResolutionOptions? options = null) =>
            ResolveVariables(
                variableOverrides,
                stagesView => stagesView.Stages,
                instruction => true,
                options);

        private string ResolveVariables(
            IDictionary<string, string?>? variableOverrides,
            Func<StagesView, IEnumerable<Stage>> getStages,
            Func<InstructionBase, bool> processInstruction,
            ResolutionOptions? options)
        {
            if (variableOverrides is null)
            {
                variableOverrides = new Dictionary<string, string?>();
            }

            if (options is null)
            {
                options = new ResolutionOptions();
            }

            StagesView stagesView = new StagesView(this);

            char escapeChar = EscapeChar;
            Dictionary<string, string?> globalArgs = GetGlobalArgs(stagesView, escapeChar, variableOverrides, options);

            var stages = getStages(stagesView);

            string? resolvedValue = null;

            foreach (Stage stage in stages)
            {
                if (processInstruction(stage.FromInstruction))
                {
                    resolvedValue = stage.FromInstruction.ResolveVariables(escapeChar, globalArgs, options);
                }

                Dictionary<string, string?> stageArgs = new Dictionary<string, string?>();

                IEnumerable<InstructionBase> instructions = stage.Items
                    .OfType<InstructionBase>()
                    .Where(instruction => processInstruction(instruction));

                foreach (InstructionBase instruction in instructions)
                {
                    if (instruction is ArgInstruction argInstruction)
                    {
                        // If this is just an arg declaration and a value has been provided from a global arg or arg override
                        if (argInstruction.ArgValue is null && globalArgs.TryGetValue(argInstruction.ArgName, out string? globalArg))
                        {
                            stageArgs.Add(argInstruction.ArgName, globalArg);
                        }
                        // If an arg override exists for this arg
                        else if (variableOverrides.TryGetValue(argInstruction.ArgName, out string? overrideArgValue))
                        {
                            stageArgs.Add(argInstruction.ArgName, overrideArgValue);
                        }
                        else
                        {
                            string? resolvedArgValue = argInstruction.ArgValueToken?.ResolveVariables(escapeChar, stageArgs, options);
                            stageArgs[argInstruction.ArgName] = resolvedArgValue;
                            resolvedValue = instruction.ResolveVariables(escapeChar, stageArgs, options);
                        }
                    }
                    else
                    {
                        resolvedValue = instruction.ResolveVariables(escapeChar, stageArgs, options);
                    }
                }
            }

            return resolvedValue ?? String.Empty;
        }

        private static Dictionary<string, string?> GetGlobalArgs(StagesView stagesView, char escapeChar, IDictionary<string, string?> variables,
            ResolutionOptions options)
        {
            Dictionary<string, string?> globalArgs = new Dictionary<string, string?>();
            foreach (ArgInstruction arg in stagesView.GlobalArgs)
            {
                if (variables.TryGetValue(arg.ArgName, out string? overridenValue))
                {
                    globalArgs.Add(arg.ArgName, overridenValue);
                }
                else
                {
                    string? resolvedValue = arg.ArgValueToken?.ResolveVariables(escapeChar, globalArgs, options);
                    globalArgs.Add(arg.ArgName, resolvedValue);
                }
            }

            return globalArgs;
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();

            var items = Items.ToArray();
            for (int i = 0; i < items.Length; i++)
            {
                builder.Append(items[i].ToString());
            }

            return builder.ToString();
        }
    }
}
