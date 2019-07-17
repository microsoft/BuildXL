// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions.Internal;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Sessions
{
    /// <summary>
    ///     An IContentSession implemented over an IContentStoreInternal
    /// </summary>
    public class FileSystemContentSession : ReadOnlyFileSystemContentSession, ITrustedContentSession, IDecoratedStreamContentSession
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="FileSystemContentSession" /> class.
        /// </summary>
        public FileSystemContentSession(string name, ImplicitPin implicitPin, IContentStoreInternal store)
            : base(name, store, implicitPin)
        {
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
            return Store.PutFileAsync(operationContext, path, realizationMode, hashType, MakePinRequest(ImplicitPin.Put));
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
            return Store.PutFileAsync(operationContext, path, realizationMode, contentHash, MakePinRequest(ImplicitPin.Put));
        }

        /// <inheritdoc />
        public Task<PutResult> PutTrustedFileAsync(
            Context context,
            ContentHashWithSize contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return Store.PutTrustedFileAsync(context, path, realizationMode, contentHash, MakePinRequest(ImplicitPin.Put));
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext,
            HashType hashType,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return Store.PutStreamAsync(operationContext, stream, hashType, MakePinRequest(ImplicitPin.Put));
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return Store.PutStreamAsync(operationContext, stream, contentHash, MakePinRequest(ImplicitPin.Put));
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(Context context, AbsolutePath path, HashType hashType, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint, Func<Stream, Stream> wrapStream)
        {
            return Store.PutFileAsync(context, path, hashType, realizationMode,  wrapStream, MakePinRequest(ImplicitPin.Put));
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(Context context, AbsolutePath path, ContentHash contentHash, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint, Func<Stream, Stream> wrapStream)
        {
            return Store.PutFileAsync(context, path, contentHash, realizationMode, wrapStream, MakePinRequest(ImplicitPin.Put));
        }
    }
}
