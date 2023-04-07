// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.Interfaces;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using Google.Protobuf;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// Extension methods for Cache Fingerprints Grpc conversions.
    /// </summary>
    public static class CacheGrpcExtensions
    {
        /// <nodoc />
        public static ContentHash ToContentHash(this ByteString hash)
        {
            Contract.Requires(hash != null);

            return ContentHashingUtilities.CreateFrom(hash.ToByteArray());
        }

        /// <nodoc />
        public static ArraySegment<byte> Serialize<T>(T valueToSerialize) where T : IMessage<T>
        {
            return new ArraySegment<byte>(valueToSerialize.ToByteArray());
        }

        /// <nodoc />
        public static Possible<T> Deserialize<T>(ArraySegment<byte> blob) where T : IMessage<T>, new()
        {
            try
            {
                MessageParser<T> parser = new MessageParser<T>(() => new T());
                return parser.ParseFrom(blob);
            }
            catch (Exception e)
            {
                return new Possible<T>(new Failure<string>(e.ToString()));
            }
        }

        /// <nodoc />
        public static T Deserialize<T>(ByteString data) where T : IMessage<T>, new()
        {
            MessageParser<T> parser = new MessageParser<T>(() => new T());
            return parser.ParseFrom(data);

        }

        /// <summary>
        /// Deserialize a protobuf message from a stream. Must call this function inside a try/catch block.
        /// </summary>
        /// <exception cref="Google.Protobuf.InvalidProtocolBufferException"/>
        public static T Deserialize<T>(Stream stream) where T : IMessage<T>, new()
        {
            MessageParser<T> parser = new MessageParser<T>(() => new T());
            return parser.ParseFrom(stream);
        }

        /// <summary>
        /// Protobuf-serializes a given <typeparamref name="T"/> and hashes the result.
        /// </summary>
        public static async Task<Possible<ContentHash>> TrySerializeAndStoreContent<T>(
            T valueToSerialize,
            Func<ContentHash, ArraySegment<byte>, Task<Possible<Unit>>> storeAsync,
            BoxRef<long> contentSize = null) where T : IMessage<T>
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
        public static GrpcFileMaterializationInfo ToGrpcFileMaterializationInfo(in this FileMaterializationInfo info, PathTable pathTable)
        {
            return new GrpcFileMaterializationInfo()
            {
                Hash = info.Hash.ToByteString(),
                Length = info.FileContentInfo.SerializedLengthAndExistence,
                FileName = info.FileName.IsValid ?
                    info.FileName.ToString(pathTable.StringTable) : null,
                ReparsePointType = info.ReparsePointInfo.ReparsePointType.ToGrpcReparsePointType(),
                ReparsePointTarget = info.ReparsePointInfo.GetReparsePointTarget(),
                IsAllowedFileRewrite = info.IsUndeclaredFileRewrite,
                IsExecutable = info.IsExecutable
            };
        }

        /// <nodoc />
        public static FileMaterializationInfo ToFileMaterializationInfo(this GrpcFileMaterializationInfo grpcInfo, PathTable pathTable)
        {
            Contract.Requires(grpcInfo != null);

            return new FileMaterializationInfo(
                new FileContentInfo(grpcInfo.Hash.ToContentHash(), FileContentInfo.LengthAndExistence.Deserialize(grpcInfo.Length)),
                grpcInfo.FileName != null ? PathAtom.Create(pathTable.StringTable, grpcInfo.FileName) : PathAtom.Invalid,
                ReparsePointInfo.Create(grpcInfo.ReparsePointType.ToReparsePointType(), grpcInfo.ReparsePointTarget),
                grpcInfo.IsAllowedFileRewrite, grpcInfo.IsExecutable);
        }

        /// <nodoc />
        public static GrpcReparsePointType ToGrpcReparsePointType(this ReparsePointType reparsePointType)
        {
            switch (reparsePointType)
            {
                case ReparsePointType.None:
                    return GrpcReparsePointType.None;
                case ReparsePointType.FileSymlink:
                    return GrpcReparsePointType.FileSymlink;
                case ReparsePointType.DirectorySymlink:
                    return GrpcReparsePointType.DirectorySymlink;
                case ReparsePointType.UnixSymlink:
                    return GrpcReparsePointType.UnixSymlink;
                case ReparsePointType.Junction:
                    return GrpcReparsePointType.Junction;
                case ReparsePointType.NonActionable:
                    return GrpcReparsePointType.NonActionable;
                default:
                    throw Contract.AssertFailure("Cannot convert ReparsePointType to GrpcReparsePointType");
            }
        }

        /// <nodoc />
        public static ReparsePointType ToReparsePointType(this GrpcReparsePointType reparsePointType)
        {
            switch (reparsePointType)
            {
                case GrpcReparsePointType.None:
                    return ReparsePointType.None;
                case GrpcReparsePointType.FileSymlink:
                    return ReparsePointType.FileSymlink;
                case GrpcReparsePointType.DirectorySymlink:
                    return ReparsePointType.DirectorySymlink;
                case GrpcReparsePointType.UnixSymlink:
                    return ReparsePointType.UnixSymlink;
                case GrpcReparsePointType.Junction:
                    return ReparsePointType.Junction;
                case GrpcReparsePointType.NonActionable:
                    return ReparsePointType.NonActionable;
                default:
                    throw Contract.AssertFailure("Cannot convert GrpcReparsePointType to ReparsePointType");
            }
        }
    }
}
