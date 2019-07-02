// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Types
{
    /// <summary>
    /// Kinds of primitive type
    /// </summary>
    public enum PrimitiveTypeKind : byte
    {
        /// <nodoc/>
        Any,

        /// <nodoc/>
        Number,

        /// <nodoc/>
        Boolean,

        /// <nodoc/>
        String,

        /// <nodoc/>
        Void,

        /// <nodoc/>
        Unit,
    }
}
