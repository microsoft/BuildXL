// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using ContentStore.Grpc;
using Google.Protobuf;
using Google.Protobuf.Collections;

namespace BuildXL.Cache.MemoizationStore.Service
{
    /// <summary>
    /// Helper class for serializing/deserializing objects send/receive over GRPC.
    /// </summary>
    public static class GrpcDataConverter
    {
        /// <nodoc />
        public static StrongFingerprint FromGrpc(this StrongFingerprintData requestFingerprint)
        {
            return new StrongFingerprint(
                DeserializeFingerprintFromGrpc(requestFingerprint.WeakFingerprint),
                requestFingerprint.Selector.FromGrpc());
        }

        /// <nodoc />
        public static StrongFingerprintData ToGrpc(this StrongFingerprint input)
        {
            return new StrongFingerprintData()
                   {
                       Selector = input.Selector.ToGrpc(),
                       WeakFingerprint = input.WeakFingerprint.ToByteString(),
                   };
        }

        /// <nodoc />
        public static Selector FromGrpc(this SelectorData request)
        {
            return new Selector(request.ContentHash.FromGrpc(), request.Output.ToByteArray());
        }

        /// <nodoc />
        public static SelectorData ToGrpc(this Selector input)
        {
            return new SelectorData
                   {
                        ContentHash = input.ContentHash.ToGrpc(),
                        Output = input.Output.ToByteString(),
                   };
        }

        /// <nodoc />
        public static ContentHashAndHashTypeData ToGrpc(this ContentHash input)
        {
            return new ContentHashAndHashTypeData()
                   {
                       ContentHash = input.ToByteString(),
                       HashType = (int)input.HashType,
                   };
        }

        /// <nodoc />
        public static ContentHash FromGrpc(this ContentHashAndHashTypeData data)
        {
            return data.ContentHash.ToContentHash((HashType)data.HashType);
        }

        /// <nodoc />
        public static ContentHashListWithDeterminismData ToGrpc(this in ContentHashListWithDeterminism input)
        {
            return new ContentHashListWithDeterminismData()
                   {
                       CacheDeterminism = input.Determinism.ToGrpc(),
                       ContentHashList = input.ContentHashList.ToGrpc(),
                   };
        }

        /// <nodoc />
        public static ContentHashListWithDeterminism FromGrpc(this ContentHashListWithDeterminismData input)
        {
            ContentHashList contentHashList = input.ContentHashList.FromGrpc();
            CacheDeterminism determinism = FromGrpc(input.CacheDeterminism);

            return new ContentHashListWithDeterminism(contentHashList, determinism);
        }

        /// <nodoc />
        public static Fingerprint DeserializeFingerprintFromGrpc(this ByteString data)
        {
            return new Fingerprint(data.ToByteArray());
        }

        /// <nodoc />
        public static StrongFingerprint[] FromGrpc(StrongFingerprintData[] data)
        {
            return data.Select(f => FromGrpc(f)).ToArray();
        }

        /// <nodoc />
        public static Result<LevelSelectors> FromGrpc(this GetSelectorsResponse input)
        {
            Contract.Assert(input.Header.Succeeded);
            var selectors = input.Selectors.Select(s => s.FromGrpc()).ToArray();
            return Result.Success(new LevelSelectors(selectors, input.HasMore));
        }

        /// <nodoc />
        public static ByteString FromGrpc(Fingerprint weakFingerprint)
        {
            return weakFingerprint.ToByteString();
        }

        /// <nodoc />
        public static GetContentHashListResult FromGrpc(this GetContentHashListResponse input)
        {
            Contract.Assert(input.Header.Succeeded);
            return new GetContentHashListResult(FromGrpc(input.HashList));
        }

        /// <nodoc />
        public static AddOrGetContentHashListResult FromGrpc(this AddOrGetContentHashListResponse input)
        {
            return new AddOrGetContentHashListResult((AddOrGetContentHashListResult.ResultCode)input.Header.Result, input.HashList.FromGrpc());
        }

        /// <nodoc />
        public static ContentHashList FromGrpc(this ContentHashListData input)
        {
            if (input.Payload.IsEmpty && input.ContentHashes.Count == 0)
            {
                // Special case: if all the fields are empty, the result is null.
                return null;
            }

            return new ContentHashList(
                input.ContentHashes.Select(ch => ch.FromGrpc()).ToArray(),
                // Grpc does not support passing null values over the wire.
                // To work-around this issue, the null payload is serialized as an empty array.
                // But to preserve equality we need to use a special logic and return null if the payload is empty.
                input.Payload.DeserializePayload());
        }

        /// <nodoc />
        public static byte[] DeserializePayload(this ByteString input)
        {
            if (input.IsEmpty)
            {
                return null;
            }

            return input.ToByteArray();
        }

        /// <nodoc />
        public static ContentHashListData ToGrpc(this ContentHashList input)
        {
            var result = new ContentHashListData();
            if (input != null)
            {
                foreach (var hash in input.Hashes)
                {
                    result.ContentHashes.Add(hash.ToGrpc());
                }

                result.Payload = input.Payload.ToByteString();
            }

            return result;
        }

        /// <nodoc />
        public static CacheDeterminism FromGrpc(this CacheDeterminismData input)
        {
            return CacheDeterminism.ViaCache(
                new Guid(input.Guid.ToByteArray()),
                DateTime.SpecifyKind(new DateTime(input.ExpirationUtc), DateTimeKind.Utc));
        }

        /// <nodoc />
        public static CacheDeterminismData ToGrpc(this CacheDeterminism determinism)
        {
            return new CacheDeterminismData
                   {
                       ExpirationUtc = determinism.ExpirationUtc.Ticks,
                       Guid = determinism.Guid.ToByteArray().ToByteString()
                   };
        }

        /// <nodoc />
        public static RepeatedField<ByteString> FromGrpc(ContentHashList contentHashList)
        {
            var result = new RepeatedField<ByteString>();

            foreach (var element in contentHashList.Hashes)
            {
                result.Add(element.ToByteString());
            }

            return result;
        }
    }
}
