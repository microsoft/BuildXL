// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities;

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// Mode for placing or ingressing files to an <see cref="IArtifactContentCache"/>.
    /// </summary>
    public enum DiskFileRealizationMode
    {
        /// <summary>
        /// Always copy. Upon completion, the file will be writable.
        /// </summary>
        Copy,

        /// <summary>
        /// Always hardlink. Upon completion, the file will be read-only.
        /// </summary>
        HardLink,

        /// <summary>
        /// Prefer hardlinking, but fall back to copy. Assume that the file is read-only upon completion.
        /// </summary>
        HardLinkOrCopy,
    }

    /// <summary>
    /// Mode for placing or ingressing files to an <see cref="IArtifactContentCache"/>.
    /// </summary>
    public readonly struct FileRealizationMode : IEquatable<FileRealizationMode>
    {
        /// <summary>
        /// <see cref="DiskFileRealizationMode.Copy"/>
        /// </summary>
        public static readonly FileRealizationMode Copy = new FileRealizationMode(DiskFileRealizationMode.Copy);

        /// <summary>
        /// <see cref="DiskFileRealizationMode.HardLink"/>
        /// </summary>
        public static readonly FileRealizationMode HardLink = new FileRealizationMode(DiskFileRealizationMode.HardLink);

        /// <summary>
        /// <see cref="DiskFileRealizationMode.HardLinkOrCopy"/>
        /// </summary>
        public static readonly FileRealizationMode HardLinkOrCopy = new FileRealizationMode(DiskFileRealizationMode.HardLinkOrCopy);

        /// <summary>
        /// Indicates how file should be materialized on disk
        /// </summary>
        public DiskFileRealizationMode DiskMode { get; }

        /// <summary>
        /// Indicates whether the file can be virtualized when materialized
        /// </summary>
        public bool AllowVirtualization { get; }

        /// <nodoc />
        public FileRealizationMode(DiskFileRealizationMode diskMode, bool allowVirtualization = false)
        {
            DiskMode = diskMode;
            AllowVirtualization = allowVirtualization;
        }

        /// <summary>
        /// Returns a new <see cref="FileRealizationMode"/> with allow virtualization set to given value (true by default)
        /// </summary>
        public FileRealizationMode WithAllowVirtualization(bool allowVirtualization = true)
        {
            return new FileRealizationMode(DiskMode, allowVirtualization: allowVirtualization);
        }

        /// <inheritdoc />
        public bool Equals(FileRealizationMode other)
        {
            // Equality is based solely on disk mode
            return DiskMode == other.DiskMode;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return DiskMode.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return DiskMode.ToString();
        }

        /// <nodoc />
        public static bool operator ==(FileRealizationMode left, FileRealizationMode right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(FileRealizationMode left, FileRealizationMode right)
        {
            return !left.Equals(right);
        }

        /// <nodoc />
        public static DiskFileRealizationMode operator &(FileRealizationMode left, FileRealizationMode right)
        {
            return left.DiskMode & right.DiskMode;
        }
    }
}
