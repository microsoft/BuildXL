using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Vfs
{
    /// <todoc />
    public class VirtualizedContentSession : ContentSessionBase
    {
        private IContentSession InnerSession { get; }
        //private VirtualizationRegistry VirtualizationRegistry { get; }
        private VirtualizedContentStore Store { get; }
        protected override Tracer Tracer { get; } = new Tracer(nameof(VirtualizedContentSession));

        public VirtualizedContentSession(VirtualizedContentStore store, IContentSession session, string name)
            : base(name)
        {
            // TODO: ImplicitPin?
            Store = store;
            InnerSession = session;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await InnerSession.StartupAsync(context).ThrowIfFailure();
            return await base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var result = await base.ShutdownCoreAsync(context);

            result &= await InnerSession.ShutdownAsync(context);

            return result;
        }

        /// <inheritdoc />
        protected override Task<OpenStreamResult> OpenStreamCoreAsync(OperationContext operationContext, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return InnerSession.OpenStreamAsync(operationContext, contentHash, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PinResult> PinCoreAsync(OperationContext operationContext, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return InnerSession.PinAsync(operationContext, contentHash, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(OperationContext operationContext, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint, Counter retryCounter, Counter fileCounter)
        {
            return InnerSession.PinAsync(operationContext, contentHashes, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PlaceFileResult> PlaceFileCoreAsync(OperationContext operationContext, ContentHash contentHash, AbsolutePath path, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return InnerSession.PlaceFileAsync(operationContext, contentHash, path, accessMode, replacementMode, realizationMode, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(OperationContext operationContext, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return InnerSession.PlaceFileAsync(operationContext, hashesWithPaths, accessMode, replacementMode, realizationMode, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutFileCoreAsync(OperationContext operationContext, ContentHash contentHash, AbsolutePath path, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return InnerSession.PutFileAsync(operationContext, contentHash, path, realizationMode, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutFileCoreAsync(OperationContext operationContext, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return InnerSession.PutFileAsync(operationContext, hashType, path, realizationMode, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(OperationContext operationContext, ContentHash contentHash, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return InnerSession.PutStreamAsync(operationContext, contentHash, stream, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(OperationContext operationContext, HashType hashType, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return InnerSession.PutStreamAsync(operationContext, hashType, stream, operationContext.Token, urgencyHint);
        }

        //protected override async Task<PlaceFileResult> PlaceFileCoreAsync(
        //    OperationContext operationContext,
        //    ContentHash contentHash,
        //    AbsolutePath path,
        //    FileAccessMode accessMode,
        //    FileReplacementMode replacementMode,
        //    FileRealizationMode realizationMode,
        //    UrgencyHint urgencyHint,
        //    Counter retryCounter)
        //{
        //    // Open stream gets translated into PlaceFile for server/client CAS. Ideally this case would be handled,
        //    // i.e. those calls should be just directed to the underlying CAS. Better still would be to never go through
        //    // the virtual CAS for those calls

        //    // TODO: Handle replacement modes
        //    VirtualizationRegistry.RegisterFile(CreateVirtualPath(path), contentHash, realizationMode);
        //    return new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy);
        //}

        //private VirtualPath CreateVirtualPath(AbsolutePath path)
        //{
        //    throw new NotImplementedException();
        //}

        //protected override async Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(
        //    OperationContext operationContext,
        //    IReadOnlyList<ContentHashWithPath> hashesWithPaths,
        //    FileAccessMode accessMode,
        //    FileReplacementMode replacementMode,
        //    FileRealizationMode realizationMode,
        //    UrgencyHint urgencyHint,
        //    Counter retryCounter)
        //{
        //    return await hashesWithPaths.SelectList((item, index) => (item: item, index: index)).ForEachAsync(
        //        degreeOfParallelism: 8,
        //        async tuple =>
        //        {
        //            var result = await PlaceFileCoreAsync(operationContext, tuple.item.Hash, tuple.item.Path, accessMode, replacementMode, realizationMode, urgencyHint, retryCounter);
        //            return Task.FromResult(result.WithIndex(tuple.index));
        //        }, operationContext.Token);
        //}
    }
}
