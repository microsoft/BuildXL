// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities
{
    /// <summary>
    /// The type that allows only one value
    /// </summary>
    public readonly struct UnitValue : IEquatable<UnitValue>
    {
        /// <nodoc/>
        public static readonly UnitValue Unit = default(UnitValue);

        /// <nodoc/>
        public override int GetHashCode()
        {
            return 0;
        }

        /// <nodoc/>
        public bool Equals(UnitValue other)
        {
            return true;
        }
    }
}
