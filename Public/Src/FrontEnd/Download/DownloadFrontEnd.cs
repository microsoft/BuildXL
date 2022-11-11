// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Download.Tracing;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Download
{
    /// <summary>
    /// Download resolver frontend
    /// </summary>
    public sealed class DownloadFrontEnd : FrontEnd<DownloadWorkspaceResolver>
    {
        private readonly Script.Tracing.Logger m_logger;
        private readonly FrontEndStatistics m_frontEndStatistics;
        private readonly EvaluationStatistics m_evaluationStatistics;

        /// <summary>
        /// Gets or sets the name of the front-end.
        /// </summary>
        public override string Name => KnownResolverKind.DownloadResolverKind;

        /// <inheritdoc />
        public override bool ShouldRestrictBuildParameters { get; } = true;

        /// <nodoc/>
        public DownloadFrontEnd()
        {
            m_logger = Script.Tracing.Logger.CreateLogger(preserveLogEvents: true);
            m_frontEndStatistics = new FrontEndStatistics();
            m_evaluationStatistics = new EvaluationStatistics();
        }

        /// <inheritdoc />
        public override IReadOnlyCollection<string> SupportedResolvers { get; } = new[] { KnownResolverKind.DownloadResolverKind };

        /// <inheritdoc />
        public override IResolver CreateResolver([NotNull] string kind)
        {
            Contract.Requires(SupportedResolvers.Contains(kind));

            return new DownloadResolver(
                m_frontEndStatistics,
                m_evaluationStatistics,
                Host,
                Context,
                m_logger,
                Name);
        }

        /// <inheritdoc />
        public override void LogStatistics(Dictionary<string, long> statistics)
        {
            // If the frontend was not properly initialized, there is nothing to log
            if (Context == null)
            {
                return;
            }

            Logger.Log.ContextStatistics(Context.LoggingContext, Name, m_evaluationStatistics.ContextTrees,
                m_evaluationStatistics.Contexts);

            var frontEndStatistics = new Dictionary<string, long>
            {
                { "Download.AggregatedAstConversionCount", (long)m_frontEndStatistics.SpecAstConversion.Count },
                { "Download.AggregatedAstConversionDurationMs", (long)m_frontEndStatistics.SpecAstConversion.AggregateDuration.TotalMilliseconds },
                { "Download.AggregatedAstSerializationDurationMs", (long)m_frontEndStatistics.SpecAstSerialization.AggregateDuration.TotalMilliseconds },
                { "Download.AggregatedAstDeserializationDurationMs", (long)m_frontEndStatistics.SpecAstDeserialization.AggregateDuration.TotalMilliseconds },
            };

            Logger.Log.BulkStatistic(Context.LoggingContext, frontEndStatistics);
        }
    }
}