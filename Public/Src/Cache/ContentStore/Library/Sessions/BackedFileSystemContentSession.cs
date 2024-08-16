// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Sessions.Internal;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Interop.Unix;
using BuildXL.Utilities.Core;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Session which aggregates a local and backing content session which also represents local content. This is used to ensure
    /// that content is deduplicated as hardlinks between local and backing store.
    /// </summary>
    public class BackedFileSystemContentSession : ContentSessionBase, IContentSession, IHibernateContentSession, ITrustedContentSession
    {
        protected readonly ITrustedContentSession LocalSession;
        protected readonly IContentSession BackingSession;
        protected readonly IAbsFileSystem FileSystem;
        private readonly FileSystemContentStore _localStore;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(BackedFileSystemContentSession));

        /// <nodoc />
        public BackedFileSystemContentSession(
            string name,
            IAbsFileSystem fileSystem,
            FileSystemContentStore localStore,
            ITrustedContentSession localSession,
            IContentSession backingSession)
            : base(name)
        {
            Contract.Requires(name != null);
            Contract.Requires(fileSystem != null);
            Contract.Requires(localSession != null);
            Contract.Requires(backingSession != null);

            FileSystem = fileSystem;
            _localStore = localStore;
            LocalSession = localSession;
            BackingSession = backingSession;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            return (await LocalSession.StartupAsync(context) & await BackingSession.StartupAsync(context));
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            return (await LocalSession.ShutdownAsync(context) & await BackingSession.ShutdownAsync(context));
        }

        private async Task<PutResult> MultiLevelWriteAsync(
            OperationContext context,
            ContentHash? hash,
            UrgencyHint urgencyHint,
            Func<IContentSession, Task<PutResult>> writeAsync)
        {
            // 
            if (!hash.HasValue || !_localStore.Contains(hash.Value))
            {
                var backingResult = await writeAsync(BackingSession);
                if (backingResult.Succeeded)
                {
                    if (_localStore.Contains(backingResult.ContentHash))
                    {
                        return backingResult;
                    }
                    else
                    {
                        return await TransferContentAsync(context, backingResult.ContentHash, backingResult.ContentSize, urgencyHint, BackingSession, LocalSession);
                    }
                }
            }

            return await writeAsync(LocalSession);
        }

        private Task<PutResult> TransferContentAsync(
            OperationContext context,
            ContentHash hash,
            long size,
            UrgencyHint urgencyHint,
            IContentSession sourceSession,
            IContentSession targetSession)
        {
            return context.PerformOperationAsync<PutResult>(
                Tracer,
                async () =>
                {
                    var tempLocation = AbsolutePath.CreateRandomFileName(_localStore.Store.TempFolder);

                    var placeResult = await sourceSession.PlaceFileAsync(
                        context,
                        hash,
                        tempLocation,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.None,
                        FileRealizationMode.Any,
                        context.Token,
                        urgencyHint).ThrowIfFailure();

                    return await targetSession.PutOrPutTrustedFileAsync(
                        context,
                        new ContentHashWithSize(hash, placeResult.FileSize),
                        tempLocation,
                        FileRealizationMode.Move,
                        context.Token,
                        urgencyHint);
                },
                traceOperationStarted: TraceOperationStarted,
                extraStartMessage: $"Hash=[{hash}] Size=[{size}]",
                extraEndMessage: r => $"Hash=[{hash}] Size=[{size}]");
        }

        /// <inheritdoc />
        public IEnumerable<ContentHash> EnumeratePinnedContentHashes()
        {
            return LocalSession is IHibernateContentSession session
                ? session.EnumeratePinnedContentHashes()
                : Enumerable.Empty<ContentHash>();
        }

        /// <inheritdoc />
        public Task PinBulkAsync(Context context, IEnumerable<ContentHash> contentHashes)
        {
            return LocalSession is IHibernateContentSession session
                ? session.PinBulkAsync(context, contentHashes)
                : BoolResult.SuccessTask;
        }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownEvictionAsync(Context context)
        {
            return LocalSession is IHibernateContentSession session
                ? session.ShutdownEvictionAsync(context)
                : BoolResult.SuccessTask;
        }

        /// <inheritdoc />
        protected override Task<OpenStreamResult> OpenStreamCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return LocalSession.OpenStreamAsync(operationContext, contentHash, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PinResult> PinCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return LocalSession.PinAsync(operationContext, contentHash, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHash> contentHashes,
            UrgencyHint urgencyHint,
            Counter retryCounter,
            Counter fileCounter)
        {
            return LocalSession.PinAsync(operationContext, contentHashes, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PlaceFileResult> PlaceFileCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return LocalSession.PlaceFileAsync(
                    operationContext,
                    contentHash,
                    path,
                    accessMode,
                    replacementMode,
                    realizationMode,
                    operationContext.Token,
                    urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutFileCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return MultiLevelWriteAsync(
                operationContext,
                contentHash,
                urgencyHint,
                session => session.PutFileAsync(
                    operationContext,
                    contentHash,
                    path,
                    CoerceRealizationMode(realizationMode, session),
                    operationContext.Token,
                    urgencyHint));
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutFileCoreAsync(
            OperationContext operationContext,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return MultiLevelWriteAsync(
                operationContext,
                hash: null,
                urgencyHint,
                session => session.PutFileAsync(
                    operationContext,
                    hashType,
                    path,
                    CoerceRealizationMode(realizationMode, session),
                    operationContext.Token,
                    urgencyHint));
        }

        private FileRealizationMode CoerceRealizationMode(FileRealizationMode mode, IContentSession session)
        {
            // Backing session may be on a different volume. Don't enforce the same rules around FileRealizationMode.
            if (mode == FileRealizationMode.HardLink && session == BackingSession)
            {
                return FileRealizationMode.Any;
            }

            return mode;
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return MultiLevelWriteAsync(
                operationContext,
                contentHash,
                urgencyHint,
                session => session.PutStreamAsync(operationContext, contentHash, stream, operationContext.Token, urgencyHint));
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext,
            HashType hashType,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return MultiLevelWriteAsync(
                operationContext,
                hash: null,
                urgencyHint,
                session => session.PutStreamAsync(operationContext, hashType, stream, operationContext.Token, urgencyHint));
        }

        public Task<PutResult> PutTrustedFileAsync(Context context, ContentHashWithSize contentHashWithSize, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint)
        {
            var operationContext = new OperationContext(context, cts);
            return MultiLevelWriteAsync(
                operationContext,
                contentHashWithSize.Hash,
                urgencyHint,
                session =>
                {
                    return session.PutOrPutTrustedFileAsync(
                        context,
                        contentHashWithSize,
                        path,
                        CoerceRealizationMode(realizationMode, session),
                        cts,
                        urgencyHint);
                });
        }

        public AbsolutePath? TryGetWorkingDirectory(AbsolutePath? pathHint)
        {
            return LocalSession.TryGetWorkingDirectory(pathHint);
        }
    }
}
