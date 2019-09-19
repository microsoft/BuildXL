// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Download.Tracing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Download
{
    /// <summary>
    /// NuGet resolver frontend
    /// </summary>
    public sealed class DownloadFrontEnd : IFrontEnd
    {
        private FrontEndHost m_host;
        private FrontEndContext m_context;
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
        public IReadOnlyCollection<string> SupportedResolvers { get; } = new[]
                    { KnownResolverKind.DownloadResolverKind };

        /// <inheritdoc />
        public void InitializeFrontEnd([JetBrains.Annotations.NotNull] FrontEndHost host, [JetBrains.Annotations.NotNull] FrontEndContext context, [JetBrains.Annotations.NotNull] IConfiguration configuration)
        {
            m_host = host;
            m_context = context;
        }

        /// <inheritdoc />
        public IResolver CreateResolver([JetBrains.Annotations.NotNull] string kind)
        {
            Contract.Requires(SupportedResolvers.Contains(kind));

            return new DownloadResolver(
                m_statistics,
                m_host,
                m_context,
                m_logger,
                Name);
        }

        /// <inheritdoc />
        public void LogStatistics(Dictionary<string, long> statistics)
        {
            m_statistics.Downloads.LogStatistics("Download.Download", statistics);
            m_statistics.Extractions.LogStatistics("Download.Extract", statistics);
        }
    }
}