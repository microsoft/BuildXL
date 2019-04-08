// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// DScript front-end.
    /// </summary>
    public sealed class DScriptFrontEnd : DScriptInterpreterBase, IFrontEnd
    {
        private readonly IDecorator<EvaluationResult> m_evaluationDecorator;
        private SourceFileProcessingQueue<bool> m_sourceFileProcessingQueue;

        /// <nodoc/>
        public DScriptFrontEnd(
            GlobalConstants constants,
            ModuleRegistry sharedModuleRegistry,
            IFrontEndStatistics statistics,
            Logger logger = null,
            IDecorator<EvaluationResult> evaluationDecorator = null)
            : base(constants, sharedModuleRegistry, statistics, logger)
        {
            Name = nameof(DScriptFrontEnd);

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
            return new DScriptSourceResolver(Constants, SharedModuleRegistry, FrontEndHost, Context, Configuration,
                FrontEndStatistics, m_sourceFileProcessingQueue, Logger, m_evaluationDecorator);
        }

        /// <inheritdoc />
        public void LogStatistics(Dictionary<string, long> statistics)
        {
            // Scripts statistics are still logged centrally for now.
        }
    }
}
