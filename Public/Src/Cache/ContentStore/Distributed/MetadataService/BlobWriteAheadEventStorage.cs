// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text.RegularExpressions;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Azure blob implementation of <see cref="IWriteAheadEventStorage"/>
    /// </summary>
    public class BlobWriteAheadEventStorage : BlobEventStorageBase<BlockReference, ReadOnlyMemory<byte>>, IWriteAheadEventStorage
    {
        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(BlobWriteAheadEventStorage));

        public BlobWriteAheadEventStorage(BlobEventStorageConfiguration configuration)
            : base(configuration)
        {
        }

        private static readonly Regex NameRegex = new Regex(@"(?<logId>[0-9]+)_(?<blockId>[0-9]+)\.bin", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <inheritdoc />
        protected override bool TryParseName(string name, out BlockReference cursor)
        {
            var match = NameRegex.Match(name);
            if (!match.Success)
            {
                cursor = default;
                return false;
            }

            var logIdGroup = match.Groups["logId"];
            var blockIdGroup = match.Groups["blockId"];

            var logId = new CheckpointLogId(ParseInt(logIdGroup.Value));
            var blockId = ParseInt(blockIdGroup.Value);
            cursor = (logId, blockId);
            return true;
        }

        /// <inheritdoc />
        protected override BlockReference ToKey(BlockReference blockReference)
        {
            return blockReference;
        }

        /// <inheritdoc />
        protected override Stream AsStream(ReadOnlyMemory<byte> data)
        {
            return data.AsMemoryStream(out _);
        }

        /// <inheritdoc />
        protected override ReadOnlyMemory<byte> FromStream(MemoryStream stream)
        {
            return stream.AsReadOnlyMemory();
        }

        /// <inheritdoc />
        protected override string ToAppendBlobName(BlockReference cursor)
        {
            return $"{Pad(cursor.LogId.Value)}_{Pad(cursor.LogBlockId)}.bin";
        }
    }
}
