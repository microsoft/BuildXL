// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Enumeration representing the ways a composite directory can be constructed.
    /// </summary>
    public enum SealDirectoryCompositionActionKind : byte
    {
        /// <summary>
        /// Represents a non-composite directory.
        /// </summary>
        None,

        /// <summary>
        /// Group all the files together and place them under the root that contains all of the composed directories.
        /// </summary>
        WidenDirectoryCone,

        /// <summary>
        /// Include only those files that are under a new root (that is a subdirectory of an existing directory)
        /// </summary>
        NarrowDirectoryCone,
    }
}
