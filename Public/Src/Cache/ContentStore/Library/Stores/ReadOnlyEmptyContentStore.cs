// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// ContentStore that is empty and does not accept content. Useful when we want to disable content; specifically, to bypass talking to VSTS/CASaaS when unnecessary.
    /// </summary>
    public class ReadOnlyEmptyContentStore : StartupShutdownBase, IContentStore
    {
        private readonly ContentStoreTracer _tracer = new ContentStoreTracer(nameof(ReadOnlyEmptyContentStore));

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin) => new CreateSessionResult<IReadOnlyContentSession>(new ReadOnlyEmptyContentSession(name));

        /// <inheritdoc />
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin) => new CreateSessionResult<IContentSession>(new ReadOnlyEmptyContentSession(name));

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context) => Task.FromResult(new GetStatsResult(_tracer.GetCounters()));

        /// <inheritdoc />
        Task<DeleteResult> IContentStore.DeleteAsync(Context context, ContentHash contentHash) => Task.FromResult(new DeleteResult(DeleteResult.ResultCode.ContentNotDeleted, $"{nameof(ReadOnlyEmptyContentStore)} cannot contain any content to delete"));

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result) { }
    }

    /// <summary>
    /// ContentSession is empty and does not accept content. Useful when we want to disable content; specifically, to bypass talking to VSTS/CASaaS when unnecessary.
    /// </summary>
    public class ReadOnlyEmptyContentSession : ContentSessionBase
    {
        /// <nodoc />
        public ReadOnlyEmptyContentSession(string name)
            : base(name)
        {
        }

        private const string ErrorMessage = "Unsupported operation.";

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(ReadOnlyEmptyContentSession));

        /// <inheritdoc />
        protected override Task<OpenStreamResult> OpenStreamCoreAsync(OperationContext operationContext, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
            => Task.FromResult(new OpenStreamResult(null)); // Null stream signals failue.

        /// <inheritdoc />
        protected override Task<PinResult> PinCoreAsync(OperationContext operationContext, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
            => Task.FromResult(PinResult.ContentNotFound);

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(OperationContext operationContext, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint, Counter retryCounter, Counter fileCounter)
            => Task.FromResult(contentHashes.Select((hash, i) => Task.FromResult(PinResult.ContentNotFound.WithIndex(i))));

        /// <inheritdoc />
        protected override Task<PlaceFileResult> PlaceFileCoreAsync(OperationContext operationContext, ContentHash contentHash, AbsolutePath path, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
            => Task.FromResult(new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound));

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(OperationContext operationContext, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
            => Task.FromResult(hashesWithPaths.Select((hash, i) => Task.FromResult(new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound).WithIndex(i))));

        /// <inheritdoc />
        protected override Task<PutResult> PutFileCoreAsync(OperationContext operationContext, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
            => Task.FromResult(new PutResult(new BoolResult(ErrorMessage)));

        /// <inheritdoc />
        protected override Task<PutResult> PutFileCoreAsync(OperationContext operationContext, ContentHash contentHash, AbsolutePath path, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
            => Task.FromResult(new PutResult(contentHash, ErrorMessage));

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(OperationContext operationContext, HashType hashType, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
            => Task.FromResult(new PutResult(new BoolResult(ErrorMessage)));

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(OperationContext operationContext, ContentHash contentHash, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
            => Task.FromResult(new PutResult(contentHash, ErrorMessage));
    }
}
