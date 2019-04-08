// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Quota rule limiting size to a specific limit.
    /// </summary>
    public class MaxSizeRule : QuotaRule
    {
        /// <summary>
        ///     Name of rule for logging
        /// </summary>
        public const string MaxSizeRuleName = "MaxSize";

        // Removing linked content does make progress toward the cache size.
        private const bool OnlyUnlinkedValue = false;

        private readonly Func<long> _getCurrentSizeFunc;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MaxSizeRule"/> class.
        /// </summary>
        public MaxSizeRule(MaxSizeQuota quota, EvictAsync evictAsync, Func<long> getCurrentSizeFunc, DistributedEvictionSettings distributedEvictionSettings = null)
            : base(evictAsync, OnlyUnlinkedValue, distributedEvictionSettings)
        {
            Contract.Requires(quota != null);
            Contract.Requires(evictAsync != null);
            Contract.Requires(getCurrentSizeFunc != null);

            _quota = quota;
            _getCurrentSizeFunc = getCurrentSizeFunc;
        }

        /// <summary>
        ///     Check if reserve count is inside limit.
        /// </summary>
        public override BoolResult IsInsideLimit(long limit, long reserveCount)
        {
            var currentSize = _getCurrentSizeFunc();
            var finalSize = currentSize + reserveCount;

            if (finalSize > limit)
            {
                return new BoolResult($"Exceeds {limit} bytes when adding {reserveCount} bytes. Configuration: [Rule={MaxSizeRuleName} {_quota}]");
            }

            return BoolResult.Success;
        }
    }
}
