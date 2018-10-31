// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Pips
{
    /// <summary>
    /// Defines root and behavior for a path.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct SemanticPathInfo
    {
        /// <summary>
        /// Gets the invalid semantic path info
        /// </summary>
        public static readonly SemanticPathInfo Invalid = default(SemanticPathInfo);

        /// <summary>
        /// The semantic flags describing behavior for the path
        /// </summary>
        public readonly SemanticPathFlags Flags;

        /// <summary>
        /// The semantic root path
        /// </summary>
        public readonly AbsolutePath Root;

        /// <summary>
        /// The semantic root name
        /// </summary>
        public readonly PathAtom RootName;

        /// <summary>
        /// Creates a new SemanticPathInfo
        /// </summary>
        public SemanticPathInfo(PathAtom rootName, AbsolutePath root, bool allowHashing, bool readable, bool writable, bool system = false, bool scrubbable = false, bool allowCreateDirectory = false)
        {
            RootName = rootName;
            Root = root;
            Flags = SemanticPathFlags.None |
                (allowHashing ? SemanticPathFlags.AllowHashing : SemanticPathFlags.None) |
                (readable ? SemanticPathFlags.Readable : SemanticPathFlags.None) |
                (writable ? SemanticPathFlags.Writable : SemanticPathFlags.None) |
                (system ? SemanticPathFlags.System : SemanticPathFlags.None) |
                (scrubbable ? SemanticPathFlags.Scrubbable : SemanticPathFlags.None) |
                (allowCreateDirectory ? SemanticPathFlags.AllowCreateDirectory : SemanticPathFlags.None);
        }

        /// <summary>
        /// Creates a new SemanticPathInfo
        /// </summary>
        public SemanticPathInfo(PathAtom rootName, AbsolutePath root, SemanticPathFlags flags)
        {
            RootName = rootName;
            Root = root;
            Flags = flags;
        }

        /// <summary>
        /// Gets whether the semantic path info is valid.
        /// </summary>
        public bool IsValid => Root.IsValid;

        /// <summary>
        /// Gets whether the path is hashable
        /// </summary>
        public bool AllowHashing => (Flags & SemanticPathFlags.AllowHashing) != SemanticPathFlags.None;

        /// <summary>
        /// Gets whether the path is readable
        /// </summary>
        public bool IsReadable => (Flags & SemanticPathFlags.Readable) != SemanticPathFlags.None;

        /// <summary>
        /// Gets whether the path is writable
        /// </summary>
        public bool IsWritable => (Flags & SemanticPathFlags.Writable) != SemanticPathFlags.None;

        /// <summary>
        /// Gets whether CreateDirectory is allowed at the path
        /// </summary>
        public bool AllowCreateDirectory => (Flags & SemanticPathFlags.AllowCreateDirectory) != SemanticPathFlags.None;

        /// <summary>
        /// Gets whether the path is a system location
        /// </summary>
        public bool IsSystem => (Flags & SemanticPathFlags.System) != SemanticPathFlags.None;

        /// <summary>
        /// Gets whether the path is a location that can be scrubbed
        /// </summary>
        public bool IsScrubbable => (Flags & SemanticPathFlags.Scrubbable) != SemanticPathFlags.None;

        /// <summary>
        /// Gets whether the path has potential build outputs
        /// </summary>
        public bool HasPotentialBuildOutputs => (Flags & SemanticPathFlags.HasPotentialBuildOutputs) != SemanticPathFlags.None;

        #region Serialization

        /// <summary>
        /// Serializes
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            writer.Write((byte)Flags);
            writer.Write(RootName);
            writer.Write(Root);
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        public static SemanticPathInfo Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            SemanticPathFlags info = (SemanticPathFlags)reader.ReadByte();
            return new SemanticPathInfo(
                reader.ReadPathAtom(),
                reader.ReadAbsolutePath(),
                info);
        }
        #endregion
    }

    /// <summary>
    /// Flags indicating path semantics (ie hashable)
    /// </summary>
    [Flags]
    public enum SemanticPathFlags : byte
    {
        /// <summary>
        /// Indicates that the path is not hashable, readable, or writeable.
        /// </summary>
        None = 0,

        /// <summary>
        /// Indicates that the path represents a hashable location.
        /// </summary>
        AllowHashing = 1,

        /// <summary>
        /// Indicates that the path represents a readable location.
        /// </summary>
        Readable = 1 << 1,

        /// <summary>
        /// Indicates that the path represents a writable location.
        /// </summary>
        Writable = 1 << 2 | HasPotentialBuildOutputs,

        /// <summary>
        /// Indicates that the path represents a system location.
        /// </summary>
        System = 1 << 3,

        /// <summary>
        /// Indicates that the paths represents a location that can be scrubbed
        /// </summary>
        Scrubbable = 1 << 4,

        /// <summary>
        /// Indicates that the path represents a location which can contain build outputs
        /// </summary>
        HasPotentialBuildOutputs = 1 << 5,

        /// <summary>
        /// Indicates that the path represents a location where a directory can be created
        /// </summary>
        AllowCreateDirectory = 1 << 6,
    }
}
