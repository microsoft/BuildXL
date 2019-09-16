// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents an opaque directory. Pips that depend on this directory can enumerate
    /// it and read its contents.
    /// </summary>
    public readonly struct DirectoryArtifact : IEquatable<DirectoryArtifact>, IImplicitPath
    {
        private const byte IsSharedOpaqueShift = 31;

        private const uint IsSharedOpaqueBit = 1U << IsSharedOpaqueShift;

        private const uint PartialSealIdMask = (1U << IsSharedOpaqueShift) - 1;

        /// <summary>
        /// Packed representation the directory seal id plus if the directory is a shared opaque one. 
        /// The top bit indicates if the directory is a shared opaque one. The remaining bits form the <see cref="PartialSealId"/>.
        /// </summary>
        internal uint IsSharedOpaquePlusPartialSealId { get; }

        /// <summary>
        /// Invalid artifact for uninitialized fields.
        /// </summary>
        public static readonly DirectoryArtifact Invalid = default(DirectoryArtifact);

        /// <nodoc />
        internal DirectoryArtifact(AbsolutePath path, uint isSharedOpaquePlusPartialSealId)
        {
            Path = path;
            IsSharedOpaquePlusPartialSealId = isSharedOpaquePlusPartialSealId;
        }

        /// <nodoc />
        public DirectoryArtifact(AbsolutePath path, uint partialSealId, bool isSharedOpaque)
            : this(path, partialSealId | (isSharedOpaque ? IsSharedOpaqueBit : 0))
        {
            Contract.Requires(!isSharedOpaque || (partialSealId > 0), "A shared opaque directory should always have a proper seal id");
            Contract.Requires((partialSealId & ~PartialSealIdMask) == 0, "The most significant bit of a partial seal id should not be used");
        }

        /// <summary>
        /// Helper to create directory artifacts
        /// </summary>
        public static DirectoryArtifact CreateWithZeroPartialSealId(PathTable pathTable, string path)
        {
            return CreateWithZeroPartialSealId(AbsolutePath.Create(pathTable, path));
        }

        /// <summary>
        /// Helper to create directory artifact
        /// </summary>
        public static DirectoryArtifact CreateWithZeroPartialSealId(AbsolutePath path)
        {
            return new DirectoryArtifact(path, 0, false);
        }

        internal static DirectoryArtifact CreateDirectoryArtifactForTesting(AbsolutePath path, uint partialSealId)
        {
            return new DirectoryArtifact(path, partialSealId, false);
        }

        /// <summary>
        /// Indicates if this directory artifact and the one given represent the same underlying value.
        /// </summary>
        /// <remarks>
        /// Two <see cref="BuildXL.Utilities.DirectoryArtifact"/> instances are equal if they represent the same directory and the
        /// same partial view of that directory (if applicable).
        /// </remarks>
        public bool Equals(DirectoryArtifact other)
        {
            return Path == other.Path && IsSharedOpaquePlusPartialSealId == other.IsSharedOpaquePlusPartialSealId;
        }

        /// <summary>
        /// Indicates if a given object is a DirectoryArtifact equal to this one. See <see cref="Equals(DirectoryArtifact)" />.
        /// </summary>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return unchecked(Path.GetHashCode() + (int)PartialSealId);
        }

        /// <summary>
        /// Equality operator for two directory artifacts.
        /// </summary>
        public static bool operator ==(DirectoryArtifact left, DirectoryArtifact right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for two directory artifacts.
        /// </summary>
        public static bool operator !=(DirectoryArtifact left, DirectoryArtifact right)
        {
            return !left.Equals(right);
        }

#pragma warning disable 809

#pragma warning restore 809

        /// <summary>
        /// Determines whether this instance has been properly initialized or is merely default(DirectoryArtifact).
        /// </summary>
        public bool IsValid => Path != AbsolutePath.Invalid;

        /// <summary>
        /// Gets the AbsolutePath associated with this artifact.
        /// </summary>
        public AbsolutePath Path { get; }

        /// <summary>
        /// The unique id for partially sealed directories
        /// </summary>
        public uint PartialSealId => IsSharedOpaquePlusPartialSealId & PartialSealIdMask;

        /// <summary>
        /// Whether this directory represents a shared opaque directory
        /// </summary>
        public bool IsSharedOpaque => (IsSharedOpaquePlusPartialSealId & IsSharedOpaqueBit) != 0;
    }
}
