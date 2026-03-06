// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using NLog;
using NLog.Targets;

#nullable enable

namespace BuildXL.Cache.Logging.External
{
    /// <summary>
    /// Makes <see cref="KustoCacheDirectIngestLog"/> available as an NLog target.
    /// </summary>
    [Target("KustoCacheDirectIngestLogTarget")]
    public sealed class KustoCacheDirectIngestLogTarget : TargetWithLayout
    {
        private readonly KustoCacheDirectIngestLog _log;

        /// <nodoc />
        public KustoCacheDirectIngestLogTarget(KustoCacheDirectIngestLog log)
        {
            _log = log;

            // Allow NLog to reuse internal buffers, reducing allocations.
            OptimizeBufferReuse = true;
        }

        /// <inheritdoc />
        protected override void Write(LogEventInfo logEvent)
        {
            // RenderLogEvent respects 'OptimizeBufferReuse' and has fewer allocations
            // than calling Layout.Render(logEvent) directly.
            _log.Write(RenderLogEvent(Layout, logEvent));
        }
    }
}
