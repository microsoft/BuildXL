// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Enumeration representing the types of values that can be contained in a pip data entry.
    /// </summary>
    public enum PipDataEntryType : byte
    {
        /// <summary>
        /// Invalid, default value.
        /// </summary>
        Invalid = 0,

        /// <summary>
        /// A string literal.
        /// </summary>
        /// <remarks>
        /// Data is <see cref="BuildXL.Utilities.StringId.Value"/>
        /// </remarks>
        StringLiteral,

        /// <summary>
        /// Absolute file or directory path.
        /// </summary>
        /// <remarks>
        /// Data is <see cref="BuildXL.Utilities.AbsolutePath.RawValue"/>
        /// </remarks>
        AbsolutePath,

        /// <summary>
        /// First entry of a <see cref="PipFragmentType.VsoHash"/> fragment which holds the
        /// <see cref="BuildXL.Utilities.FileArtifact.Path"/> value of the corresponding FileArtifact.  Must
        /// be followed by an entry of type <see cref="PipDataEntryType.VsoHashEntry2RewriteCount"/>.
        /// </summary>
        /// <remarks>
        /// Data is <see cref="BuildXL.Utilities.AbsolutePath.RawValue"/>.
        /// </remarks>
        VsoHashEntry1Path,

        /// <summary>
        /// Second entry of a <see cref="PipFragmentType.VsoHash"/> fragment which holds the
        /// <see cref="BuildXL.Utilities.FileArtifact.RewriteCount"/> value of the corresponding FileArtifact.
        /// </summary>
        /// <remarks>
        /// Data is <see cref="BuildXL.Utilities.FileArtifact.RewriteCount"/>.
        /// </remarks>
        VsoHashEntry2RewriteCount,

        /// <summary>
        /// First entry of a <see cref="PipFragmentType.FileId"/> fragment which holds the
        /// <see cref="BuildXL.Utilities.FileArtifact.Path"/> value of the corresponding FileArtifact.  Must
        /// be followed by an entry of type <see cref="PipDataEntryType.FileId2RewriteCount"/>.
        /// </summary>
        /// <remarks>
        /// Data is <see cref="BuildXL.Utilities.AbsolutePath.RawValue"/>.
        /// </remarks>
        FileId1Path,

        /// <summary>
        /// Second entry of a <see cref="PipFragmentType.FileId"/> fragment which holds the
        /// <see cref="BuildXL.Utilities.FileArtifact.RewriteCount"/> value of the corresponding FileArtifact.
        /// </summary>
        /// <remarks>
        /// Data is <see cref="BuildXL.Utilities.FileArtifact.RewriteCount"/>.
        /// </remarks>
        FileId2RewriteCount,

        /// <summary>
        /// IPC moniker.
        /// </summary>
        /// <remarks>
        /// Data is <see cref="BuildXL.Utilities.StringId"/> of <see cref="BuildXL.Ipc.Interfaces.IIpcMoniker.Id"/>.
        /// </remarks>
        IpcMoniker,

        /// <summary>
        /// The header for a nested pip data fragment.
        /// </summary>
        /// <remarks>
        /// Data is <see cref="BuildXL.Utilities.StringId.Value"/>
        /// Next entry's type should be <see cref="NestedDataStart"/>
        /// </remarks>
        NestedDataHeader,

        /// <summary>
        /// The start entry for nested fragments of nested pip data.
        /// </summary>
        /// <remarks>
        /// Data is number entries in nested pip data excluding header.
        /// The nested data end entry is located at (NestedDataStartIndex + Data - 1).
        /// </remarks>
        NestedDataStart,

        /// <summary>
        /// The end entry for nested fragments of nested pip data.
        /// </summary>
        /// <remarks>
        /// Data is number of fragments in nested pip data excluding header).
        /// </remarks>
        NestedDataEnd,
    }

    /// <summary>
    /// Extension methods for <see cref="PipDataEntryType"/>.
    /// </summary>
    public static class PipDataEntryTypeExtensions
    {
        /// <summary>
        /// Whether the enum constant is one of the 3 Vso values.
        /// </summary>
        [Pure]
        public static bool IsVsoHash(this PipDataEntryType entryType)
        {
            return
                entryType == PipDataEntryType.VsoHashEntry1Path ||
                entryType == PipDataEntryType.VsoHashEntry2RewriteCount;
        }
    }
}
