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
        /// A process pip.
        /// </summary>
        Process,

        /// <summary>
        /// A value pip
        /// </summary>
        Value,

        /// <summary>
        /// This is a non-value, but places an upper-bound on the range of the enum
        /// </summary>
        Max,
    }
}
