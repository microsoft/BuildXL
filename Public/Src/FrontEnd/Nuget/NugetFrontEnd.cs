// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// NuGet resolver frontend
    /// </summary>
    public sealed class NugetFrontEnd : DScriptInterpreterBase, Sdk.IFrontEnd
    {
        private readonly IDecorator<EvaluationResult> m_evaluationDecorator;
        private SourceFileProcessingQueue<bool> m_sourceFileProcessingQueue;

        /// <nodoc/>
        public NugetFrontEnd(
            GlobalConstants constants,
            ModuleRegistry sharedModuleRegistry,
            IFrontEndStatistics statistics,
            Logger logger = null,
            IDecorator<EvaluationResult> evaluationDecorator = null)
            : base(constants, sharedModuleRegistry, statistics, logger)
        {
            Name = nameof(NugetFrontEnd);

            m_evaluationDecorator = evaluationDecorator;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> SupportedResolvers { get; } = new[] { WorkspaceNugetModuleResolver.NugetResolverName };

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
                Constants,
                SharedModuleRegistry,
                FrontEndHost,
                Context,
                Configuration,
                FrontEndStatistics,
                m_sourceFileProcessingQueue,
                Logger,
                m_evaluationDecorator);
        }

        /// <inheritdoc />
        public void LogStatistics(Dictionary<string, long> statistics)
        {
            // Nuget statistics still go through central system rather than pluggable system.
        }
    }
}
