// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Threading.Tasks;
using NLog;
using NLog.Common;
using NLog.Targets;

#nullable enable

namespace BuildXL.Cache.Logging.External
{
    /// <summary>
    ///     Makes <see cref="AzureBlobStorageLog"/> available as an NLog target
    /// </summary>
    [Target("AzureBlobStorageLogTarget")]
    public sealed class AzureBlobStorageLogTarget : TargetWithLayoutHeaderAndFooter
    {
        private readonly AzureBlobStorageLog _log;

        /// <nodoc />
        public AzureBlobStorageLogTarget(AzureBlobStorageLog log)
        {
            _log = log;
            _log.OnFileOpen = WriteHeaderAsync;
            _log.OnFileClose = WriteFooterAsync;

            // Enabling a feature that allows NLog to re-use internal buffers to reduce allocations.
            OptimizeBufferReuse = true;
        }

        private Task WriteHeaderAsync(StreamWriter streamWriter)
        {
            if (Header != null)
            {
                var line = Header.Render(LogEventInfo.CreateNullEvent());
                return streamWriter.WriteLineAsync(line);
            }

            return Task.CompletedTask;
        }

        private Task WriteFooterAsync(StreamWriter streamWriter)
        {
            if (Footer != null)
            {
                var line = Footer.Render(LogEventInfo.CreateNullEvent());
                return streamWriter.WriteLineAsync(line);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override void Write(LogEventInfo logEvent)
        {
            // RenderLogEvent respects 'OptimizeBufferReuse' flag and will have less allocations
            // compared to a _log.Write(Layout.Render(logEvent)); call.
            _log.Write(RenderLogEvent(Layout, logEvent));
        }
    }
}
