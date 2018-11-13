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
}
