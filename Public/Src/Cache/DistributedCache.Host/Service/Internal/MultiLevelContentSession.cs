// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Core;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.Host.Service.Internal
{
    public class MultiLevelContentSession : ContentSessionBase, IContentSession, IHibernateContentSession
    {
        protected readonly IContentSession LocalSession;
        protected readonly IContentSession BackingSession;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(MultiLevelContentStore));

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiLevelContentSession"/> class.
        /// </summary>
        public MultiLevelContentSession(
            string name,
            IContentSession localSession,
            IContentSession backingSession)
            : base(name)
        {
            Contract.Requires(name != null);
            Contract.Requires(localSession != null);
            Contract.Requires(backingSession != null);

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

        private async Task<TResult> MultiLevelReadAsync<TResult>(
            OperationContext context,
            ContentHash hash,
            Func<IContentSession, Task<TResult>> runAsync)
            where TResult : ResultBase
        {
            var result = await runAsync(LocalSession);
            if (!result.Succeeded)
            {
                var ensureLocalResult = await EnsureLocalAsync(context, hash);
                if (ensureLocalResult.Succeeded)
                {
                    return await runAsync(LocalSession);
                }
            }

            return result;
        }

        private Task<PutResult> EnsureLocalAsync(OperationContext context, ContentHash hash)
        {
            return context.PerformOperationAsync<PutResult>(
                Tracer,
                async () =>
                {
                    var streamResult = await BackingSession.OpenStreamAsync(context, hash, context.Token).ThrowIfFailure();

                    return await LocalSession.PutStreamAsync(context, hash, streamResult.Stream, context.Token);
                });
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
            return MultiLevelReadAsync(
                operationContext,
                contentHash,
                session => session.OpenStreamAsync(operationContext, contentHash, operationContext.Token, urgencyHint));
        }

        /// <inheritdoc />
        protected override Task<PinResult> PinCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            // TODO: decide if we want to pin on the backing session as well. The issue here is as follows: on the use-
            // case we need this for (permanent distributed CASaaS running + local CAS on a different drive with drop
            // as client against distributed), it doesn't really matters what pin does. For the general scenario, it
            // depends.
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
            // TODO: decide if we want to pin on the backing session as well. The issue here is as follows: on the use-
            // case we need this for (permanent distributed CASaaS running + local CAS on a different drive with drop
            // as client against distributed), it doesn't really matters what pin does. For the general scenario, it
            // depends.
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
            return MultiLevelReadAsync(
                operationContext,
                contentHash,
                session => session.PlaceFileAsync(
                    operationContext,
                    contentHash,
                    path,
                    accessMode,
                    replacementMode,
                    realizationMode,
                    operationContext.Token,
                    urgencyHint));
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            // NOTE: Most of the IContentSession implementations throw NotImplementedException, most notably
            // the ReadOnlyServiceClientContentSession which is used to communicate with this session. Given that,
            // it is safe for this method to not be implemented here as well.
            throw new NotImplementedException();
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
            // Backing session may likely be on a different volume. Don't enforce the same rules around FileRealizationMode.
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
            return MultiLevelWriteAsync(session => session.PutStreamAsync(operationContext, hashType, stream, operationContext.Token, urgencyHint));
        }

        private async Task<PutResult> MultiLevelWriteAsync(Func<IContentSession, Task<PutResult>> writeAsync)
        {
            await writeAsync(BackingSession).ThrowIfFailureAsync();
            return await writeAsync(LocalSession);
        }
    }
}
