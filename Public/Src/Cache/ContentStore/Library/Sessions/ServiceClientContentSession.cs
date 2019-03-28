// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tracing;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;

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
        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext, HashType hashType, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
        {
            // We need a seekable stream, that can give its length. If the input stream is seekable, we can use it directly.
            // Otherwise, we need to create a temp file for this purpose.
            Stream putStream;
            long position;
            Stream disposableStream;

            if (stream.CanSeek)
            {
                putStream = stream;
                position = stream.Position;
                disposableStream = null;
            }
            else
            {
                putStream = TempFileStreamFactory.Create(operationContext, stream);
                position = 0;
                disposableStream = putStream;
            }

            using (disposableStream)
            {
                return PerformRetries(
                    operationContext,
                    () => RpcClient.PutStreamAsync(operationContext, hashType, stream),
                    retryCounter: retryCounter);
            }
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext, ContentHash contentHash, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
        {
            // We need a seekable stream, that can give its length. If the input stream is seekable, we can use it directly.
            // Otherwise, we need to create a temp file for this purpose.
            Stream putStream;
            long position;
            Stream disposableStream;

            if (stream.CanSeek)
            {
                putStream = stream;
                position = stream.Position;
                disposableStream = null;
            }
            else
            {
                putStream = TempFileStreamFactory.Create(operationContext, stream);
                position = 0;
                disposableStream = putStream;
            }

            using (disposableStream)
            {
                return PerformRetries(
                    operationContext,
                    () => RpcClient.PutStreamAsync(operationContext, contentHash, stream),
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
