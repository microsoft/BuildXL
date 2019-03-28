// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Analyzer.Documentation;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer.Analyzers
{
    /// <summary>
    /// Analyzer that generates HtmlDocumentation
    /// </summary>
    public class DocumentationGenerator : Analyzer
    {
        private const string OutputFolderOptionLong = "outputFolder";
        private const string OutputFolderOptionShort = "o";
        private const string RootLinkOptionLong = "rootLink";
        private const string RootLinkOptionShort = "l";
        private const string CleanOutputOptionLong = "cleanOutput";
        private const string CleanOutputOptionShort = "c";
        private const string ModuleListOptionLong = "moduleList";
        private const string ModuleListOptionShort = "m";

        /// <summary>
        /// Where the Documentation should be generated in.
        /// </summary>
        public string OutputFolder { get; private set; }

        /// <summary>
        /// Root path for links.
        /// </summary>
        public string RootLink { get; private set; }

        /// <summary>
        /// Whether the output folder should be cleaned before running.
        /// </summary>
        /// <remarks>
        /// Defaults to true.
        /// </remarks>
        public bool CleanOutputFolder { get; private set; } = true;

        /// <summary>
        /// List of modules to emit documentation for.
        /// </summary>
        public HashSet<string> ModuleList;

        /// <inheritdoc />
        public override AnalyzerKind Kind => AnalyzerKind.Documentation;

        private DocWorkspace DocWorkspace { get; } = new DocWorkspace("TestRepo");

        /// <inheritdoc />
        public override bool HandleOption(CommandLineUtilities.Option opt)
        {
            if (string.Equals(OutputFolderOptionLong, opt.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(OutputFolderOptionShort, opt.Name, StringComparison.OrdinalIgnoreCase))
            {
                OutputFolder = CommandLineUtilities.ParsePathOption(opt);
                return true;
            }

            if (string.Equals(RootLinkOptionLong, opt.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(RootLinkOptionShort, opt.Name, StringComparison.OrdinalIgnoreCase))
            {
                RootLink = CommandLineUtilities.ParseStringOption(opt);
                return true;
            }

            if (string.Equals(CleanOutputOptionLong, opt.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(CleanOutputOptionShort, opt.Name, StringComparison.OrdinalIgnoreCase))
            {
                CleanOutputFolder = CommandLineUtilities.ParseBooleanOption(opt);
                return true;
            }

            if (string.Equals(ModuleListOptionLong, opt.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ModuleListOptionShort, opt.Name, StringComparison.OrdinalIgnoreCase))
            {
                ModuleList = new HashSet<string>(CommandLineUtilities.ParseStringOption(opt).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                return true;
            }

            return base.HandleOption(opt);
        }

        /// <inheritdoc />
        public override void WriteHelp(HelpWriter writer)
        {
            writer.WriteOption(OutputFolderOptionLong, "The path where the documentation should be generated", shortName: OutputFolderOptionShort);
            writer.WriteOption(RootLinkOptionLong, "The root path used for links in the returned Markdown", shortName: RootLinkOptionShort);
            writer.WriteOption(
                CleanOutputOptionLong,
                "Whether to clean the output folder before generating documentation. (Defaults to true)",
                shortName: CleanOutputOptionShort);
            writer.WriteOption(ModuleListOptionLong, "Documentation is emitted for each module named in this list (',' delimited)", shortName: ModuleListOptionShort);
            base.WriteHelp(writer);
        }

        /// <inheritdoc />
        public override bool Initialize()
        {
            if (string.IsNullOrEmpty(OutputFolder))
            {
                Logger.DocumentationMissingOutputFolder(LoggingContext, OutputFolderOptionLong);
                return false;
            }

            if (!TryPrepareOutputFolder(OutputFolder, CleanOutputFolder))
            {
                return false;
            }

            return base.Initialize();
        }

        /// <inheritdoc />
        public override bool AnalyzeSourceFile(BuildXL.FrontEnd.Workspaces.Core.Workspace workspace, AbsolutePath path, ISourceFile sourceFile)
        {
            var parsedModule = workspace.TryGetModuleBySpecFileName(path);
            if (parsedModule == null)
            {
                // Skip all spec files not part of a module
                return true;
            }

            var module = DocWorkspace.GetOrAddModule(parsedModule.Descriptor.Name, parsedModule.Descriptor.Version);

            if (parsedModule.Definition.ResolutionSemantics == NameResolutionSemantics.ImplicitProjectReferences)
            {
                var visitor = new DocumentationVisitor(module, path);
                visitor.VisitSourceFile(sourceFile);
            }
            else
            {
                if (!module.Ignored)
                {
                    module.Ignored = true;
                    Logger.DocumentationSkippingV1Module(LoggingContext, module.Name);
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override bool FinalizeAnalysis()
        {
            MarkdownWriter.SetRootFolder(OutputFolder);
            MarkdownWriter.SetRootLink(RootLink);
            MarkdownWriter.SetModuleList(ModuleList);
            MarkdownWriter.WriteWorkspace(DocWorkspace);
            return base.FinalizeAnalysis();
        }
    }
}
