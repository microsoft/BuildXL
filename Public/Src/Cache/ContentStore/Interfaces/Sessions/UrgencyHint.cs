// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// Indicates to Put calls that content should not be registered eagerly.
        /// This is typically used with <see cref="RegisterAssociatedContent"/> in
        /// AddOrGetContentHashList calls
        /// </summary>
        SkipRegisterContent = -1,

        /// <summary>
        /// Nominal urgency - the default urgency - middle of the total range
        /// </summary>
        Nominal = 0,

        /// <summary>
        /// Indicates to AddOrGetContentHashList that associated content should be
        /// registered with the central content tracker. This is typically used alongsize
        /// <see cref="SkipRegisterContent"/> when putting the content.
        /// </summary>
        RegisterAssociatedContent = 1,

        /// <summary>
        ///     High urgency - in the middle of the range between Nominal and Maximum
        /// </summary>
        High = int.MaxValue / 2,

        /// <summary>
        ///     Absolute maximum urgency - there is nothing above this
        /// </summary>
        Maximum = int.MaxValue,

        /// <summary>
        ///     A hint that the cache should prefer shared datastore instead of using a local one.
        ///     Used only by memoization stores.
        /// </summary>
        PreferShared = Maximum,
    }
}
