// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Sessions
{
    /// <summary>
    ///     An IContentSession implemented over a ServiceClientsContentStore.
    /// </summary>
    public class ServiceClientContentSession : ReadOnlyServiceClientContentSession, IContentSession
    {
        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(ServiceClientContentSession));

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
            Func<IRpcClient>? rpcClientFactory = null)
            : base(name, implicitPin, logger, fileSystem, sessionTracer, configuration, rpcClientFactory)
        {
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext, HashType hashType, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return PutStreamCoreAsync(
                operationContext,
                stream,
                retryCounter,
                args => RpcClient.PutStreamAsync(operationContext, hashType, args.putStream, args.createDirectory, urgencyHint));
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext, ContentHash contentHash, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return PutStreamCoreAsync(
                operationContext,
                stream,
                retryCounter,
                args => RpcClient.PutStreamAsync(operationContext, contentHash, args.putStream, args.createDirectory, urgencyHint));
        }

        private async Task<PutResult> PutStreamCoreAsync(OperationContext operationContext, Stream stream, Counter retryCounter, Func<(Stream putStream, bool createDirectory), Task<PutResult>> putStreamAsync)
        {
            // We need a seekable stream, that can give its length. If the input stream is seekable, we can use it directly.
            // Otherwise, we need to create a temp file for this purpose.
            var putStream = stream;
            Stream? disposableStream = null;
            if (!stream.CanSeek)
            {
                putStream = TempFileStreamFactory.Create(operationContext, stream);
                disposableStream = putStream;
            }

            bool createDirectory = false;
            using (disposableStream)
            {
                return await PerformRetries(
                    operationContext,
                    () => putStreamAsync((putStream, createDirectory)),
                    onRetry: r => createDirectory = true,
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
                () => RpcClient.PutFileAsync(operationContext, hashType, path, realizationMode, urgencyHint),
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
                () => RpcClient.PutFileAsync(operationContext, contentHash, path, realizationMode, urgencyHint),
                retryCounter: retryCounter);
        }
    }
}
