// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Script.Analyzer.Codex;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using Codex.Analysis.External;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer.Analyzers
{
    internal class CodexAnalyzer : Analyzer
    {
        public override AnalyzerKind Kind => AnalyzerKind.Codex;

        private CodexContext m_codex;

        private string m_outputDirectory;

        public override bool HandleOption(CommandLineUtilities.Option opt)
        {
            if (opt.Name.Equals("out", StringComparison.OrdinalIgnoreCase) ||
                opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
            {
                m_outputDirectory = CommandLineUtilities.ParsePathOption(opt);
                return true;
            }

            return base.HandleOption(opt);
        }

        public override bool Initialize()
        {
            var store = new CodexSemanticStore(m_outputDirectory);
            m_codex = new CodexContext(store, Workspace);

            if (!TryPrepareOutputFolder(m_outputDirectory, cleanOutputFolder: true))
            {
                return false;
            }

            return base.Initialize();
        }

        /// <inheritdoc />
        public override bool FinalizeAnalysis()
        {
            // Analyzers are skipping prelude and configuration modules.
            // Need to analyze them manually.
            AnalyzePreludeAndConfigurationFiles();

            CleanOutputDirectory();

            m_codex.LinkSpans();

            foreach (var file in m_codex.Store.Files.List)
            {
                m_codex.Store.SaveFile(file);
            }

            m_codex.Store.Save();

            return base.FinalizeAnalysis();
        }

        /// <summary>
        /// Performs the analysis.
        /// </summary>
        public override bool AnalyzeSourceFile(Workspace workspace, AbsolutePath path, ISourceFile sourceFile)
        {
            if (sourceFile.Text == null)
            {
                return true;
            }

            var parsedModule = workspace.TryGetModuleBySpecFileName(path);

            var file = new CodexFile()
            {
                Path = path.ToString(PathTable),
                Length = sourceFile.End,
                Hash = path.ToString(PathTable),
            };

            m_codex.Store.Files.Add(file);

            var moduleName = parsedModule.Descriptor.DisplayName.Replace(Names.ConfigModuleName, "RootConfig");
            var project = m_codex.Store.Projects.Add(new CodexProject()
            {
                Name = $@"DominoScript_{moduleName}",
                Directory = parsedModule.Definition.Root.ToString(PathTable),
                PrimaryFile = parsedModule.Definition.MainFile.ToString(PathTable),
            });

            var semanticModel = workspace.GetSemanticModel();

            var definitionVisitor = new CodexDefinitionVisitor(m_codex, file, project, parsedModule.Descriptor.DisplayName, semanticModel, sourceFile);
            definitionVisitor.VisitSourceFile();

            var visitor = new CodexReferenceVisitor(m_codex, file, definitionVisitor);
            visitor.VisitSourceFile(sourceFile);

            return true;
        }

        private void CleanOutputDirectory()
        {
            if (Directory.Exists(m_outputDirectory))
            {
                Directory.Delete(m_outputDirectory, true);
            }

            Directory.CreateDirectory(m_outputDirectory);
        }

        private void AnalyzePreludeAndConfigurationFiles()
        {
            foreach (var preludeSpec in Workspace.PreludeModule.Specs.Concat(Workspace.ConfigurationModule.Specs))
            {
                AnalyzeSourceFile(Workspace, preludeSpec.Key, preludeSpec.Value);
            }
        }
    }
}
