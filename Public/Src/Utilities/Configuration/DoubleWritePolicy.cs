// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Policy to be applied when a process incurs in a double write
    /// </summary>
    public enum DoubleWritePolicy : byte
    {
        /// <summary>
        /// Double writes are blocked
        /// </summary>
        DoubleWritesAreErrors,

        /// <summary>
        /// Double writes are allowed as long as the file content is the same
        /// </summary>
        AllowSameContentDoubleWrites,

        /// <summary>
        /// Double writes are allowed and the first output will (non-deterministically) represent the final
        /// output
        /// </summary>
        /// <remarks>
        /// This option is unsafe since it introduces non-deterministic behavior.
        /// </remarks>
        UnsafeFirstDoubleWriteWins
    }

    /// <nodoc/>
    public static class DoubleWritePolicyExtensions
    {
        /// <summary>
        /// Whether the double-write policy implies that double writes should be flagged as a warning (as opposed to an error)
        /// </summary>
        public static bool ImpliesDoubleWriteIsWarning(this DoubleWritePolicy policy)
        {
            switch (policy)
            {
                case DoubleWritePolicy.DoubleWritesAreErrors:
                case DoubleWritePolicy.AllowSameContentDoubleWrites:
                    return false;
                case DoubleWritePolicy.UnsafeFirstDoubleWriteWins:
                    return true;
                default:
                    throw new InvalidOperationException("Unexpected double write policy " + policy.ToString());
            }
        }

        /// <summary>
        /// Whether the double-write policy implies that double writes are possible without implying a build break
        /// </summary>
        public static bool ImpliesDoubleWriteCanHappen(this DoubleWritePolicy policy)
        {
            switch (policy)
            {
                case DoubleWritePolicy.DoubleWritesAreErrors:
                    return false;
                case DoubleWritePolicy.AllowSameContentDoubleWrites:
                case DoubleWritePolicy.UnsafeFirstDoubleWriteWins:
                    return true;
                default:
                    throw new InvalidOperationException("Unexpected double write policy " + policy.ToString());
            }
        }
    }
}
