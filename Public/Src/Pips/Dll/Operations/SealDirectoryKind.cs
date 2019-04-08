// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Seal directory kind.
    /// </summary>
    public enum SealDirectoryKind : byte
    {
        /// <summary>
        /// Full Seal. The content of directory is fully known and cannot further change.
        /// </summary>
        Full,

        /// <summary>
        /// Partial Seal. The content of directory is only partially known, and this Pip only represent a partial view. Underlying directory can still change.
        /// </summary>
        Partial,

        /// <summary>
        /// Source Seal with recursive permission. No content is statically known, and will be known only during runtime.
        /// </summary>
        SourceAllDirectories,

        /// <summary>
        /// Source Seal that only only files in that folder, not recursive. No content is statically known, and will be known only during runtime.
        /// </summary>
        SourceTopDirectoryOnly,

        /// <summary>
        /// Opaque directory. No content is statically known, and will be known only during runtime.
        /// </summary>
        Opaque,

        /// <summary>
        /// Shared opaque directory. No content is statically known, and will be known only during runtime. It can be shared by multiple producers.
        /// </summary>
        SharedOpaque,
    }
}
