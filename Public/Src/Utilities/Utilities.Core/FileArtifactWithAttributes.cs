// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Represents a file artifact with additional <see cref="FileExistence"/> attributes.
    /// </summary>
    /// <remarks>
    /// This struct is similar to <see cref="BuildXL.Utilities.Core.FileArtifact"/> but holds additional information
    /// provided by FileExistence property and file rewrite tracking.
    /// </remarks>
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
    public readonly struct FileArtifactWithAttributes : IEquatable<FileArtifactWithAttributes>
    {
        private const FileExistence DefaultFileExistence = FileExistence.Required;

        /// <summary>
        /// Number of bits for rewrite count in m_rewriteCountAndFileExistenceAndFileRewrite field
        /// </summary>
        private const int RewriteCountSize = 24;

        /// <summary>
        /// Bitmask for getting rewrite count from m_rewriteCountAndFileExistenceAndFileRewrite field
        /// </summary>
        private const int RewriteCountMask = 0x00FFFFFF;

        /// <summary>
        /// Bitmask for getting the file existence from m_rewriteCountAndFileExistenceAndFileRewrite field
        /// </summary>
        private const int FileExistenceMask = 0x7F000000;

        /// <summary>
        /// Bitmask for the source rewrite bit from m_rewriteCountAndFileExistenceAndFileRewrite field
        /// </summary>
        private const uint UndeclaredFileRewriteMask = 0x80000000;

        /// <summary>
        /// Max value for <see cref="RewriteCount"/> property.
        /// </summary>
        public const int MaxRewriteCount = (int)(uint.MaxValue >> 8);

        /// <summary>
        /// Max value for <see cref="FileExistence"/> property.
        /// </summary>
        public const int MaxFileExistence = 127;

        private readonly AbsolutePath m_path;

        /// <summary>
        /// Stores information about rewrite count as well as additional file existence attributes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Following structure is used (FR is for FileRewrite):
        /// |--------------------------------------------------|
        /// |FR| FileExistence |        Rewrite Count          |
        /// |--------------------------------------------------|
        /// |31|30           24|23                            0|
        /// |--------------------------------------------------|
        /// </para>
        /// <para>
        /// Following structure allows to store 128 different FileExistence attributes and up to 16.777.216 rewrites.
        /// </para>
        /// </remarks>
        private readonly uint m_rewriteCountAndFileExistenceAndFileRewrite;

        private int RewriteCountAndFileExistenceAndFileRewriteAsInt => unchecked((int)m_rewriteCountAndFileExistenceAndFileRewrite);

        /// <summary>
        /// Invalid <see cref="FileArtifactWithAttributes"/> for uninitialized fields.
        /// </summary>
        public static readonly FileArtifactWithAttributes Invalid = new FileArtifactWithAttributes(AbsolutePath.Invalid, 0, DefaultFileExistence, isUndeclaredFileRewrite: false);

        /// <summary>
        /// Creates an  artifact at the given write count and existence.
        /// </summary>
        internal FileArtifactWithAttributes(AbsolutePath path, int rewriteCount, FileExistence fileExistence, bool isUndeclaredFileRewrite = false)
        {
            Contract.Requires(rewriteCount <= MaxRewriteCount);
            Contract.Requires(((byte)fileExistence) <= MaxFileExistence);

            m_path = path;
            unchecked
            {
                m_rewriteCountAndFileExistenceAndFileRewrite = ((uint)rewriteCount | (uint)((byte)fileExistence << RewriteCountSize) | (isUndeclaredFileRewrite ? UndeclaredFileRewriteMask : 0));
            }
        }

        /// <summary>
        /// Creates a file artifact from deserialized values.
        /// </summary>
        internal FileArtifactWithAttributes(AbsolutePath path, uint rewriteCountAndFileExistenceAndFileRewrite)
        {
            m_path = path;
            m_rewriteCountAndFileExistenceAndFileRewrite = rewriteCountAndFileExistenceAndFileRewrite;
        }

        /// <nodoc />
        public static FileArtifactWithAttributes Create(FileArtifact artifact, FileExistence fileExistence, bool isUndeclaredFileRewrite = false)
        {
            return new FileArtifactWithAttributes(artifact.Path, artifact.RewriteCount, fileExistence, isUndeclaredFileRewrite);
        }

        /// <summary>
        /// Whether this file artifact is valid (and not the default value)
        /// </summary>
        public bool IsValid => m_path.IsValid;

        /// <summary>
        /// Path of the file
        /// </summary>
        public AbsolutePath Path => m_path;

        /// <summary>
        /// Compact representation that is exposed for internal custom serialization/deseralization
        /// </summary>
        internal uint RewriteCountAndFileExistenceAndFileRewrite => m_rewriteCountAndFileExistenceAndFileRewrite;

        /// <summary>
        /// Returns the rewrite count.
        /// </summary>
        /// <remarks>
        /// A value of 0 means it is a source file
        /// A value of 1 means it is an output file
        /// A value greater than 1 means it is a rewritten file.
        /// </remarks>
        public int RewriteCount => (int)(m_rewriteCountAndFileExistenceAndFileRewrite & RewriteCountMask);

        /// <summary>
        /// Determines if this is a source file or not.
        /// </summary>
        public bool IsSourceFile => RewriteCount == 0;

        /// <summary>
        /// Determines if this is an output file or not.
        /// </summary>
        public bool IsOutputFile => RewriteCount > 0;

        /// <summary>
        /// Returns <see cref="FileExistence"/> associated with current <see cref="BuildXL.Utilities.Core.FileArtifact"/>.
        /// </summary>
        public FileExistence FileExistence => (FileExistence)((m_rewriteCountAndFileExistenceAndFileRewrite & FileExistenceMask) >> RewriteCountSize);

        /// <summary>
        /// Returns whether the current <see cref="BuildXL.Utilities.Core.FileArtifact"/> is rewriting an undeclared source or alien file.
        /// </summary>
        public bool IsUndeclaredFileRewrite => (m_rewriteCountAndFileExistenceAndFileRewrite & UndeclaredFileRewriteMask) > 0;

        /// <summary>
        /// Determines if this is a temporary output file or not.
        /// </summary>
        public bool IsTemporaryOutputFile => IsOutputFile && FileExistence == FileExistence.Temporary;

        /// <summary>
        /// Determins if this is a required output file or not
        /// </summary>
        public bool IsRequiredOutputFile => IsOutputFile && FileExistence == FileExistence.Required;

        /// <summary>
        /// Returns corresponding <see cref="BuildXL.Utilities.Core.FileArtifact"/> with the same path and write count.
        /// </summary>
        public FileArtifact ToFileArtifact()
        {
            if (!IsValid)
            {
                return FileArtifact.Invalid;
            }

            return new FileArtifact(Path, RewriteCount);
        }

        /// <summary>
        /// Factory method that creates new instance based on specified <paramref name="fileArtifact"/>.
        /// </summary>
        public static FileArtifactWithAttributes FromFileArtifact(FileArtifact fileArtifact, FileExistence fileExistence, bool isUndeclaredFileRewrite = false)
        {
            if (!fileArtifact.IsValid)
            {
                return Invalid;
            }

            return new FileArtifactWithAttributes(fileArtifact.Path, fileArtifact.RewriteCount, fileExistence, isUndeclaredFileRewrite);
        }

        /// <summary>
        /// Create new written version from existing instance.
        /// </summary>
        public FileArtifactWithAttributes CreateNextWrittenVersion()
        {
            return new FileArtifactWithAttributes(Path, checked(RewriteCount + 1), FileExistence, IsUndeclaredFileRewrite);
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other" /> parameter; otherwise, false.
        /// </returns>
        /// <param name="other">An object to compare with this object.</param>
        /// <filterpriority>2</filterpriority>
        public bool Equals(FileArtifactWithAttributes other)
        {
            // Note: The Invalid FileArtifactWithAttributes always has a rewrite count of 0.
            return Path == other.Path && m_rewriteCountAndFileExistenceAndFileRewrite == other.m_rewriteCountAndFileExistenceAndFileRewrite;
        }

        /// <summary>
        /// Compares this object to another for purposes of ordering.
        /// </summary>
        /// <param name="other">Other File artifact to compare against.</param>
        /// <param name="comparer">Path comparer to compare file paths</param>
        /// <param name="pathOnly">If false, also compare rewrite count and file existence</param>
        /// <returns>0 if the objects are equal, postitive or negative number depending on which object comes first.</returns>
        public int CompareTo(FileArtifactWithAttributes other, PathTable.ExpandedAbsolutePathComparer comparer, bool pathOnly)
        {
            var pathCompare = comparer.Compare(Path, other.Path);
            return pathCompare != 0 || pathOnly ? pathCompare : (RewriteCountAndFileExistenceAndFileRewriteAsInt - other.RewriteCountAndFileExistenceAndFileRewriteAsInt);
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <returns>
        /// true if the specified object  is equal to the current object; otherwise, false.
        /// </returns>
        /// <param name="obj">The object to compare with the current object. </param>
        /// <filterpriority>2</filterpriority>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Path.GetHashCode(), RewriteCountAndFileExistenceAndFileRewriteAsInt);
        }

        /// <summary>
        /// Indicates whether two object instances are equal.
        /// </summary>
        /// <returns>
        /// true if the values of <paramref name="left" /> and <paramref name="right" /> are equal; otherwise, false.
        /// </returns>
        /// <param name="left">The first object to compare. </param>
        /// <param name="right">The second object to compare. </param>
        /// <filterpriority>3</filterpriority>
        public static bool operator ==(FileArtifactWithAttributes left, FileArtifactWithAttributes right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two object instances are not equal.
        /// </summary>
        /// <returns>
        /// true if the values of <paramref name="left" /> and <paramref name="right" /> are not equal; otherwise, false.
        /// </returns>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <filterpriority>3</filterpriority>
        public static bool operator !=(FileArtifactWithAttributes left, FileArtifactWithAttributes right)
        {
            return !left.Equals(right);
        }

#pragma warning disable 809

        /// <summary>
        /// Not available for <see cref="FileArtifactWithAttributes"/>. Throws an exception.
        /// </summary>
        [Obsolete("Not suitable for FileArtifactWithAttributes")]
        public override string ToString()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns a string to be displayed as the debugger representation of this value.
        /// This string contains an expanded path when possible. See the comments in PathTable.cs
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Nothing is private to the debugger.")]
        [ExcludeFromCodeCoverage]
        private string ToDebuggerDisplay()
        {
            string annotation;
            switch (RewriteCount)
            {
                case 0:
                    annotation = "source";
                    break;
                case 1:
                    annotation = "output";
                    break;
                default:
                    annotation = "rewrite:" + RewriteCount.ToString(CultureInfo.InvariantCulture);
                    break;
            }

            if (IsUndeclaredFileRewrite)
            {
                annotation += "[File rewrite]";
            }

            string path;
            if (!Path.IsValid)
            {
                path = "Invalid";
            }
            else
            {
                PathTable owner = HierarchicalNameTable.DebugTryGetTableForId(Path.Value) as PathTable;
                path = (owner == null)
                    ? "{Unable to expand AbsolutePath; this may occur after the allocation of a large number of PathTables}"
                    : Path.ToString(owner);
            }

            return string.Format(CultureInfo.InvariantCulture, "{{{0} ({1}): {2}}}", path, annotation, FileExistence);
        }
#pragma warning restore 809

        internal static FileArtifactWithAttributes Deserialize(BuildXLReader reader)
        {
            Contract.RequiresNotNull(reader);

            return new FileArtifactWithAttributes(
                reader.ReadAbsolutePath(),
                reader.ReadUInt32());
        }

        internal void Serialize(BuildXLWriter writer)
        {
            Contract.RequiresNotNull(writer);

            writer.Write(m_path);
            writer.Write(m_rewriteCountAndFileExistenceAndFileRewrite);
        }
    }
}
