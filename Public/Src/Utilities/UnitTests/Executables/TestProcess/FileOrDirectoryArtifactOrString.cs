// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;

namespace Test.BuildXL.Executables.TestProcess
{
    /// <summary>
    /// Union type for a file or directory artifact or a string (representing a path).
    /// </summary>
    /// <remarks>
    /// The string form of this class <see cref="Create(string)"/> is meant for preserving the specific casing of the string (as opposed 
    /// to using a file or directory artifact, whose path expansion depends on the path table). This means that equality comparisons/hashing of this class
    /// uses a case sensitive comparison when strings are used.
    /// </remarks>
    public readonly struct FileOrDirectoryArtifactOrString : IEquatable<FileOrDirectoryArtifactOrString>
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
        /// A string representation of a file.
        /// </summary>
        public string FileAsString { get; }

        /// <summary>
        /// True if this union instance is a file.
        /// </summary>
        public bool IsFile => FileArtifact.IsValid;

        /// <summary>
        /// True if this union instance is a directory.
        /// </summary>
        public bool IsDirectory => DirectoryArtifact.IsValid;
        
        /// <summary>
        /// True if this union instance is a string.
        /// </summary>
        public bool IsString => FileAsString != null;

        /// <summary>
        /// True if this union instance is valid;
        /// </summary>
        public bool IsValid => this != Invalid;

        /// <inheritdoc />
        public override string ToString() => IsFile ? FileArtifact.ToString() : IsDirectory? DirectoryArtifact.ToString() : FileAsString;

        /// <summary>
        /// Invalid union instance.
        /// </summary>
        public static readonly FileOrDirectoryArtifactOrString Invalid = new FileOrDirectoryArtifactOrString(FileArtifact.Invalid, DirectoryArtifact.Invalid, null);

        /// <nodoc />
        private FileOrDirectoryArtifactOrString(FileArtifact fileArtifact, DirectoryArtifact directoryArtifact, string fileAsString)
        {
            FileArtifact = fileArtifact;
            DirectoryArtifact = directoryArtifact;
            FileAsString = fileAsString;
        }

        /// <summary>
        /// Creates a union instance from a file artifact.
        /// </summary>
        public static FileOrDirectoryArtifactOrString Create(FileArtifact fileArtifact)
        {
            Contract.Requires(fileArtifact.IsValid);
            return new FileOrDirectoryArtifactOrString(fileArtifact, DirectoryArtifact.Invalid, null);
        }

        /// <summary>
        /// Creates a union instance from a directory artifact.
        /// </summary>
        public static FileOrDirectoryArtifactOrString Create(DirectoryArtifact directoryArtifact)
        {
            Contract.Requires(directoryArtifact.IsValid);
            return new FileOrDirectoryArtifactOrString(FileArtifact.Invalid, directoryArtifact, null);
        }

        /// <summary>
        /// Creates a union instance from a string.
        /// </summary>
        public static FileOrDirectoryArtifactOrString Create(string fileAsString)
        {
            Contract.Requires(!string.IsNullOrEmpty(fileAsString));
            return new FileOrDirectoryArtifactOrString(FileArtifact.Invalid, DirectoryArtifact.Invalid, fileAsString);
        }

        /// <inheritdoc />
        public bool Equals(FileOrDirectoryArtifactOrString other)
        {
            return FileArtifact == other.FileArtifact && DirectoryArtifact == other.DirectoryArtifact && FileAsString == other.FileAsString;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(FileArtifact.GetHashCode(), FileAsString?.GetHashCode() ?? 0);
        }

        /// <nodoc />
        public static bool operator ==(FileOrDirectoryArtifactOrString left, FileOrDirectoryArtifactOrString right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(FileOrDirectoryArtifactOrString left, FileOrDirectoryArtifactOrString right)
        {
            return !(left == right);
        }

        /// <inheritdoc />
        public string Path(PathTable pathTable) => IsFile ? FileArtifact.Path.ToString(pathTable) : IsDirectory ? DirectoryArtifact.Path.ToString(pathTable) : FileAsString;

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator FileOrDirectoryArtifactOrString(FileArtifact file)
        {
            return Create(file);
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator FileOrDirectoryArtifactOrString(DirectoryArtifact directoryArtifact)
        {
            return Create(directoryArtifact);
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator FileOrDirectoryArtifactOrString(string fileAsString)
        {
            return Create(fileAsString);
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator FileOrDirectoryArtifactOrString(FileOrDirectoryArtifact fileOrDirectoryArtifact)
        {
            return fileOrDirectoryArtifact.IsFile ? Create(fileOrDirectoryArtifact.FileArtifact) : Create(fileOrDirectoryArtifact.DirectoryArtifact);
        }
    }
}
