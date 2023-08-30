// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Storage
{
    /// <summary>
    /// A <see cref="FileMaterializationInfo"/> is the combination of a file's known content hash and file name.
    /// </summary>
    public readonly struct FileMaterializationInfo : IEquatable<FileMaterializationInfo>
    {
        /// <summary>
        /// The file name of the file
        /// </summary>
        public readonly PathAtom FileName;

        /// <summary>
        /// Underlying <see cref="FileContentInfo"/> (hash and length of the corresponding file).
        /// </summary>
        public readonly FileContentInfo FileContentInfo;

        /// <summary>
        /// Checks whether reparse points is actionable, i.e., a mount point or a symlink.
        /// </summary>
        public bool IsReparsePointActionable => ReparsePointInfo.ReparsePointType.IsActionable();

        /// <summary>
        /// Checks whether the file's content should be cached. Returns true if the file is neither 
        /// a symlink / mount point <see cref="IsReparsePointActionable"/> nor a special-case hash.
        /// </summary>
        public bool IsCacheable => !IsReparsePointActionable && !Hash.IsSpecialValue();

        /// <summary>
        /// Underlying <see cref="ReparsePointInfo"/> (type and target (if available) of the reparse point).
        /// </summary>
        public readonly ReparsePointInfo ReparsePointInfo;

        /// <summary>
        /// Whether the file represents an allowed source rewrite
        /// </summary>
        public readonly bool IsUndeclaredFileRewrite;

        /// <summary>
        /// Whether the file has execution permission for the owner
        /// </summary>
        /// <remarks>
        /// Only valid in linux/mac OSs
        /// </remarks>
        public readonly bool IsExecutable;

        /// <summary>
        /// For dynamic outputs, the case preserving relative directory of the output (relative to the output directory root).
        /// </summary>
        /// <remarks>
        /// Only valid for dynamic outputs and when preserve directory casing is enabled. When combined with <see cref="FileName"/>, it represents the relative path of the output
        /// </remarks>
        public readonly RelativePath DynamicOutputCaseSensitiveRelativeDirectory;

        /// <summary>
        /// For dynamic outputs, the opaque directory root of the output. Invalid for non-dynamic outputs
        /// </summary>
        public readonly AbsolutePath OpaqueDirectoryRoot;

        /// <summary>
        /// Creates a <see cref="FileMaterializationInfo"/> with an associated change tracking subscription.
        /// </summary>
        public FileMaterializationInfo(
            FileContentInfo fileContentInfo, 
            PathAtom fileName, 
            AbsolutePath opaqueDirectoryRoot, 
            RelativePath dynamicOutputCaseSensitiveRelativeDirectory, 
            ReparsePointInfo ? reparsePointInfo = null, 
            bool isAllowedSourceRewrite = false, 
            bool isExecutable = false)
        {
            // If the case sensitive relative path for a dynamic output is specified, then its output directory root needs to be valid
            Contract.Requires(!dynamicOutputCaseSensitiveRelativeDirectory.IsValid || opaqueDirectoryRoot.IsValid);

            FileName = fileName;
            FileContentInfo = fileContentInfo;
            ReparsePointInfo = reparsePointInfo ?? ReparsePointInfo.CreateNoneReparsePoint();
            IsUndeclaredFileRewrite = isAllowedSourceRewrite;
            IsExecutable = isExecutable;
            OpaqueDirectoryRoot = opaqueDirectoryRoot;
            DynamicOutputCaseSensitiveRelativeDirectory = dynamicOutputCaseSensitiveRelativeDirectory;
            // NOTE: Update ExecutionResultSerializer WriteOutputContent/ReadOutputContent when adding new fields (i.e., BuildXL.Engine.Cache protobuf structure) 
            // NOTE: Update FileArtifactKeyedHash when adding new fields (i.e., BuildXL.Engine protobuf structure) 
        }

        /// <summary>
        /// Creates a <see cref="FileMaterializationInfo"/> with a hash but no file name and length.
        /// This is intended for abstract hashes that don't correspond to real files on disk.
        /// </summary>
        public static FileMaterializationInfo CreateWithUnknownLength(ContentHash hash)
        {
            return new FileMaterializationInfo(FileContentInfo.CreateWithUnknownLength(hash), PathAtom.Invalid, AbsolutePath.Invalid, RelativePath.Invalid);
        }

        /// <summary>
        /// Creates a <see cref="FileMaterializationInfo"/> with a hash but no file name.
        /// This is intended for abstract hashes that don't correspond to real files on disk.
        /// </summary>
        public static FileMaterializationInfo CreateWithUnknownName(in FileContentInfo contentInfo)
        {
            return new FileMaterializationInfo(contentInfo, PathAtom.Invalid, AbsolutePath.Invalid, RelativePath.Invalid);
        }

        /// <summary>
        /// Given a path representing the absolute path of this file materialization info, returns an equivalent path with the proper casing enforced, if 
        /// either <see cref="FileName"/> or <see cref="DynamicOutputCaseSensitiveRelativeDirectory"/> are valid.
        /// </summary>
        public ExpandedAbsolutePath GetPathWithProperCasingIfAvailable(PathTable pathTable, ExpandedAbsolutePath path)
        {
            if (!FileName.IsValid && !DynamicOutputCaseSensitiveRelativeDirectory.IsValid)
            {
                return path;
            }

            // If the filename with the proper casing is not available, use the existing one
            var finalFileName = FileName;
            if (!finalFileName.IsValid)
            {
                finalFileName = path.Path.GetName(pathTable);
            }

            RelativePath relativePath = DynamicOutputCaseSensitiveRelativeDirectory.IsValid ? DynamicOutputCaseSensitiveRelativeDirectory.Combine(finalFileName) : RelativePath.Create(finalFileName);

            return path.WithTrailingRelativePath(pathTable, relativePath);
        }

        /// <summary>
        /// Content hash of the file as of when tracking was started.
        /// </summary>
        public ContentHash Hash => FileContentInfo.Hash;

        /// <summary>
        /// Length of the file in bytes.
        /// </summary>
        /// <remarks>
        /// Do not use this value for serialization (use FileContentInfo.RawLength)
        /// </remarks>
        public long Length => FileContentInfo.Length;

        /// <inheritdoc />
        public override string ToString()
        {
            return I($"[Content {FileContentInfo} with path '{FileName}']");
        }

        /// <inheritdoc />
        public bool Equals(FileMaterializationInfo other)
        {
            return other.FileName == FileName && 
                   other.FileContentInfo == FileContentInfo &&
                   other.ReparsePointInfo == ReparsePointInfo &&
                   other.IsUndeclaredFileRewrite == IsUndeclaredFileRewrite &&
                   other.IsExecutable == IsExecutable &&
                   other.OpaqueDirectoryRoot == OpaqueDirectoryRoot &&
                   other.DynamicOutputCaseSensitiveRelativeDirectory == DynamicOutputCaseSensitiveRelativeDirectory;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                FileName.GetHashCode(),
                FileContentInfo.GetHashCode(), 
                ReparsePointInfo.GetHashCode(), 
                IsUndeclaredFileRewrite.GetHashCode(), 
                IsExecutable.GetHashCode(),
                OpaqueDirectoryRoot.GetHashCode(),
                DynamicOutputCaseSensitiveRelativeDirectory.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(FileMaterializationInfo left, FileMaterializationInfo right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(FileMaterializationInfo left, FileMaterializationInfo right)
        {
            return !left.Equals(right);
        }
    }
}
