// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Union type for file or directory artifact.
    /// </summary>
    public readonly struct FileOrDirectoryArtifact : IEquatable<FileOrDirectoryArtifact>, IImplicitPath
    {
        /// <summary>
        /// File artifact.
        /// </summary>
        public FileArtifact FileArtifact { get; }

        /// <summary>
        /// Directory artifact.
        /// </summary>
        public DirectoryArtifact DirectoryArtifact { get; }

        /// <summary>
        /// True if this union instance is a file.
        /// </summary>
        public bool IsFile => FileArtifact.IsValid;

        /// <summary>
        /// True if this union instance is a directory.
        /// </summary>
        public bool IsDirectory => DirectoryArtifact.IsValid;

        /// <summary>
        /// True if this union instance is valid;
        /// </summary>
        public bool IsValid => this != Invalid;

        /// <summary>
        /// Invalid union instance.
        /// </summary>
        public static readonly FileOrDirectoryArtifact Invalid = new FileOrDirectoryArtifact(FileArtifact.Invalid, DirectoryArtifact.Invalid);

        /// <nodoc />
        private FileOrDirectoryArtifact(FileArtifact fileArtifact, DirectoryArtifact directoryArtifact)
        {
            FileArtifact = fileArtifact;
            DirectoryArtifact = directoryArtifact;
        }

        /// <summary>
        /// Creates a union instance from a file artifact.
        /// </summary>
        public static FileOrDirectoryArtifact Create(FileArtifact fileArtifact)
        {
            Contract.Requires(fileArtifact.IsValid);
            return new FileOrDirectoryArtifact(fileArtifact, DirectoryArtifact.Invalid);
        }

        /// <summary>
        /// Creates a union instance from a directory artifact.
        /// </summary>
        public static FileOrDirectoryArtifact Create(DirectoryArtifact directoryArtifact)
        {
            Contract.Requires(directoryArtifact.IsValid);
            return new FileOrDirectoryArtifact(FileArtifact.Invalid, directoryArtifact);
        }

        /// <inheritdoc />
        public bool Equals(FileOrDirectoryArtifact other)
        {
            return FileArtifact == other.FileArtifact && DirectoryArtifact == other.DirectoryArtifact;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(FileArtifact.GetHashCode(), DirectoryArtifact.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(FileOrDirectoryArtifact left, FileOrDirectoryArtifact right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(FileOrDirectoryArtifact left, FileOrDirectoryArtifact right)
        {
            return !(left == right);
        }

        /// <inheritdoc />
        public AbsolutePath Path => IsFile ? FileArtifact.Path : DirectoryArtifact.Path;

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator FileOrDirectoryArtifact(FileArtifact file)
        {
            return Create(file);
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator FileOrDirectoryArtifact(DirectoryArtifact directory)
        {
            return Create(directory);
        }
    }
}
