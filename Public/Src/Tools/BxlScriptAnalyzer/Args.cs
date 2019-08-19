// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.ToolSupport;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Analyzer
{
    internal sealed partial class Args : CommandLineUtilities
    {
        private static readonly string[] s_helpStrings = new[] { "?", "help" };

        /// <summary>
        /// The path to configuration file.
        /// </summary>
        public readonly string Config;

        /// <summary>
        /// The filter expression.
        /// </summary>
        public readonly string Filter;

        /// <summary>
        /// Whether the tool should fix the specs or only report issues
        /// </summary>
        public readonly bool Fix;

        /// <summary>
        /// Whether to display help or not.
        /// </summary>
        public readonly bool Help;

        /// <summary>
        /// The analyzers to apply (in sequence)
        /// </summary>
        public readonly List<Analyzer> Analyzers;

        /// <summary>
        /// Object directory used for evaluation.
        /// </summary>
        public readonly string ObjectDirectory;

        /// <summary>
        /// Output directory used for evaluation.
        /// </summary>
        public readonly string OutputDirectory;

        /// <nodoc />
        public Args(string config, string filter, bool fix, bool help, List<Analyzer> analyzers, params string[] args)
            : base(args)
        {
            Config = config;
            Filter = filter;
            Fix = fix;
            Help = help;
            Analyzers = analyzers;
        }

        /// <nodoc />
        public Args(string[] args, Func<AnalyzerKind, Analyzer> analyzerFactory)
            : base(args)
        {
            Analyzers = new List<Analyzer>();
            Analyzer currentAnalyzer = null;

            foreach (Option opt in Options)
            {
                if (opt.Name.Equals("filter", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("f", StringComparison.OrdinalIgnoreCase))
                {
                    Filter = opt.Value;
                }
                else if (opt.Name.Equals("config", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("c", StringComparison.OrdinalIgnoreCase))
                {
                    Config = opt.Value;
                }
                else if (opt.Name.Equals("objectDirectory", StringComparison.OrdinalIgnoreCase))
                {
                    ObjectDirectory = GetFullPath(opt.Value, opt);
                }
                else if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase))
                {
                    OutputDirectory = GetFullPath(opt.Value, opt);
                }
                else if (opt.Name.Equals("fix", StringComparison.OrdinalIgnoreCase))
                {
                    Fix = ParseBooleanOption(opt);
                }
                else if (opt.Name.Equals("analyzer", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("a", StringComparison.OrdinalIgnoreCase))
                {
                    var analyzerType = ParseEnumOption<AnalyzerKind>(opt);
                    var newAnalyzer = analyzerFactory(analyzerType);
                    if (newAnalyzer == null)
                    {
                        throw Error(I($"Unsupported Analyzer type '{analyzerType}'. Supported types are: {string.Join(", ", Enum.GetValues(typeof(AnalyzerKind)).Cast<string>())}"));
                    }

                    currentAnalyzer = newAnalyzer;
                    Analyzers.Add(currentAnalyzer);
                }
                else if (s_helpStrings.Any(s => opt.Name.Equals(s, StringComparison.OrdinalIgnoreCase)))
                {
                    Help = true;
                    WriteHelp(analyzerFactory);
                }
                else
                {
                    if (currentAnalyzer == null)
                    {
                        throw Error(I($"Unsupported option: {opt.Name}."));
                    }
                    else
                    {
                        if (!currentAnalyzer.HandleOption(opt))
                        {
                            throw new InvalidOperationException(I($"Unsupported option '{opt.Name}'. This option is not supported by analyzer {currentAnalyzer.Kind}."));
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(Filter) && string.IsNullOrEmpty(Config))
            {
                throw Error("Must specify at least /Config -or- /Filter argument.");
            }

            if (Analyzers.Count == 0)
            {
                throw Error("Must specify at least one Analyzer.");
            }
        }

        private static void WriteHelp(Func<AnalyzerKind, Analyzer> analyzerFactory)
        {
            HelpWriter writer = new HelpWriter();
            writer.WriteBanner($"Tool for performing analysis/transformation of cached pip graphs and execution logs.");

            writer.WriteOption(nameof(Config), "Optional main config file to be used.", shortName: "c");
            writer.WriteOption(nameof(Filter), "The filter representing the scope of script specs that should be analyzed.", shortName: "f");
            writer.WriteOption("Analyzer", "One or more analyzers to run. Subsequent arguments are analyzer specific.", shortName: "a");
            writer.WriteOption($"{nameof(Fix)}[+|-]", "Whether the analyzer should fix the specs.");

            writer.WriteLine(string.Empty);
            writer.WriteLine("Analyzers:");

            foreach (var type in Enum.GetValues(typeof(AnalyzerKind)).Cast<AnalyzerKind>())
            {
                var analyzer = analyzerFactory(type);
                if (analyzer != null)
                {
                    writer.WriteBanner(I($"{type}"));
                    analyzer.WriteHelp(writer);
                }
            }
        }
    }
}
