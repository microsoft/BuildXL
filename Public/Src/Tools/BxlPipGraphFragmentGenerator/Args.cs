// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration.Mutable;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.PipGraphFragmentGenerator
{
    internal sealed class Args : CommandLineUtilities
    {
        /// <summary>
        /// Whether to display help or not.
        /// </summary>
        public bool Help { get; private set; }

        /// <summary>
        /// Resulting command line configuration.
        /// </summary>
        public readonly CommandLineConfiguration CommandLineConfig;

        /// <summary>
        /// Resulting configuration specific for pip graph fragment generation.
        /// </summary>
        public readonly PipGraphFragmentGeneratorConfiguration PipGraphFragmentGeneratorConfig;

        private readonly PathTable m_pathTable;

        /// <nodoc />
        public Args(string[] args, PathTable pathTable)
            : base(args)
        {
            Contract.Requires(pathTable != null);

            m_pathTable = pathTable;
            CommandLineConfig = new CommandLineConfiguration();
            PipGraphFragmentGeneratorConfig = new PipGraphFragmentGeneratorConfiguration();

            NamedOption[] namedOptions = new[]
            {
                new NamedOption(
                    "config",
                    shortName: "c",
                    description: "Config file",
                    action: opt => CommandLineConfig.Startup.ConfigFile = AbsolutePath.Create(m_pathTable, opt.Value)),

                new NamedOption(
                    "filter",
                    shortName: "f",
                    description: "Pip filter",
                    action: opt => CommandLineConfig.Filter = opt.Value),

                new NamedOption(
                    "property",
                    shortName: "p",
                    description: "Build property",
                    action: opt => ParsePropertyOption(opt, CommandLineConfig.Startup.Properties)),

                new NamedOption(
                    "inCloudBuild",
                    description: "Indicate if build will be run in CloudBuild",
                    action: opt => CommandLineConfig.InCloudBuild = ParseBooleanOption(opt)),

                new NamedOption(
                    "computeStaticFingerprints",
                    description: "Compute pip static fingerprints on pip graph fragment creation",
                    action: opt => CommandLineConfig.Schedule.ComputePipStaticFingerprints = ParseBooleanOption(opt)),

                new NamedOption(
                    "objectDirectory",
                    description: "Object directory used by pips in the created pip graph fragment",
                    action: opt => CommandLineConfig.Layout.ObjectDirectory = AbsolutePath.Create(m_pathTable, GetFullPath(opt.Value, opt))),

                new NamedOption(
                    "outputDirectory",
                    description: "Output directory used by pips in the created pip graph fragment",
                    action: opt => CommandLineConfig.Layout.OutputDirectory = AbsolutePath.Create(m_pathTable, GetFullPath(opt.Value, opt))),

                new NamedOption(
                    "redirectedUserProfileJunctionRoot",
                    description: "Root for redirected user profile junction",
                    action: opt => CommandLineConfig.Layout.RedirectedUserProfileJunctionRoot = AbsolutePath.Create(m_pathTable, GetFullPath(opt.Value, opt))),

                // Options specific for configuring pip graph fragment generation.

                new NamedOption(
                    "outputFile",
                    shortName: "o",
                    description: "Output file containing the generated pip graph fragment",
                    action: opt => PipGraphFragmentGeneratorConfig.OutputFile = AbsolutePath.Create(m_pathTable, GetFullPath(opt.Value, opt))),

                new NamedOption(
                    "description",
                    shortName: "d",
                    description: "Provide description for the pip graph fragment",
                    action: opt => PipGraphFragmentGeneratorConfig.Description = ParseStringOption(opt)),

                new NamedOption(
                    "topSort",
                    shortName: "t",
                    description: "Enable topological sort on serializing pip graph fragment",
                    action: opt => PipGraphFragmentGeneratorConfig.TopSort = ParseBooleanOption(opt)),

                new NamedOption(
                    "alternateSymbolSeparator",
                    shortName: "sep",
                    description: "Set the alternate symbol separator",
                    action: opt =>
                    {
                        var separator = ParseStringOption(opt);
                        PipGraphFragmentGeneratorConfig.AlternateSymbolSeparator = separator.Length > 0 ? separator[0] : default;
                    }),

                // Help.

                new NamedOption(
                    "help",
                    shortName: "?",
                    description: "Prints help",
                    action: opt => Help = true)

            };

            var mappedNamedOptions = new Dictionary<string, NamedOption>(StringComparer.OrdinalIgnoreCase);
            Array.ForEach(
                namedOptions,
                o =>
                {
                    mappedNamedOptions.Add(o.LongName, o);
                    if (!string.IsNullOrWhiteSpace(o.ShortName))
                    {
                        mappedNamedOptions.Add(o.ShortName, o);
                    }
                });

            foreach (Option option in Options)
            {
                if (!mappedNamedOptions.TryGetValue(option.Name, out var namedOption))
                {
                    throw Error(I($"Unsupported option '{option.Name}'"));
                }

                namedOption.Action?.Invoke(option);

                if (Help)
                {
                    WriteHelp(namedOptions);
                    return;
                }
            }

            if (!CommandLineConfig.Startup.ConfigFile.IsValid)
            {
                throw Error("Config file is not specified");
            }
        }

        private static void WriteHelp(NamedOption[] namedOptions)
        {
            HelpWriter writer = new HelpWriter();
            writer.WriteBanner("Tool for generating pip graph fragment by evaluating BuildXL specifications");

            foreach (var namedOption in namedOptions)
            {
                namedOption.WriteDescription(writer);
            }
        }

        private struct NamedOption
        {
            public readonly string LongName;
            public readonly string ShortName;
            public readonly string Description;
            public readonly Action<Option> Action;

            public NamedOption(string longName, string shortName = null, string description = null, Action<Option> action = null)
            {
                Contract.Requires(!string.IsNullOrWhiteSpace(longName));
                Contract.Requires(shortName == null || !string.IsNullOrWhiteSpace(shortName));
                Contract.Requires(description == null || !string.IsNullOrWhiteSpace(description));

                LongName = longName;
                Action = action;
                ShortName = shortName;
                Description = description;
            }

            public bool Match(string option) =>
                string.Equals(option, LongName, StringComparison.OrdinalIgnoreCase)
                || (ShortName != null && string.Equals(option, ShortName, StringComparison.OrdinalIgnoreCase));

            public bool ActionIfMatch(Option option)
            {
                bool matched = Match(option.Name);
                if (matched)
                {
                    Action?.Invoke(option);
                }

                return matched;
            }

            public void WriteDescription(HelpWriter writer) => writer.WriteOption(LongName, Description ?? string.Empty, shortName: ShortName);
        }
    }
}
