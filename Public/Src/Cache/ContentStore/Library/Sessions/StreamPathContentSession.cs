// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Tracing;

namespace BuildXL.Cache.ContentStore.Sessions
{
    /// <summary>
    ///     An <see cref="IContentSession"/> implemented over two <see cref="FileSystemContentSession"/>'s, one for putting paths and the other for putting streams.
    /// </summary>
    public sealed class StreamPathContentSession : IContentSession, IHibernateContentSession
    {
        private const string SessionForPathText = "Path content session";
        private const string SessionForStreamText = "Stream content session";

        private readonly ContentSessionTracer _tracer = new ContentSessionTracer(nameof(StreamPathContentSession));
        private readonly IContentSession _sessionForPath;
        private readonly IContentSession _sessionForStream;
        private bool _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="StreamPathContentSession" /> class.
        /// </summary>
        public StreamPathContentSession(
            string name,
            IContentSession sessionForStream,
            IContentSession sessionForPath)
        {
            Name = name;
            _sessionForStream = sessionForStream;
            _sessionForPath = sessionForPath;
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        public Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;

            return StartupCall<ContentSessionTracer>.RunAsync(_tracer, context, async () =>
            {
                var startupResults =
                    await
                        Task.WhenAll(_sessionForStream.StartupAsync(context), _sessionForPath.StartupAsync(context));
                Contract.Assert(startupResults.Length == 2);

                var startupResultForStream = startupResults[0];
                var startupResultForPath = startupResults[1];

                var result = startupResultForStream & startupResultForPath;
                if (!result.Succeeded)
                {
                    var sb = new StringBuilder();

                    if (!startupResultForStream.Succeeded)
                    {
                        sb.Concat($"{SessionForStreamText} startup failed, error=[{startupResultForStream}]", "; ");
                    }

                    if (!startupResultForPath.Succeeded)
                    {
                        sb.Concat($"{SessionForPathText} startup failed, error=[{startupResultForPath}]", "; ");
                    }

                    if (startupResultForStream.Succeeded)
                    {
                        var shutdownResult = await _sessionForStream.ShutdownAsync(context);
                        if (!shutdownResult.Succeeded)
                        {
                            sb.Concat($"{SessionForStreamText} shutdown failed, error=[{shutdownResult}]", "; ");
                        }
                    }

                    if (startupResultForPath.Succeeded)
                    {
                        var shutdownResult = await _sessionForPath.ShutdownAsync(context);
                        if (!shutdownResult.Succeeded)
                        {
                            sb.Concat($"{SessionForPathText} shutdown failed, error=[{shutdownResult}]", "; ");
                        }
                    }

                    result = new BoolResult(sb.ToString());
                }

                StartupCompleted = true;

                return result;
            });
        }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;

            return ShutdownCall<ContentSessionTracer>.RunAsync(_tracer, context, async () =>
            {
                var shutdownResults =
                    await
                        Task.WhenAll(_sessionForStream.ShutdownAsync(context), _sessionForPath.ShutdownAsync(context));
                Contract.Assert(shutdownResults.Length == 2);

                var shutdownResultForStream = shutdownResults[0];
                var shutdownResultForPath = shutdownResults[1];

                var result = shutdownResultForStream & shutdownResultForPath;

                if (!result.Succeeded)
                {
                    var sb = new StringBuilder();
                    if (!shutdownResultForStream.Succeeded)
                    {
                        sb.Concat($"{SessionForStreamText} shutdown failed, error=[{shutdownResultForStream}]", "; ");
                    }

                    if (!shutdownResultForPath.Succeeded)
                    {
                        sb.Concat($"{SessionForPathText} shutdown failed, error=[{shutdownResultForPath}]", "; ");
                    }

                    result = new BoolResult(sb.ToString());
                }

                ShutdownCompleted = true;

                return result;
            });
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);

            // ReSharper disable once GCSuppressFinalizeForTypeWithoutDestructor
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc />
        public async Task<PinResult> PinAsync(
            Context context,
            ContentHash contentHash,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var pinResult = await _sessionForStream.PinAsync(context, contentHash, cts, urgencyHint);

            if (pinResult.Code == PinResult.ResultCode.ContentNotFound)
            {
                pinResult = await _sessionForPath.PinAsync(context, contentHash, cts, urgencyHint);
            }

            return pinResult;
        }

        /// <inheritdoc />
        public async Task<OpenStreamResult> OpenStreamAsync(
            Context context,
            ContentHash contentHash,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var openStreamResult = await _sessionForStream.OpenStreamAsync(context, contentHash, cts, urgencyHint);

            if (openStreamResult.Code == OpenStreamResult.ResultCode.ContentNotFound)
            {
                openStreamResult = await _sessionForPath.OpenStreamAsync(context, contentHash, cts, urgencyHint);
            }

            return openStreamResult;
        }

        /// <inheritdoc />
        public async Task<PlaceFileResult> PlaceFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var placeFileResult = await _sessionForPath.PlaceFileAsync(
                        context,
                        contentHash,
                        path,
                        accessMode,
                        replacementMode,
                        realizationMode,
                        cts,
                        urgencyHint);

            if (placeFileResult.Code == PlaceFileResult.ResultCode.NotPlacedContentNotFound)
            {
                placeFileResult = await _sessionForStream.PlaceFileAsync(
                        context,
                        contentHash,
                        path,
                        accessMode,
                        replacementMode,
                        realizationMode,
                        cts,
                        urgencyHint);
            }

            return placeFileResult;
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return Workflows.RunWithFallback(
                contentHashes,
                inputContentHashes => _sessionForStream.PinAsync(context, inputContentHashes, cts, urgencyHint),
                inputContentHashes => _sessionForPath.PinAsync(context, inputContentHashes, cts, urgencyHint),
                result => result.Succeeded);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(
            Context context,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return Workflows.RunWithFallback(
                hashesWithPaths,
                inputHashesWithPaths =>
                    _sessionForPath.PlaceFileAsync(context, inputHashesWithPaths, accessMode, replacementMode, realizationMode, cts, urgencyHint),
                inputHashesWithPaths =>
                    _sessionForStream.PlaceFileAsync(context, inputHashesWithPaths, accessMode, replacementMode, realizationMode, cts, urgencyHint),
                result => result.Code != PlaceFileResult.ResultCode.NotPlacedContentNotFound);
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _sessionForPath.PutFileAsync(context, hashType, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _sessionForPath.PutFileAsync(context, contentHash, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(
            Context context,
            HashType hashType,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _sessionForStream.PutStreamAsync(context, hashType, stream, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(
            Context context,
            ContentHash contentHash,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _sessionForStream.PutStreamAsync(context, contentHash, stream, cts, urgencyHint);
        }

        /// <inheritdoc />
        public IEnumerable<ContentHash> EnumeratePinnedContentHashes()
        {
            var sessionForStream = _sessionForStream as IHibernateContentSession;
            var sessionForPath = _sessionForPath as IHibernateContentSession;

            var pinnedHashes = sessionForStream != null
                ? sessionForStream.EnumeratePinnedContentHashes()
                : Enumerable.Empty<ContentHash>();

            return sessionForPath != null ? pinnedHashes.Concat(sessionForPath.EnumeratePinnedContentHashes()) : pinnedHashes;
        }

        /// <inheritdoc />
        public async Task PinBulkAsync(Context context, IEnumerable<ContentHash> contentHashes)
        {
            var contentHashList = contentHashes.ToList();
            var results = await Workflows.RunWithFallback(
                contentHashList,
                inputContentHashes => _sessionForStream.PinAsync(context, inputContentHashes, CancellationToken.None),
                inputContentHashes => _sessionForPath.PinAsync(context, inputContentHashes, CancellationToken.None),
                result => result.Succeeded);

            foreach (var result in results)
            {
                var r = await result;
                if (!r.Item.Succeeded)
                {
                    _tracer.Warning(context, $"Failed to pin contentHash=[{contentHashList[r.Index].ToShortString()}]");
                }
            }
        }

        /// <summary>
        ///     Dispose pattern.
        /// </summary>
        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _sessionForStream.Dispose();
                _sessionForPath.Dispose();
            }

            _disposed = true;
        }
    }
}
