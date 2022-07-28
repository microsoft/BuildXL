// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Azure blob implementation of <see cref="IWriteBehindEventStorage"/>
    /// </summary>
    public class BlobWriteBehindEventStorage : BlobEventStorageBase<CheckpointLogId, Stream>, IWriteBehindEventStorage
    {
        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(BlobWriteBehindEventStorage));

        public BlobWriteBehindEventStorage(BlobEventStorageConfiguration configuration)
            : base(configuration)
        {
        }

        private struct SealMetadata
        {
            public CheckpointLogId LogId { get; init; }

            public DateTime CreationTime { get; init; }

            public string TraceId { get; init; }
        }

        /// <inheritdoc />
        public Task<BoolResult> SealAsync(OperationContext context, CheckpointLogId logId)
        {
            var msg = $"LogId=[{logId}]";
            return context.PerformOperationWithTimeoutAsync(Tracer, async context =>
            {
                var blobReference = BlobDirectory.GetBlockBlobReference(ToSealBlobName(logId));

                var seal = new SealMetadata
                {
                    // TODO: it would be good to have some kind of identifier for the checkpoint
                    LogId = logId,
                    CreationTime = DateTime.UtcNow,
                    TraceId = context.TracingContext.TraceId
                };

                var serializedSeal = JsonSerializer.SerializeToUtf8Bytes(seal);
                await blobReference.UploadFromByteArrayAsync(serializedSeal, 0, serializedSeal.Length);

                return BoolResult.Success;
            },
            traceOperationStarted: false,
            timeout: Configuration.StorageInteractionTimeout,
            extraStartMessage: msg,
            extraEndMessage: _ => msg);
        }

        /// <inheritdoc />
        public Task<Result<bool>> IsSealedAsync(OperationContext context, CheckpointLogId logId)
        {
            var msg = $"LogId=[{logId}]";
            return context.PerformOperationWithTimeoutAsync(Tracer, async context =>
            {
                var sealBlob = BlobDirectory.GetBlockBlobReference(ToSealBlobName(logId));
                var isSealed = await sealBlob.ExistsAsync();
                return Result.Success(isSealed);
            },
            traceOperationStarted: false,
            timeout: Configuration.StorageInteractionTimeout,
            extraStartMessage: msg,
            extraEndMessage: _ => msg);
        }

        private static readonly Regex NameRegex = new Regex(@"(?<logId>[0-9]+)\.(?<extension>bin|seal)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <inheritdoc />
        protected override bool TryParseName(string name, out CheckpointLogId logId)
        {
            var match = NameRegex.Match(name);
            if (!match.Success)
            {
                logId = default;
                return false;
            }

            var logIdGroup = match.Groups["logId"];
            Contract.Assert(logIdGroup.Success);

            var extensionGroup = match.Groups["extension"];
            Contract.Assert(extensionGroup.Success);

            logId = new CheckpointLogId(ParseInt(logIdGroup.Value));
            return true;
        }

        /// <inheritdoc />
        protected override string ToAppendBlobName(CheckpointLogId checkpointLogId)
        {
            return $"{Pad(checkpointLogId.Value)}.bin";
        }

        private string ToSealBlobName(CheckpointLogId checkpointLogId)
        {
            return $"{Pad(checkpointLogId.Value)}.seal";
        }

        /// <inheritdoc />
        protected override Stream AsStream(Stream data)
        {
            return data;
        }

        /// <inheritdoc />
        protected override Stream FromStream(MemoryStream stream)
        {
            return stream;
        }

        /// <inheritdoc />
        protected override CheckpointLogId ToKey(BlockReference blockReference)
        {
            return blockReference.LogId;
        }
    }
}
