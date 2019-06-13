// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
