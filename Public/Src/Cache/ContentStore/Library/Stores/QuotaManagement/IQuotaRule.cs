// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Check content tracker for last access time.
    /// </summary>
    public delegate Task<ObjectResult<IList<ContentHashWithLastAccessTimeAndReplicaCount>>> TrimOrGetLastAccessTimeAsync(Context context, IList<Tuple<ContentHashWithLastAccessTimeAndReplicaCount, bool>> contentHashesWithInfo, CancellationToken cts, UrgencyHint urgencyHint);

    /// <summary>
    ///     Common interface for all quota rules.
    /// </summary>
    public interface IQuotaRule
    {
        /// <summary>
        ///     Check if current size plus given reservation is under the hard limit.
        /// </summary>
        BoolResult IsInsideHardLimit(long reserveSize = 0);

        /// <summary>
        ///     Check if current size plus given reservation is under the soft limit.
        /// </summary>
        BoolResult IsInsideSoftLimit(long reserveSize = 0);

        /// <summary>
        ///     Check if current size plus given reservation is under the target limit.
        /// </summary>
        BoolResult IsInsideTargetLimit(long reserveSize = 0);

        /// <summary>
        ///     Purge content in LRU-ed order according to limits.
        /// </summary>
        Task<PurgeResult> PurgeAsync(Context context, long reserveSize, IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo, CancellationToken token);

        /// <summary>
        ///     Gets a value indicating whether quota can be calibrated.
        /// </summary>
        bool CanBeCalibrated { get; }

        /// <summary>
        ///     Calibrate current quota if quota can be calibrated.
        /// </summary>
        Task<CalibrateResult> CalibrateAsync();

        /// <summary>
        ///     Gets or sets a value indicating whether quota rule is enabled.
        /// </summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// Whether or not to remove only unlinked content.
        /// </summary>
        bool OnlyUnlinked { get; }

        /// <summary>
        /// Gets quota that the rule should enforce.
        /// </summary>
        ContentStoreQuota Quota { get; }
    }

    /// <nodoc />
    public static class QuotaRuleExtensions
    {
        /// <nodoc />
        public static bool GetOnlyUnlinked(this IQuotaRule /*Can Be Null*/ rule)
        {
            return rule?.OnlyUnlinked ?? false;
        }
    }
}
