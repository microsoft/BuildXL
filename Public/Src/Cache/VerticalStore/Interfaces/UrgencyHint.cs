// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// This is the hint that may be given to the cache operations
    /// to potentially allow an implementation to adjust the order
    /// of processing within its core.  It is perfectly fine to
    /// ignore the urgency hint but using it may provide for better
    /// throughput.
    /// </summary>
    /// <remarks>Lower values are lower urgency</remarks>
    public enum UrgencyHint : int
    {
        /// <summary>
        /// Absolute minimum urgency - there is nothing below this
        /// </summary>
        Minimum = int.MinValue,

        /// <summary>
        /// Low urgency - in the middle of the range between Nominal and Minimum
        /// </summary>
        Low = int.MinValue / 2,

        /// <summary>
        /// Nominal urgency - the default urgency - middle of the total range
        /// </summary>
        Nominal = 0,

        /// <summary>
        /// High urgency - in the middle of the range between Nominal and Maximum
        /// </summary>
        High = int.MaxValue / 2,

        /// <summary>
        /// Absolute maximum urgency - there is nothing above this
        /// </summary>
        Maximum = int.MaxValue,
    }

    /// <summary>
    /// Helper extension methods for UrgencyHint enum.
    /// </summary>
    public static class UrgencyHintHelper
    {
        /// <summary>
        /// Convert a number into an UrgencyHint
        /// </summary>
        /// <param name="hintValue">Number value to be converted</param>
        /// <returns>UrgencyHint</returns>
        /// <remarks>Lower values are lower urgency</remarks>
        public static UrgencyHint AsUrgencyHint(this int hintValue)
        {
            return (UrgencyHint)hintValue;
        }

        /// <summary>
        /// Convert an UrgencyHint to a number
        /// </summary>
        /// <param name="hint">The hint</param>
        /// <returns>A number that represents the hint</returns>
        /// <remarks>Lower values are lower urgency</remarks>
        public static int AsValue(this UrgencyHint hint)
        {
            return (int)hint;
        }
    }
}
