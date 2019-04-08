// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Enumeration representing the types of pips.
    /// </summary>
    public enum PipType : byte
    {
        /// <summary>
        /// A write file pip.
        /// </summary>
        WriteFile,

        /// <summary>
        /// A copy file pip.
        /// </summary>
        CopyFile,

        /// <summary>
        /// A process pip.
        /// </summary>
        Process,

        /// <summary>
        /// A pip representing an IPC call (to some other service pip)
        /// </summary>
        Ipc,

        /// <summary>
        /// A value pip
        /// </summary>
        Value,

        /// <summary>
        /// A spec file pip
        /// </summary>
        SpecFile,

        /// <summary>
        /// A module pip
        /// </summary>
        Module,

        /// <summary>
        /// A pip representing the hashing of a source file
        /// </summary>
        HashSourceFile,

        /// <summary>
        /// A pip representing the completion of a directory (after which it is immutable).
        /// </summary>
        SealDirectory,

        /// <summary>
        /// This is a non-value, but places an upper-bound on the range of the enum
        /// </summary>
        Max,
    }

    /// <summary>
    /// Enumeration representing the types of seal directories.
    /// </summary>
    public enum SealDirectoryType : byte
    {
        /// <summary>
        /// A regular seal directory.
        /// </summary>
        SealDirectory,

        /// <summary>
        /// A shared opaque directory composed of other shared opaques
        /// </summary>
        CompositeSharedOpaqueDirectory,
    }

    /// <summary>
    /// Extension methods for PipType
    /// </summary>
    [Pure]
    public static class PipTypeExtensions
    {
        /// <summary>
        /// Returns true if this is a MetaPip
        /// </summary>
        public static bool IsMetaPip(this PipType pipType)
        {
            return pipType == PipType.Value || pipType == PipType.SpecFile || pipType == PipType.Module;
        }
    }
}
