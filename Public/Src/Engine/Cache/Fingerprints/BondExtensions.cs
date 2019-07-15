// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using Bond.IO.Unsafe;
using Bond.Protocols;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Serialization;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// Extension methods for <see cref="BondContentHash"/> conversions.
    /// </summary>
    public static class BondExtensions
    {
        /// <nodoc />
        [Pure]
        public static BondContentHash ToBondContentHash(in this ContentHash contentHash)
        {
            var bondHash = new BondContentHash
            {
                Data = new ArraySegment<byte>(contentHash.ToHashByteArray()),
            };

            return bondHash;
        }

        /// <nodoc />
        public static ContentHash ToContentHash(this BondContentHash bondHash)
        {
            Contract.Requires(bondHash != null);

            ArraySegment<byte> arraySegment = bondHash.Data;
            return ContentHashingUtilities.CreateFrom(arraySegment.Array, arraySegment.Offset);
        }

        /// <nodoc />
        [Pure]
        public static BondFingerprint ToBondFingerprint(in this Fingerprint fingerprint)
        {
            var bondFingerprint = new BondFingerprint
            {
                Data = new ArraySegment<byte>(fingerprint.ToByteArray()),
            };

            return bondFingerprint;
        }

        /// <nodoc />
        public static Fingerprint ToFingerprint(this BondFingerprint bondFingerprint)
        {
            Contract.Requires(bondFingerprint != null);

            ArraySegment<byte> arraySegment = bondFingerprint.Data;
            return FingerprintUtilities.CreateFrom(arraySegment);
        }

        /// <nodoc />
        public static ArraySegment<byte> Serialize<T>(T valueToSerialize)
        {
            OutputBuffer valueBuffer = new OutputBuffer(1024);
            CompactBinaryWriter<OutputBuffer> writer = new CompactBinaryWriter<OutputBuffer>(valueBuffer);
            Bond.Serialize.To(writer, valueToSerialize);
            return valueBuffer.Data;
        }

        /// <nodoc />
        public static T Deserialize<T>(ArraySegment<byte> blob)
        {
            InputBuffer input = new InputBuffer(blob);
            CompactBinaryReader<InputBuffer> reader = new CompactBinaryReader<InputBuffer>(input);
            return Bond.Deserialize<T>.From(reader);
        }

        /// <summary>
        /// Bond-serializes a given <typeparamref name="T"/> and hashes the result.
        /// </summary>
        public static async Task<Possible<ContentHash>> TrySerializeAndStoreContent<T>(
            T valueToSerialize,
            Func<ContentHash, ArraySegment<byte>, Task<Possible<Unit>>> storeAsync,
            BoxRef<long> contentSize = null)
        {
            var valueBuffer = Serialize(valueToSerialize);

            if (contentSize != null)
            {
                contentSize.Value = valueBuffer.Count;
            }

            ContentHash valueHash = ContentHashingUtilities.HashBytes(
                valueBuffer.Array,
                valueBuffer.Offset,
                valueBuffer.Count);

            Possible<Unit> maybeStored = await storeAsync(valueHash, valueBuffer);
            if (!maybeStored.Succeeded)
            {
                return maybeStored.Failure;
            }

            return valueHash;
        }

        /// <nodoc />
        [Pure]
        public static BondFileMaterializationInfo ToBondFileMaterializationInfo(in this FileMaterializationInfo info, PathTable pathTable)
        {
            return new BondFileMaterializationInfo()
            {
                Hash = info.Hash.ToBondContentHash(),
                Length = info.FileContentInfo.SerializedLengthAndExistence,
                FileName = info.FileName.IsValid ?
                    info.FileName.ToString(pathTable.StringTable) : null,
                ReparsePointType = info.ReparsePointInfo.ReparsePointType.ToBondReparsePointType(),
                ReparsePointTarget = info.ReparsePointInfo.GetReparsePointTarget()
            };
        }

        /// <nodoc />
        public static FileMaterializationInfo ToFileMaterializationInfo(this BondFileMaterializationInfo bondInfo, PathTable pathTable)
        {
            Contract.Requires(bondInfo != null);

            return new FileMaterializationInfo(
                new FileContentInfo(bondInfo.Hash.ToContentHash(), FileContentInfo.LengthAndExistence.Deserialize(bondInfo.Length)),
                bondInfo.FileName != null ? PathAtom.Create(pathTable.StringTable, bondInfo.FileName) : PathAtom.Invalid,
                ReparsePointInfo.Create(bondInfo.ReparsePointType.ToReparsePointType(), bondInfo.ReparsePointTarget));
        }

        /// <nodoc />
        public static BondReparsePointType ToBondReparsePointType(this ReparsePointType reparsePointType)
        {
            switch (reparsePointType)
            {
                case ReparsePointType.None:
                    return BondReparsePointType.None;
                case ReparsePointType.SymLink:
                    return BondReparsePointType.SymLink;
                case ReparsePointType.MountPoint:
                    return BondReparsePointType.MountPoint;
                case ReparsePointType.NonActionable:
                    return BondReparsePointType.NonActionable;
                default:
                    throw Contract.AssertFailure("Cannot convert ReparsePointType to BondReparsePointType");
            }
        }

        /// <nodoc />
        public static ReparsePointType ToReparsePointType(this BondReparsePointType reparsePointType)
        {
            switch (reparsePointType)
            {
                case BondReparsePointType.None:
                    return ReparsePointType.None;
                case BondReparsePointType.SymLink:
                    return ReparsePointType.SymLink;
                case BondReparsePointType.MountPoint:
                    return ReparsePointType.MountPoint;
                case BondReparsePointType.NonActionable:
                    return ReparsePointType.NonActionable;
                default:
                    throw Contract.AssertFailure("Cannot convert BondReparsePointType to ReparsePointType");
            }
        }

        /// <nodoc />
        public static T DeserializeDedup<T>(InliningReader buildXLReader)
        {
            CompactBinaryReader<InliningReaderDedupInputStream> reader = new CompactBinaryReader<InliningReaderDedupInputStream>(new InliningReaderDedupInputStream(buildXLReader));
            return Bond.Deserialize<T>.From(reader);
        }

        /// <nodoc />
        public static void SerializeDedup<T>(InliningWriter buildXLWriter, T valueToSerialize)
        {
            CompactBinaryWriter<InliningWriterDedupOutputStream> writer = new CompactBinaryWriter<InliningWriterDedupOutputStream>(new InliningWriterDedupOutputStream(buildXLWriter));
            Bond.Serialize.To(writer, valueToSerialize);
        }
    }
}
