// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Return result enum for ValidateContent
    /// </summary>
    public enum ValidateContentStatus
    {
        /// <summary>
        /// This cache could not validate the CasHash or has no way
        /// to do this validation
        /// </summary>
        /// <remarks>
        /// This is usually only returned for caches that do not
        /// support this feature or can not implement this feature.
        /// </remarks>
        NotSupported = 0,

        /// <summary>
        /// The CasHash element hashed to the expected value
        /// </summary>
        /// <remarks>
        /// In a case where one thought that the entry was corrupted
        /// and then called the ValidateContent API, this would signal
        /// that the corruption has since been fixed or that the
        /// corruptions was somewhere in the transmision of the data.
        /// In either case, it would likely be worth trying again.
        /// </remarks>
        Ok,

        /// <summary>
        /// The CasHash element did not hash to the expected value
        /// and the invalid element was removed (or fixed).
        /// </summary>
        /// <remarks>
        /// This result is also reported if there is no CasHash item
        /// at that hash.  This is because that is the expected case
        /// for when an item was remediated and if two or more clients
        /// are validating the same CasHash there is a race as to which
        /// one got to remove the invalid entry.  This allows the user
        /// to not care about the race semantically.
        /// </remarks>
        Remediated,

        /// <summary>
        /// The CasHash element does not hash to the expected value
        /// and the invalid element was not removed or fixed due to
        /// whatever constraints may be involved (read-only cache, etc)
        /// </summary>
        /// <remarks>
        /// In these conditions, the cache should do its best to
        /// signal that maintenance is needed on this item since this
        /// leaves a known corrupted CAS element in the CAS.
        /// </remarks>
        Invalid,
    }
}
