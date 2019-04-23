// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    internal sealed class AnalysisContext
    {
        private bool m_disableLanguagePolicies;

        /// <summary>
        /// Disables all (optional) language policies, like semicolon checks.
        /// </summary>
        public void DisableLanguagePolicies()
        {
            m_disableLanguagePolicies = true;
        }

        private sealed class NodeHandlersRepository
        {
            private readonly List<NodeHandler>[] m_specHandlers;
            private readonly List<NodeHandler>[] m_configHandlers;
            private readonly List<NodeHandler>[] m_packageConfigHandlers;
            private readonly List<NodeHandler>[] m_buildListHandlers;

            public NodeHandlersRepository(int numberOfNodes)
            {
                m_specHandlers = CreateEmptyList(numberOfNodes);
                m_configHandlers = CreateEmptyList(numberOfNodes);
                m_packageConfigHandlers = CreateEmptyList(numberOfNodes);
                m_buildListHandlers = CreateEmptyList(numberOfNodes);
            }

            public void RegisterHandler(RuleAnalysisScope analysisScope, int kind, NodeHandler handler)
            {
                if ((analysisScope & RuleAnalysisScope.SpecFile) != RuleAnalysisScope.None)
                {
                    m_specHandlers[kind].Add(handler);
                }

                if ((analysisScope & RuleAnalysisScope.RootConfig) != RuleAnalysisScope.None)
                {
                    m_configHandlers[kind].Add(handler);
                }

                if ((analysisScope & RuleAnalysisScope.PackageConfig) != RuleAnalysisScope.None)
                {
                    m_packageConfigHandlers[kind].Add(handler);
                }

                if ((analysisScope & RuleAnalysisScope.BuildListFile) != RuleAnalysisScope.None)
                {
                    m_buildListHandlers[kind].Add(handler);
                }
            }

            public List<NodeHandler> GetHandlers(RuleAnalysisScope scope, int kind)
            {
                Contract.Requires(IsSingleCaseEnum(scope));

                return GetHandlerFields(scope)[kind];
            }

            private List<NodeHandler>[] GetHandlerFields(RuleAnalysisScope scope)
            {
                Contract.Requires(IsSingleCaseEnum(scope));

                if ((scope & RuleAnalysisScope.SpecFile) != RuleAnalysisScope.None)
                {
                    return m_specHandlers;
                }

                if ((scope & RuleAnalysisScope.RootConfig) != RuleAnalysisScope.None)
                {
                    return m_configHandlers;
                }

                if ((scope & RuleAnalysisScope.PackageConfig) != RuleAnalysisScope.None)
                {
                    return m_packageConfigHandlers;
                }

                if ((scope & RuleAnalysisScope.BuildListFile) != RuleAnalysisScope.None)
                {
                    return m_buildListHandlers;
                }

                throw Contract.AssertFailure(
                   I($"Unknown RuleAnalysisScope '{scope}'."));
            }

            private static List<NodeHandler>[] CreateEmptyList(int count)
            {
                var result = new List<NodeHandler>[count];
                for (int i = 0; i < count; i++)
                {
                    result[i] = new List<NodeHandler>();
                }

                return result;
            }
        }

        private readonly NodeHandlersRepository m_handlersesRepository;

        public AnalysisContext()
        {
            int numberOfSyntaxKinds = (int)TypeScript.Net.Types.SyntaxKind.Count;
            m_handlersesRepository = new NodeHandlersRepository(numberOfSyntaxKinds);
        }

        public void RegisterSyntaxNodeAction(DiagnosticRule rule, NodeHandler handler, params TypeScript.Net.Types.SyntaxKind[] syntaxKinds)
        {
            // TODO: Create type safe registration.
            Contract.Requires(handler != null);
            Contract.Requires(syntaxKinds != null);

            if (m_disableLanguagePolicies && rule.RuleType == RuleType.LanguagePolicy)
            {
                // Skipping language policy rule, because they were disabled.
                return;
            }

            foreach (var kind in syntaxKinds)
            {
                Contract.Assume((int)kind >= 0 && (int)kind <= (int)TypeScript.Net.Types.SyntaxKind.Count);

                m_handlersesRepository.RegisterHandler(rule.AnalysisScope, (int)kind, handler);
            }
        }

        [Pure]
        public List<NodeHandler> GetHandlers(RuleAnalysisScope scope, TypeScript.Net.Types.SyntaxKind syntaxKind)
        {
            Contract.Requires(IsSingleCaseEnum(scope), "Scope should have just once bit enabled!");
            Contract.Requires((int)syntaxKind >= 0 && (int)syntaxKind <= (int)TypeScript.Net.Types.SyntaxKind.Count);
            Contract.Ensures(Contract.Result<IReadOnlyList<NodeHandler>>() != null);

            return m_handlersesRepository.GetHandlers(scope, (int)syntaxKind);
        }

        /// <summary>
        /// Returns true if a given <paramref name="scope"/> has just one bit enabled.
        /// </summary>
        /// <remarks>
        /// In some cases this analysis is critical because it simplifies a lot an implementation logic and lead to more efficient implementation.
        /// This method is public only due to Code Contracts requirements.
        /// </remarks>
        [Pure]
        public static bool IsSingleCaseEnum(RuleAnalysisScope scope)
        {
            return NumberOfSetBits((int)scope) == 1;
        }

        private static int NumberOfSetBits(int i)
        {
            // This is a canonical way to get number of set bits in a DWORD.
            i = i - ((i >> 1) & 0x55555555);
            i = (i & 0x33333333) + ((i >> 2) & 0x33333333);
            return (((i + (i >> 4)) & 0x0F0F0F0F) * 0x01010101) >> 24;
        }
    }

    internal sealed class DiagnosticContext
    {
        public DiagnosticContext(ISourceFile sourceFile, RuleAnalysisScope analysisScope, Logger logger, LoggingContext loggingContext, PathTable pathTable, Workspace workspace)
        {
            Workspace = workspace;
            PathTable = pathTable;
            SemanticModel = workspace?.GetSemanticModel();
            SourceFile = sourceFile;
            AnalysisScope = analysisScope;
            LoggingContext = loggingContext;
            Logger = logger;
        }

        public ISourceFile SourceFile { get; }

        public RuleAnalysisScope AnalysisScope { get; }

        public LoggingContext LoggingContext { get; }

        public Logger Logger { get; }

        public ISemanticModel SemanticModel { get; }

        public Workspace Workspace { get; }

        public PathTable PathTable { get; set; }

        public bool IsSemanticModelAvailable => SemanticModel != null;
    }

    internal delegate void NodeHandler(INode node, DiagnosticContext context);
}
