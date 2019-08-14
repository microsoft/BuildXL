// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer.Analyzers
{
    /// <summary>
    /// Analyzer for generating graph fragment.
    /// </summary>
    public class GraphFragmentGenerator : Analyzer
    {
        private string m_outputFile;
        private string m_description;
        private readonly OptionName m_outputFileOption = new OptionName("OutputFile", "o");
        private readonly OptionName m_descriptionOption = new OptionName("Description", "d");
        private readonly OptionName m_outputDirectoryForEvaluationOption = new OptionName("OutputDirectoryForEvaluation");

        private AbsolutePath m_absoluteOutputPath;

        /// <inheritdoc />
        public override AnalyzerKind Kind => AnalyzerKind.GraphFragment;

        /// <inheritdoc />
        public override EnginePhases RequiredPhases => EnginePhases.Schedule;

        /// <inheritdoc />
        public override bool HandleOption(CommandLineUtilities.Option opt)
        {
            if (m_outputFileOption.Match(opt.Name))
            {
                m_outputFile = CommandLineUtilities.ParsePathOption(opt);
                return true;
            }

            if (m_descriptionOption.Match(opt.Name))
            {
                m_description = opt.Value;
                return true;
            }

            return base.HandleOption(opt);
        }

        /// <inheritdoc />
        public override void WriteHelp(HelpWriter writer)
        {
            writer.WriteOption(m_outputFileOption.LongName, "The path where the graph fragment should be generated", shortName: m_outputFileOption.ShortName);
            base.WriteHelp(writer);
        }

        /// <inheritdoc />
        public override bool Initialize()
        {
            if (string.IsNullOrEmpty(m_outputFile))
            {
                Logger.GraphFragmentMissingOutputFile(LoggingContext, m_outputFileOption.LongName);
                return false;
            }

            if (!Path.IsPathRooted(m_outputFile))
            {
                m_outputFile = Path.GetFullPath(m_outputFile);
            }

            if (!AbsolutePath.TryCreate(PathTable, m_outputFile, out m_absoluteOutputPath))
            {
                Logger.GraphFragmentInvalidOutputFile(LoggingContext, m_outputFile, m_outputFileOption.LongName);
                return false;
            }

            return base.Initialize();
        }

        /// <inheritdoc />
        public override bool AnalyzeSourceFile(BuildXL.FrontEnd.Workspaces.Core.Workspace workspace, AbsolutePath path, ISourceFile sourceFile) => true;

        /// <inheritdoc />
        public override bool FinalizeAnalysis()
        {
            if (PipGraph == null)
            {
                Logger.GraphFragmentMissingGraph(LoggingContext);
                return false;
            }

            var serializer = new PipGraphFragmentSerializer(Context, new PipGraphFragmentContext());

            try
            {
                serializer.Serialize(m_absoluteOutputPath, PipGraph.RetrieveScheduledPips().ToList(), m_description);
                Logger.GraphFragmentSerializationStats(LoggingContext, serializer.FragmentDescription, serializer.Stats.ToString());
            }
            catch (Exception e) when (e is BuildXLException || e is IOException)
            {
                Logger.GraphFragmentExceptionOnSerializingFragment(LoggingContext, m_absoluteOutputPath.ToString(Context.PathTable), e.ToString());
                return false;
            }

            return base.FinalizeAnalysis();
        }

        private struct OptionName
        {
            public readonly string LongName;
            public readonly string ShortName;

            public OptionName(string name)
            {
                LongName = name;
                ShortName = name;
            }

            public OptionName(string longName, string shortName)
            {
                LongName = longName;
                ShortName = shortName;
            }

            public bool Match(string option) =>
                string.Equals(option, LongName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option, ShortName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
