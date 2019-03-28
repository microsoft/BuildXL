// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions
{
    /// <summary>
    ///     Hint to the cache operations to allow an implementation
    ///     to adjust the order of processing within its core.
    /// </summary>
    /// <remarks>
    ///     It is perfectly fine to ignore the urgency hint but
    ///     using it may provide for better throughput.
    ///     Lower values are lower urgency.
    /// </remarks>
    public enum UrgencyHint
    {
        /// <summary>
        ///     Absolute minimum urgency - there is nothing below this
        /// </summary>
        Minimum = int.MinValue,

        /// <summary>
        ///     Low urgency - in the middle of the range between Nominal and Minimum
        /// </summary>
        Low = int.MinValue / 2,

        /// <summary>
        ///     Nominal urgency - the default urgency - middle of the total range
        /// </summary>
        Nominal = 0,

        /// <summary>
        ///     High urgency - in the middle of the range between Nominal and Maximum
        /// </summary>
        High = int.MaxValue / 2,

        /// <summary>
        ///     Absolute maximum urgency - there is nothing above this
        /// </summary>
        Maximum = int.MaxValue
    }
}
