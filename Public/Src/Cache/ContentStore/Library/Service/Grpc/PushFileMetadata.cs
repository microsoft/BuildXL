// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using Grpc.Core;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <nodoc />
    internal class PushRequest
    {
        /// <nodoc />
        public ContentHash Hash { get; }

        /// <nodoc />
        public Guid TraceId { get; }

        /// <nodoc />
        public PushRequest(ContentHash hash, Guid traceId)
        {
            Hash = hash;
            TraceId = traceId;
        }

        /// <nodoc />
        public static PushRequest FromMetadata(Metadata metadata)
        {
            byte[] hashBytes = null;
            var hashType = HashType.Unknown;
            Guid traceId = default;

            foreach (var header in metadata)
            {
                switch (header.Key)
                {
                    case "hash-bin": hashBytes = header.ValueBytes; break;
                    case "hash_type": Enum.TryParse(header.Value, out hashType); break;
                    case "trace_id": traceId = new Guid(header.Value); break;
                }
            }

            var hash = new ContentHash(hashType, hashBytes);

            return new PushRequest(hash, traceId);
        }

        /// <nodoc />
        public Metadata GetMetadata()
        {
            return new Metadata()
            {
                { "hash-bin", Hash.ToHashByteArray() }, // -bin suffix required to send bytes directly
                { "hash_type", Hash.HashType.ToString() },
                { "trace_id", TraceId.ToString() }
            };
        }
    }

    /// <nodoc />
    internal class PushResponse
    {
        /// <nodoc />
        public bool ShouldCopy { get; }

        private readonly Lazy<Metadata> _metadata;

        /// <nodoc />
        public Metadata Metadata => _metadata.Value;

        private PushResponse(bool shouldCopy)
        {
            ShouldCopy = shouldCopy;
            _metadata = new Lazy<Metadata>(() => new Metadata { { "should_copy", ShouldCopy.ToString() } });
        }

        /// <nodoc />
        public static PushResponse Copy { get; } = new PushResponse(shouldCopy: true);

        /// <nodoc />
        public static PushResponse DontCopy { get; } = new PushResponse(shouldCopy: false);

        /// <nodoc />
        public static PushResponse FromMetadata(Metadata metadata)
        {
            foreach (var header in metadata)
            {
                if (header.Key == "should_copy")
                {
                    if (bool.TryParse(header.Value, out var shouldCopy))
                    {
                        return shouldCopy ? Copy : DontCopy;
                    }
                }
            }

            return Copy;
        }
    }
}
