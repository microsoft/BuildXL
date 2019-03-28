// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Types
{
    /// <summary>
    /// Kinds of primitive type
    /// </summary>
    public enum PrimitiveTypeKind : byte
    {
        /// <summary>
        /// Any.
        /// </summary>
        Any,

        /// <summary>
        /// Number.
        /// </summary>
        Number,

        /// <summary>
        /// Boolean.
        /// </summary>
        Boolean,

        /// <summary>
        /// String.
        /// </summary>
        String,

        /// <summary>
        /// Void.
        /// </summary>
        Void,
    }
}
