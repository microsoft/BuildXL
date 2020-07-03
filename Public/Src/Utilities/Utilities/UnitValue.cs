// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities
{
    /// <summary>
    /// The type that allows only one value
    /// </summary>
    public readonly struct UnitValue
    {
        /// <nodoc/>
        public static readonly UnitValue Unit = default(UnitValue);
    }
}
