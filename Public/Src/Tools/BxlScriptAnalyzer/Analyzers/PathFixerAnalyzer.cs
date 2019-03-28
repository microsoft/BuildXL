// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.ToolSupport;
using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer.Analyzers
{
    /// <summary>
    /// Analyzer that fixes some of the Paths
    /// </summary>
    public class PathFixerAnalyzer : Analyzer
    {
        /// <summary>
        /// Whether all directories should be lowercased
        /// </summary>
        public bool LowerCaseDirectories { get; private set; }

        private PathFixer.SlashType Slashes { get; set; }

        /// <inheritdoc />
        public override AnalyzerKind Kind => AnalyzerKind.PathFixer;

        /// <inheritdoc />
        public override bool HandleOption(CommandLineUtilities.Option opt)
        {
            if (string.Equals("pathFormat", opt.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals("p", opt.Name, StringComparison.OrdinalIgnoreCase))
            {
                Slashes = CommandLineUtilities.ParseEnumOption<PathFixer.SlashType>(opt);

                switch (Slashes)
                {
                    case PathFixer.SlashType.Default:
                    case PathFixer.SlashType.Unix:
                    case PathFixer.SlashType.Windows:
                        break;
                    default:
                        throw Contract.AssertFailure("Unexpected enum value for SlashType");
                }

                return true;
            }

            if (string.Equals("lowerCaseDirectories", opt.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals("l", opt.Name, StringComparison.OrdinalIgnoreCase))
            {
                LowerCaseDirectories = CommandLineUtilities.ParseBooleanOption(opt);
                return true;
            }

            return base.HandleOption(opt);
        }

        /// <inheritdoc />
        public override void WriteHelp(HelpWriter writer)
        {
            writer.WriteOption("pathFormat", "The path format to use. Valid options are: " + string.Join(", ", Enum.GetNames(typeof(PathFixer.SlashType))), shortName: "p");
            writer.WriteOption("lowerCaseDirectories", "Whether to update all file, path and directory literals to have a lowercase for the Directories. Note that Paths (p`...`) and RelativePaths (r`...`) are assumed to be files, so the last part is not lowercased.", shortName: "l");
            base.WriteHelp(writer);
        }

        /// <inheritdoc />
        public override bool Initialize()
        {
            PathFixer = new PathFixer(LowerCaseDirectories, Slashes);

            RegisterSyntaxNodeAction(PathFix, TypeScript.Net.Types.SyntaxKind.TaggedTemplateExpression);
            return base.Initialize();
        }

        /// <summary>
        /// Fix Path handler
        /// </summary>
        public bool PathFix(INode node, [CanBeNull] DiagnosticsContext context)
        {
            var taggedTemplateExpression = node.Cast<ITaggedTemplateExpression>();

            return Fix ? PathFixer.Fix(taggedTemplateExpression) : PathFixer.Analyze(taggedTemplateExpression, context, Logger, LoggingContext);
        }

        private PathFixer PathFixer { get; set; }
    }
}
