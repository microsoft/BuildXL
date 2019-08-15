// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents a file artifact in the graph.
    /// </summary>
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
    public readonly struct FileArtifact : IEquatable<FileArtifact>, IImplicitPath
    {
        private readonly int m_rewriteCount;

        /// <summary>
        /// Invalid FileArtifact for uninitialized FileArtifact fields.
        /// </summary>
        public static readonly FileArtifact Invalid = new FileArtifact(AbsolutePath.Invalid);

        /// <summary>
        /// Creates a file artifact at the given write count.
        /// </summary>
        /// <remarks>
        /// Outside of tests, artifacts are scheduler handles that should only be created by a scheduler.
        /// </remarks>
        public FileArtifact(AbsolutePath path, int rewriteCount = 0)
        {
            Path = path;
            m_rewriteCount = rewriteCount;
        }

        /// <summary>
        /// Creates a FileArtifact.
        /// </summary>
        public static FileArtifact CreateSourceFile(AbsolutePath path)
        {
            Contract.Requires(path != AbsolutePath.Invalid);
            Contract.Ensures(Contract.Result<FileArtifact>().IsValid);

            return new FileArtifact(path);
        }

        /// <summary>
        /// Creates an output FileArtifact (with rewriteCount set to 1).
        /// </summary>
        public static FileArtifact CreateOutputFile(AbsolutePath path)
        {
            Contract.Requires(path != AbsolutePath.Invalid);
            Contract.Ensures(Contract.Result<FileArtifact>().IsValid);

            return new FileArtifact(path, 1);
        }

        /// <summary>
        /// Creates a file artifact for the next written version of the one given.
        /// </summary>
        /// <remarks>
        /// The result represents the same path, but has a write count one higher.
        /// This method is internal since schedule correctness / determinism depends
        /// on the entailment of scheduling pips and file artifact generation. If
        /// users were able to create file artifacts of arbitrary versions,
        /// they would be able to (non-deterministically) rendezvous by path.
        /// </remarks>
        /// <returns>File Artifact that is the rewrite of the one passed in.</returns>
        public FileArtifact CreateNextWrittenVersion()
        {
            Contract.Requires(IsValid);
            Contract.Ensures(Contract.Result<FileArtifact>().IsValid);

            int newCount = checked(1 + m_rewriteCount);
            return new FileArtifact(Path, newCount);
        }

        /// <summary>
        /// Whether this file artifact is valid (and not the default value)
        /// </summary>
        public bool IsValid => Path.IsValid;

        /// <summary>
        /// Path of the file
        /// </summary>
        public AbsolutePath Path { get; }

        /// <summary>
        /// Returns the rewrite count.
        /// </summary>
        /// <remarks>
        /// A value of 0 means it is a source file
        /// A value of 1 means it is an output file
        /// A value greater than 1 means it is a rewritten file.
        /// </remarks>
        public int RewriteCount => m_rewriteCount;

        /// <summary>
        /// Determines if this is a source file or not.
        /// </summary>
        public bool IsSourceFile => m_rewriteCount == 0;

        /// <summary>
        /// Determines if this is an output file or not.
        /// </summary>
        public bool IsOutputFile => m_rewriteCount > 0;

        /// <summary>
        /// Constructs <see cref="FileArtifactWithAttributes"/> based on the current FileAttribute instance
        /// with specified <paramref name="fileExistence"/> attibute.
        /// </summary>
        public FileArtifactWithAttributes WithAttributes(FileExistence fileExistence = FileExistence.Required)
        {
            return FileArtifactWithAttributes.FromFileArtifact(this, fileExistence);
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other" /> parameter; otherwise, false.
        /// </returns>
        /// <param name="other">An object to compare with this object.</param>
        /// <filterpriority>2</filterpriority>
        public bool Equals(FileArtifact other)
        {
            // Note: The Invalid FileArtifact always has a rewrite count of 0.
            return Path == other.Path && RewriteCount == other.RewriteCount;
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

        /// <summary>
        /// Serves as a hash function for a particular type.
        /// </summary>
        /// <remarks>
        /// It is illegal for a file to have both a rewrite count of 0 AND 1 in the graph.
        /// Therefore we will give both the same hash value as there shouldn't be many collisions, only to report errors.
        /// Furthermore we expect the rewrites > 1 to be limited and eliminated over time. We will use the higher-order bits,
        /// One strategy would be to reverse the bits on the rewrite count and bitwise or it with the absolute path so collisions
        /// would only occur when there are tons of files or high rewrite counts.
        /// </remarks>
        /// <returns>
        /// A hash code for the current object.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override int GetHashCode()
        {
            // see remarks on why it is implemented this way.
            return Path.GetHashCode();
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
        public static bool operator ==(FileArtifact left, FileArtifact right)
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
        public static bool operator !=(FileArtifact left, FileArtifact right)
        {
            return !left.Equals(right);
        }

#pragma warning disable 809

        /// <inheritdoc />
        public override string ToString()
        {
            if (this == Invalid)
            {
                return "{Invalid}";
            }

            return I($"{{File (id: {Path.Value.Value:x}) ({GetAnnotation()})}}");
        }

        /// <summary>
        /// Returns a string to be displayed as the debugger representation of this value.
        /// This string contains an expanded path when possible. See the comments in PathTable.cs
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Nothing is private to the debugger.")]
        [ExcludeFromCodeCoverage]
        private string ToDebuggerDisplay()
        {
            string path;
            if (!Path.IsValid)
            {
                path = "Invalid";
            }
            else
            {
                PathTable owner = HierarchicalNameTable.DebugTryGetTableForId(Path.Value) as PathTable;
                path = owner == null
                    ? "{Unable to expand AbsolutePath; this may occur after the allocation of a large number of PathTables}"
                    : Path.ToString(owner);
            }

            return I($"{{{path} ({GetAnnotation()})}}");
        }

        private string GetAnnotation()
        {
            switch (m_rewriteCount)
            {
                case 0:
                    return "source";
                case 1:
                    return "output";
                default:
                    return I($"rewrite:{m_rewriteCount}");
            }
        }

#pragma warning restore 809
    }
}
