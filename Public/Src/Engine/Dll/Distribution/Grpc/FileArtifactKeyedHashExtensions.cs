// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Utilities.Core;
using static BuildXL.Distribution.Grpc.FileArtifactKeyedHash.Types;

namespace BuildXL.Distribution.Grpc
{
    internal static class FileArtifactKeyedHashExtensions
    {
        /// <summary>
        /// Gets the file artifact
        /// </summary>
        public static FileArtifact GetFileArtifact(this FileArtifactKeyedHash f)
        {
            return new FileArtifact(new AbsolutePath(f.PathValue), f.RewriteCount);
        }

        public static void SetFileArtifact(this FileArtifactKeyedHash f, FileArtifact fa)
        {
            f.PathValue = fa.Path.Value.Value;
            f.RewriteCount = fa.RewriteCount;
        }

        /// <nodoc/>
        public static FileArtifactKeyedHash SetFileMaterializationInfo(this FileArtifactKeyedHash f, PathTable pathTable, FileMaterializationInfo info)
        {
            // We are really careful below to not set anything to null,
            // as in that ase gRPC will makes us crash with an assertion failure

            f.Length = info.FileContentInfo.SerializedLengthAndExistence;
            f.ContentHash = info.Hash.ToByteString();
            
            if (info.FileName.IsValid)
            {
                var fileName = info.FileName.ToString(pathTable.StringTable);
                if (fileName is not null)
                {
                    f.FileName = fileName;
                }
            }

            f.ReparsePointType = info.ReparsePointInfo.ReparsePointType.ToGrpcReparsePointType();
            
            var reparsePointTarget = info.ReparsePointInfo.GetReparsePointTarget();
            if (reparsePointTarget is not null)
            {
                f.ReparsePointTarget = reparsePointTarget;
            }

            f.IsAllowedFileRewrite = info.IsUndeclaredFileRewrite;
            f.IsExecutable = info.IsExecutable;

            return f;
        }

        /// <nodoc/>
        public static FileMaterializationInfo GetFileMaterializationInfo(this FileArtifactKeyedHash f, PathTable pathTable)
        {
            return new FileMaterializationInfo(
                new FileContentInfo(ContentHashingUtilities.FromSpan(f.ContentHash.Span), FileContentInfo.LengthAndExistence.Deserialize(f.Length)),
                !string.IsNullOrEmpty(f.FileName) ? PathAtom.Create(pathTable.StringTable, f.FileName) : PathAtom.Invalid,
                ReparsePointInfo.Create(f.ReparsePointType.ToReparsePointType(), f.ReparsePointTarget),
                f.IsAllowedFileRewrite,
                f.IsExecutable);
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