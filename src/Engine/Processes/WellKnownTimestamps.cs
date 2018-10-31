// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Processes
{
    /// <summary>
    /// Build processes sometimes inspect the timestamp of files in order to make decisions
    /// (such as whether or not to make a copy). This class contains timestamp constants
    /// as presented to build tools to prevent non-determinism.
    /// </summary>
    public static class WellKnownTimestamps
    {
        /// <summary>
        /// Timestamp of outputs that are to be rewritten in place. This timestamp is before <see cref="NewInputTimestamp" /> so
        /// that inputs appear newer.
        /// </summary>
        public static readonly DateTime OldOutputTimestamp = new DateTime(
            year: 2001,
            month: 1,
            day: 1,
            hour: 1,
            minute: 1,
            second: 1,
            kind: DateTimeKind.Utc);

        /// <summary>
        /// Timestamp of all inputs that exist on disk. This timestamp is after <see cref="OldOutputTimestamp" /> so that inputs
        /// appear newer.
        /// </summary>
        /// <remarks>
        /// Experimentally, this needs to be in the past. Otherwise, naughty date math tends to get angry. The CLR has been
        /// observed to reliably AV when an executable has a futuristic timestamp.
        /// </remarks>
        public static readonly DateTime NewInputTimestamp = new DateTime(
            year: 2002,
            month: 2,
            day: 2,
            hour: 2,
            minute: 2,
            second: 2,
            kind: DateTimeKind.Utc);

        /// <summary>
        /// Timestamp used to set the creation timestamp of all outputs that occur under a shared opaque
        /// </summary>
        /// <remarks>
        /// Outputs under shared opaques produced by a given pip need to be scrubbed before that pip runs again. Due to the dynamic nature
        /// of shared opaques, this is not known upfront. So preventively, and before a better solution arrives (e.g. containers), BuildXL
        /// scrubs all outputs under every shared opaque. This timestamp allows the scrubber to make sure it is not scrubbing any other file
        /// that is not a shared opaque output.
        /// </remarks>
        public static readonly DateTime OutputInSharedOpaqueTimestamp = new DateTime(
            year: 2003,
            month: 3,
            day: 3,
            hour: 3,
            minute: 3,
            second: 3,
            kind: DateTimeKind.Utc);
    }
}
