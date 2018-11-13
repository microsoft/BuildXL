// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Enumeration representing the types of values that can be contained in a fragment.
    /// </summary>
    public enum PipFragmentType : byte
    {
        /// <summary>
        /// Invalid, default value.
        /// </summary>
        Invalid = 0,

        /// <summary>
        /// A string literal.
        /// </summary>
        StringLiteral,

        /// <summary>
        /// Absolute file or directory path.
        /// </summary>
        AbsolutePath,

        /// <summary>
        /// A nested description fragment.
        /// </summary>
        NestedFragment,
    }
}
