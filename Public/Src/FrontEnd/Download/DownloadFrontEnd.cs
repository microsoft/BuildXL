// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Download.Tracing;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
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
        private readonly GlobalConstants m_constants;
        private readonly ModuleRegistry m_sharedModuleRegistry;
        private FrontEndHost m_host;
        private FrontEndContext m_context;
        private Logger m_logger;
        private Statistics m_statistics;

        /// <summary>
        /// Gets or sets the name of the front-end.
        /// </summary>
        public string Name { get; }

        /// <nodoc/>
        public DownloadFrontEnd(
            GlobalConstants constants,
            ModuleRegistry sharedModuleRegistry,
            Logger logger = null)
        {
            Name = nameof(DownloadFrontEnd);
            m_constants = constants;
            m_sharedModuleRegistry = sharedModuleRegistry;
            m_logger = logger ?? Logger.Log;
            m_statistics = new Statistics();
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> SupportedResolvers { get; } = new[]
                    { KnownResolverKind.DownloadResolverKind };

        /// <inheritdoc />
        public void InitializeFrontEnd([NotNull] FrontEndHost host, [NotNull] FrontEndContext context, [NotNull] IConfiguration configuration)
        {
            m_host = host;
            m_context = context;
        }

        /// <inheritdoc />
        public IResolver CreateResolver([NotNull] string kind)
        {
            Contract.Requires(SupportedResolvers.Contains(kind));

            return new DownloadResolver(
                m_constants,
                m_sharedModuleRegistry,
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