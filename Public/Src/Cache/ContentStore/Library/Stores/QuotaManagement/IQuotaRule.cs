// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;

namespace BuildXL.Cache.ContentStore.Stores
{
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
        public static bool GetOnlyUnlinked(this IQuotaRule? rule)
        {
            return rule?.OnlyUnlinked ?? false;
        }
    }
}
