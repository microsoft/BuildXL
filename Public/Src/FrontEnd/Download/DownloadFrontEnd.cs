// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Download.Tracing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Download
{
    /// <summary>
    /// NuGet resolver frontend
    /// </summary>
    public sealed class DownloadFrontEnd : FrontEnd<DownloadWorkspaceResolver>
    {
        private readonly Logger m_logger;
        private readonly Statistics m_statistics;

        /// <summary>
        /// Gets or sets the name of the front-end.
        /// </summary>
        public const string Name = KnownResolverKind.DownloadResolverKind;

        /// <nodoc/>
        public DownloadFrontEnd()
        {
            m_logger = Logger.Log;
            m_statistics = new Statistics();
        }

        /// <inheritdoc />
        public override IReadOnlyCollection<string> SupportedResolvers { get; } = new[]
                    { KnownResolverKind.DownloadResolverKind };

        /// <inheritdoc />
        public override IResolver CreateResolver([NotNull] string kind)
        {
            Contract.Requires(SupportedResolvers.Contains(kind));

            return new DownloadResolver(
                m_statistics,
                Host,
                Context,
                m_logger,
                Name);
        }

        /// <inheritdoc />
        public override void LogStatistics(Dictionary<string, long> statistics)
        {
            m_statistics.Downloads.LogStatistics("Download.Download", statistics);
            m_statistics.Extractions.LogStatistics("Download.Extract", statistics);
        }
    }
}