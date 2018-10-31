// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Represents the data type of a <see cref="PipDataAtom"/>
    /// </summary>
    public enum PipDataAtomType : byte
    {
        /// <summary>
        /// Invalid, default value.
        /// </summary>
        Invalid = 0,

        /// <summary>
        /// A string literal.
        /// </summary>
        String,

        /// <summary>
        /// A string id.
        /// </summary>
        StringId,

        /// <summary>
        /// Absolute path.
        /// </summary>
        AbsolutePath,
    }
}
