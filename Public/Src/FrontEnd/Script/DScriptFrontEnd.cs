// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// DScript front-end.
    /// </summary>
    public sealed class DScriptFrontEnd : DScriptInterpreterBase, IFrontEnd
    {
        private readonly IDecorator<EvaluationResult> m_evaluationDecorator;
        private SourceFileProcessingQueue<bool> m_sourceFileProcessingQueue;

        private ConcurrentDictionary<IResolverSettings, IWorkspaceModuleResolver> m_workspaceResolverCache = new ConcurrentDictionary<IResolverSettings, IWorkspaceModuleResolver>();

        private Logger m_customLogger;

        /// <nodoc/>
        public DScriptFrontEnd(
            IFrontEndStatistics statistics,
            Logger logger = null,
            IDecorator<EvaluationResult> evaluationDecorator = null)
            : base(statistics, logger)
        {
            Name = nameof(DScriptFrontEnd);

            m_customLogger = logger;
            m_evaluationDecorator = evaluationDecorator;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> SupportedResolvers { get; } = new[]
            { KnownResolverKind.DScriptResolverKind, KnownResolverKind.SourceResolverKind, KnownResolverKind.DefaultSourceResolverKind };

        // To avoid an issue from the ccrewrite, the method from the base class was renamed
        // from Initialize to InitializeInterpreter.
        // Otherwise there is no way to override the Initialize method in this case.

        /// <inheritdoc />
        public void InitializeFrontEnd(FrontEndHost host, FrontEndContext context, IConfiguration configuration)
        {
            Contract.Requires(host != null);
            Contract.Requires(context != null);
            Contract.Requires(configuration != null);

            InitializeInterpreter(host, context, configuration);
        }

        /// <inheritdoc />
        public override void InitializeInterpreter(FrontEndHost host, FrontEndContext context, IConfiguration configuration)
        {
            base.InitializeInterpreter(host, context, configuration);

            m_sourceFileProcessingQueue = new SourceFileProcessingQueue<bool>(configuration.FrontEnd.MaxFrontEndConcurrency());
        }

        /// <inheritdoc />
        public IResolver CreateResolver(string kind)
        {
            Contract.Requires(SupportedResolvers.Contains(kind));
            Contract.Assert(m_sourceFileProcessingQueue != null, "Initialize method should called to initialize m_sourceFileProcessingQueue.");

            // A DScriptSourceResolver can take care of a source and a default source resolver settings
            return new DScriptSourceResolver(FrontEndHost, Context, Configuration,
                FrontEndStatistics, m_sourceFileProcessingQueue, Logger, m_evaluationDecorator);
        }

        /// <inheritdoc/>
        public bool TryCreateWorkspaceResolver(IResolverSettings resolverSettings, out IWorkspaceModuleResolver workspaceResolver)
        {
            workspaceResolver = m_workspaceResolverCache.GetOrAdd(
                resolverSettings,
                (settings) =>
                {
                    IWorkspaceModuleResolver resolver;
                    if (string.Equals(resolverSettings.Kind, KnownResolverKind.DefaultSourceResolverKind, System.StringComparison.Ordinal))
                    {
                        resolver = new WorkspaceDefaultSourceModuleResolver(Context.StringTable, FrontEndStatistics, logger: m_customLogger);
                    }
                    else
                    {
                        resolver = new WorkspaceSourceModuleResolver(Context.StringTable, FrontEndStatistics, logger: m_customLogger);
                    }

                    if (resolver.TryInitialize(FrontEndHost, Context, Configuration, settings))
                    {
                        return resolver;
                    }

                    return null;
                });

            return workspaceResolver != null;
        }

        /// <inheritdoc />
        public void LogStatistics(Dictionary<string, long> statistics)
        {
            // Scripts statistics are still logged centrally for now.
        }
    }
}
