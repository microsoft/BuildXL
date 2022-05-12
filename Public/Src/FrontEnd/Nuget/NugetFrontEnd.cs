// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// NuGet resolver frontend
    /// </summary>
    public sealed class NugetFrontEnd : DScriptInterpreterBase, IFrontEnd
    {
        private readonly IDecorator<EvaluationResult> m_evaluationDecorator;
        private SourceFileProcessingQueue<bool> m_sourceFileProcessingQueue;

        private ConcurrentDictionary<IResolverSettings, WorkspaceNugetModuleResolver> m_workspaceResolverCache = new ConcurrentDictionary<IResolverSettings, WorkspaceNugetModuleResolver>();

        /// <nodoc/>
        public NugetFrontEnd(
            IFrontEndStatistics statistics,
            Logger logger = null,
            IDecorator<EvaluationResult> evaluationDecorator = null)
            : base(statistics, logger)
        {
            Name = nameof(NugetFrontEnd);

            m_evaluationDecorator = evaluationDecorator;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> SupportedResolvers { get; } = new[] { WorkspaceNugetModuleResolver.NugetResolverName };

        /// <inheritdoc />
        public bool ShouldRestrictBuildParameters { get; } = false;

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

        /// <nodoc/>
        public IResolver CreateResolver(string kind)
        {
            Contract.Requires(SupportedResolvers.Contains(kind));
            Contract.Assert(m_sourceFileProcessingQueue != null, "Initialize method should be called to initialize m_sourceFileProcessingQueue.");

            return new NugetResolver(
                FrontEndHost,
                Context,
                Configuration,
                FrontEndStatistics,
                m_sourceFileProcessingQueue,
                Logger,
                m_evaluationDecorator);
        }

        /// <inheritdoc/>
        public bool TryCreateWorkspaceResolver(IResolverSettings resolverSettings, out IWorkspaceModuleResolver workspaceResolver)
        {
            workspaceResolver = m_workspaceResolverCache.GetOrAdd(
                resolverSettings,
                (settings) =>
                {
                    var resolver = new WorkspaceNugetModuleResolver(Context.StringTable, FrontEndStatistics);
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
            // Nuget statistics still go through central system rather than pluggable system.
        }
    }
}
