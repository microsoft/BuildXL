// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Sessions;

namespace BuildXL.Cache.ContentStore.Interfaces.Extensions
{
    /// <summary>
    ///     Useful extensions to UrgencyHint
    /// </summary>
    public static class UrgencyHintExtensions
    {
        /// <summary>
        /// Convert a number into an UrgencyHint
        /// </summary>
        /// <remarks>Lower values are lower urgency</remarks>
        public static UrgencyHint ToUrgencyHint(this int hintValue)
        {
            return (UrgencyHint)hintValue;
        }

        /// <summary>
        /// Convert an UrgencyHint to a number
        /// </summary>
        /// <remarks>Lower values are lower urgency</remarks>
        public static int ToValue(this UrgencyHint hint)
        {
            return (int)hint;
        }
    }
}
