// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Sessions
{
    /// <summary>
    ///     An IContentSession implemented over a ServiceClientsContentStore.
    /// </summary>
    public class ServiceClientContentSession : ReadOnlyServiceClientContentSession, IContentSession
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ServiceClientContentSession"/> class.
        /// </summary>
        public ServiceClientContentSession(
            string name,
            ImplicitPin implicitPin,
            ILogger logger,
            IAbsFileSystem fileSystem,
            ServiceClientContentSessionTracer sessionTracer,
            ServiceClientContentStoreConfiguration configuration,
            Func<IRpcClient> rpcClientFactory = null)
            : base(name, implicitPin, logger, fileSystem, sessionTracer, configuration, rpcClientFactory)
        {
        }

        /// <inheritdoc />
        protected override async Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext, HashType hashType, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
        {
            // We need a seekable stream, that can give its length. If the input stream is seekable, we can use it directly.
            // Otherwise, we need to create a temp file for this purpose.
            var putStream = stream;
            Stream disposableStream = null;
            if (!stream.CanSeek)
            {
                putStream = TempFileStreamFactory.Create(operationContext, stream);
                disposableStream = putStream;
            }

            using (disposableStream)
            {
                return await PerformRetries(
                    operationContext,
                    () => RpcClient.PutStreamAsync(operationContext, hashType, putStream),
                    retryCounter: retryCounter);
            }
        }

        /// <inheritdoc />
        protected override async Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext, ContentHash contentHash, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
        {
            // We need a seekable stream, that can give its length. If the input stream is seekable, we can use it directly.
            // Otherwise, we need to create a temp file for this purpose.
            var putStream = stream;
            Stream disposableStream = null;
            if (!stream.CanSeek)
            {
                putStream = TempFileStreamFactory.Create(operationContext, stream);
                disposableStream = putStream;
            }

            using (disposableStream)
            {
                return await PerformRetries(
                    operationContext,
                    () => RpcClient.PutStreamAsync(operationContext, contentHash, putStream),
                    retryCounter: retryCounter);
            }
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
            return PerformRetries(
                operationContext,
                () => RpcClient.PutFileAsync(operationContext, hashType, path, realizationMode),
                retryCounter: retryCounter);
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
            return PerformRetries(
                operationContext,
                () => RpcClient.PutFileAsync(operationContext, contentHash, path, realizationMode),
                retryCounter: retryCounter);
        }
    }
}
