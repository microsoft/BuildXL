// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
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
            byte[]? hashBytes = null;
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

            Contract.Check(hashBytes != null)?.Assert($"Can not create PushRequest instance from metadata because 'hash-bin' key is missing. Known keys: {string.Join(", ", metadata.Select(k => k.Key))}");
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

        public RejectionReason Rejection { get; }

        private readonly Lazy<Metadata> _metadata;

        /// <nodoc />
        public Metadata Metadata => _metadata.Value;

        private PushResponse(bool shouldCopy, RejectionReason rejection)
        {
            ShouldCopy = shouldCopy;
            Rejection = rejection;

            _metadata = new Lazy<Metadata>(() => new Metadata
            {
                { "should_copy", ShouldCopy.ToString() },
                { "rejection_reason", ((int)rejection).ToString() }
            });
        }

        /// <nodoc />
        public static PushResponse Copy { get; } = new PushResponse(shouldCopy: true, RejectionReason.Accepted);

        /// <nodoc />
        public static PushResponse DoNotCopy(RejectionReason reason) => PushReasonseByRejection[reason];

        private static readonly Dictionary<RejectionReason, PushResponse> PushReasonseByRejection;

        static PushResponse()
        {
            PushReasonseByRejection = new Dictionary<RejectionReason, PushResponse>();
            foreach (var rejection in Enum.GetValues(typeof(RejectionReason)).Cast<RejectionReason>())
            {
                PushReasonseByRejection[rejection] = new PushResponse(shouldCopy: false, rejection);
            }
        }

        /// <nodoc />
        public static PushResponse FromMetadata(Metadata metadata)
        {
            var shouldCopy = true;
            var rejection = RejectionReason.Accepted;

            foreach (var header in metadata)
            {
                if (header.Key == "should_copy")
                {
                    bool.TryParse(header.Value, out shouldCopy);
                }
                else if (header.Key == "rejection_reason")
                {
                    if (int.TryParse(header.Value, out var rejectionInt))
                    {
                        if (Enum.IsDefined(typeof(RejectionReason), rejectionInt))
                        {
                            rejection = (RejectionReason)rejectionInt;
                        }
                    }
                }
            }

            return shouldCopy ? Copy : DoNotCopy(rejection);
        }
    }
}
