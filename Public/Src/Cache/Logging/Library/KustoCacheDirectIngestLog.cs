// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Utilities.Core.Tracing;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Cache.Logging
{
    /// <summary>
    /// Handles direct ingestion of cache log lines into a Kusto cluster
    /// </summary>
    /// <remarks>
    /// This is the cache-side analogue of the engine's <c>KustoDirectIngestLog</c>.  It
    /// implements <see cref="IKustoLog"/> so it can be plugged into the NLog adapter in place
    /// of (or alongside) <c>AzureBlobStorageLog</c>.
    /// </remarks>
    public sealed class KustoCacheDirectIngestLog : IKustoLog
    {
        private readonly Guid _sessionId;
        private readonly string _ingestUri;
        private readonly bool _allowInteractiveAuth;
        private readonly Action<string> _errorLogger;
        private readonly Action<string> _debugLogger;
        private readonly IConsole _console;
        private readonly CancellationToken _cancellationToken;

        private KustoDirectIngestLog? _ingestClient;

        /// <inheritdoc/>
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc/>
        public bool StartupStarted { get; private set; }

        /// <inheritdoc/>
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc/>
        public bool ShutdownStarted { get; private set; }

        /// <nodoc/>
        public KustoCacheDirectIngestLog(
            Guid sessionId,
            string ingestUri,
            bool allowInteractiveAuth,
            Action<string> errorLogger,
            Action<string> debugLogger,
            IConsole console,
            CancellationToken cancellationToken)
        {
            _sessionId = sessionId;
            _ingestUri = ingestUri;
            _allowInteractiveAuth = allowInteractiveAuth;
            _errorLogger = errorLogger;
            _debugLogger = debugLogger;
            _console = console;
            _cancellationToken = cancellationToken;
        }

        /// <summary>Convenience no-context overload used by <c>BlobCacheFactoryBase</c>.</summary>
        public Task<BoolResult> StartupAsync() => StartupAsync(new Context(NullLogger.Instance));

        /// <inheritdoc/>
        public Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;
            try
            {

                _ingestClient = KustoCacheDirectIngestLogFactory.TryCreateForCache(_sessionId, _ingestUri, _allowInteractiveAuth, _errorLogger, _debugLogger, _console, _cancellationToken);
                if (_ingestClient == null)
                {
                    return Task.FromResult(new BoolResult("Failed to create KustoCacheDirectIngestLog with the provided parameters."));
                }

                StartupCompleted = true;
                return Task.FromResult(BoolResult.Success);
            }
            catch (Exception ex)
            {
                return Task.FromResult(new BoolResult(ex));
            }
        }

        /// <summary>Convenience no-context overload.</summary>
        public Task<BoolResult> ShutdownAsync() => ShutdownAsync(new Context(NullLogger.Instance));

        /// <inheritdoc/>
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            try
            {
                _ingestClient?.Dispose();
                return Task.FromResult(BoolResult.Success);
            }
            catch (OperationCanceledException)
            {
                ShutdownCompleted = true;
                return Task.FromResult(BoolResult.Success);
            }
            catch (Exception ex)
            {
                _errorLogger.Invoke($"[KustoCacheIngest] Error during shutdown flush: {ex.Message}");
                ShutdownCompleted = true;
                return Task.FromResult(new BoolResult(ex));
            }
        }

        /// <inheritdoc/>
        public void Write(string log)
        {
            _ingestClient?.Write(log);
        }

        /// <inheritdoc/>
        public void WriteBatch(IEnumerable<string> logs)
        {
            foreach (var log in logs)
            {
                Write(log);
            }
        }
    }
}
