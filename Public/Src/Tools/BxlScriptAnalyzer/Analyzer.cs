// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.FrontEnd.Script.Analyzer.Tracing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer
{
    /// <summary>
    /// Base class for each DScript analyzer
    /// </summary>
    public abstract class Analyzer
    {
        private Args m_arguments;

        /// <summary>
        /// When NodeHandler is registered this list will be populated. and null otherwise.
        /// </summary>
        private List<NodeHandler>[] m_specHandlers;

        /// <summary>
        /// Kind type of the analyzer
        /// </summary>
        public abstract AnalyzerKind Kind { get; }

        /// <summary>
        /// Whether the tool should auto-fix, or just report errors.
        /// </summary>
        protected bool Fix => m_arguments.Fix;

        /// <summary>
        /// Front end context.
        /// </summary>
        protected FrontEndContext Context { get; private set; }

        /// <summary>
        /// The PathTable analyzers can use
        /// </summary>
        protected PathTable PathTable => Context.PathTable;

        /// <summary>
        /// The workspace
        /// </summary>
        protected Workspace Workspace { get; private set; }

        /// <summary>
        /// Pip graph.
        /// </summary>
        protected IPipGraph PipGraph { get; private set; }

        /// <summary>
        /// The logger
        /// </summary>
        protected Logger Logger { get; private set; }

        /// <summary>
        /// The logging context
        /// </summary>
        protected LoggingContext LoggingContext => Context.LoggingContext;

        /// <summary>
        /// Guards calls to <see cref="RegisterSyntaxNodeAction" /> to only be called from SetSharedState.
        /// </summary>
        protected bool Initializing { get; private set; }

        /// <summary>
        /// Required engine phases.
        /// </summary>
        public virtual EnginePhases RequiredPhases { get; } = EnginePhases.AnalyzeWorkspace;

        /// <summary>
        /// Helper that stores some shared state on the Analyzer
        /// </summary>
        internal bool SetSharedState(Args arguments, FrontEndContext context, Logger logger, Workspace workspace, IPipGraph pipGraph)
        {
            m_arguments = arguments;
            Context = context;
            Workspace = workspace;
            PipGraph = pipGraph;
            Logger = logger;
            Initializing = true;
            var result = Initialize();
            Initializing = false;
            return result;
        }

        /// <summary>
        /// Handles a commandline option for the given analyzer
        /// </summary>
        public virtual bool HandleOption(CommandLineUtilities.Option opt)
        {
            return false;
        }

        /// <summary>
        /// Prints the help for the analyzer
        /// </summary>
        public virtual void WriteHelp(HelpWriter writer)
        {
        }

        /// <summary>
        /// Allows analyzers to call RegisterSyntaxNodeAction
        /// </summary>
        public virtual bool Initialize()
        {
            // By default no errors
            return true;
        }

        /// <summary>
        /// Allows analyzers to wrap something up at the end of analyzing
        /// </summary>
        public virtual bool FinalizeAnalysis()
        {
            // By default no errors
            return true;
        }

        /// <summary>
        /// Helper method for analyzers that produce a folder with outputs
        /// </summary>
        protected bool TryPrepareOutputFolder(string outputFolder, bool cleanOutputFolder)
        {
            if (cleanOutputFolder)
            {
                try
                {
                    if (Directory.Exists(outputFolder))
                    {
                        FileUtilities.DeleteDirectoryContents(outputFolder, deleteRootDirectory: false);
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is BuildXLException)
                {
                    Logger.DocumentationErrorCleaningFolder(LoggingContext, outputFolder, e.Message);
                    return false;
                }
            }

            try
            {
                FileUtilities.CreateDirectory(outputFolder);
            }
            catch (BuildXLException e)
            {
                Logger.DocumentationErrorCreatingOutputFolder(LoggingContext, outputFolder, e.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Registers a handler for a syntax kind.
        /// </summary>
        protected void RegisterSyntaxNodeAction(NodeHandler handler, params TypeScript.Net.Types.SyntaxKind[] syntaxKinds)
        {
            Contract.Requires(Initializing, "RegisterSyntaxNodeAction can only be called from the Initialize method.");

            if (m_specHandlers == null)
            {
                m_specHandlers = new List<NodeHandler>[(int)TypeScript.Net.Types.SyntaxKind.Count];
            }

            foreach (var syntaxKind in syntaxKinds)
            {
                var list = m_specHandlers[(int)syntaxKind];
                if (list == null)
                {
                    list = new List<NodeHandler>();
                    m_specHandlers[(int)syntaxKind] = list;
                }

                list.Add(handler);
            }
        }

        /// <summary>
        /// Performs the analysis.
        /// </summary>
        public virtual bool AnalyzeSourceFile(Workspace workspace, AbsolutePath path, ISourceFile sourceFile)
        {
            if (m_specHandlers != null)
            {
                var context = new DiagnosticsContext(sourceFile, Logger, LoggingContext, PathTable, workspace);

                bool success = true;
                foreach (var node in NodeWalker.TraverseBreadthFirstAndSelf(sourceFile))
                {
                    // Only non-injected nodes are checked by the linter.
                    if (node.IsInjectedForDScript())
                    {
                        continue;
                    }

                    var handlers = m_specHandlers[(int)node.Kind];
                    if (handlers != null)
                    {
                        foreach (var handler in handlers)
                        {
                            success &= handler(node, context);
                        }
                    }
                }

                return success;
            }

            return true;
        }
    }
}
