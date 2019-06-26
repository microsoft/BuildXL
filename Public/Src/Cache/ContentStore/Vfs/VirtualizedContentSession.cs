// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
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
    internal class VirtualizedContentSession : ContentSessionBase
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(VirtualizedContentSession));

        private readonly IContentSession _innerSession;
        private readonly VirtualizedContentStore _store;
        private readonly PassThroughFileSystem _fileSystem;
        private readonly VfsContentManager _contentManager;

        public VirtualizedContentSession(VirtualizedContentStore store, IContentSession session, VfsContentManager contentManager, string name)
            : base(name)
        {
            _store = store;
            _innerSession = session;
            _contentManager = contentManager;
            _fileSystem = new PassThroughFileSystem();
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _innerSession.StartupAsync(context).ThrowIfFailure();
            return await base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var result = await base.ShutdownCoreAsync(context);
            result &= await _innerSession.ShutdownAsync(context);
            return result;
        }

        /// <inheritdoc />
        protected override Task<OpenStreamResult> OpenStreamCoreAsync(OperationContext operationContext, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return _innerSession.OpenStreamAsync(operationContext, contentHash, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PinResult> PinCoreAsync(OperationContext operationContext, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return _innerSession.PinAsync(operationContext, contentHash, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(OperationContext operationContext, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint, Counter retryCounter, Counter fileCounter)
        {
            return _innerSession.PinAsync(operationContext, contentHashes, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected async override Task<PlaceFileResult> PlaceFileCoreAsync(OperationContext operationContext, ContentHash contentHash, AbsolutePath path, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
        {
            if (replacementMode != FileReplacementMode.ReplaceExisting && _fileSystem.FileExists(path))
            {
                if (replacementMode == FileReplacementMode.SkipIfExists)
                {
                    return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
                }
                else if (replacementMode == FileReplacementMode.FailIfExists)
                {
                    return new PlaceFileResult(
                        PlaceFileResult.ResultCode.Error,
                        $"File exists at destination {path} with FailIfExists specified");
                }
            }

            var virtualPath = _contentManager.ToVirtualPath(path);
            if (virtualPath == null)
            {
                return await _innerSession.PlaceFileAsync(operationContext, contentHash, path, accessMode, replacementMode, realizationMode, operationContext.Token, urgencyHint);
            }

            _contentManager.Tree.AddFileNode(virtualPath, new VfsFilePlacementData(contentHash, realizationMode, accessMode));
            return new PlaceFileResult(GetPlaceResultCode(realizationMode, accessMode), fileSize: -1 /* Unknown */);
        }

        private PlaceFileResult.ResultCode GetPlaceResultCode(FileRealizationMode realizationMode, FileAccessMode accessMode)
        {
            if (realizationMode == FileRealizationMode.Copy
                || realizationMode == FileRealizationMode.CopyNoVerify
                || accessMode == FileAccessMode.Write)
            {
                return PlaceFileResult.ResultCode.PlacedWithCopy;
            }

            return PlaceFileResult.ResultCode.PlacedWithHardLink;
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(OperationContext operationContext, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
        {
            // NOTE: Most of the IContentSession implementations throw NotImplementedException, most notably
            // the ReadOnlyServiceClientContentSession which is used to communicate with this session. Given that,
            // it is safe for this method to not be implemented here as well.
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutFileCoreAsync(OperationContext operationContext, ContentHash contentHash, AbsolutePath path, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return _innerSession.PutFileAsync(operationContext, contentHash, path, realizationMode, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutFileCoreAsync(OperationContext operationContext, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return _innerSession.PutFileAsync(operationContext, hashType, path, realizationMode, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(OperationContext operationContext, ContentHash contentHash, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return _innerSession.PutStreamAsync(operationContext, contentHash, stream, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(OperationContext operationContext, HashType hashType, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return _innerSession.PutStreamAsync(operationContext, hashType, stream, operationContext.Token, urgencyHint);
        }
    }
}
