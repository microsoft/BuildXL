// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Base implementation for maintaining content quota.
    /// </summary>
    public abstract class QuotaRule : IQuotaRule
    {
        /// <summary>
        /// Definition for content quota.
        /// </summary>
        protected ContentStoreQuota _quota;

        /// <inheritdoc />
        public bool OnlyUnlinked { get; }

        /// <inheritdoc />
        public ContentStoreQuota Quota => _quota;

        /// <summary>
        ///     Initializes a new instance of the <see cref="QuotaRule" /> class.
        /// </summary>
        protected QuotaRule(bool onlyUnlinked)
        {
            OnlyUnlinked = onlyUnlinked;
        }

        /// <summary>
        /// Checks if reserve size is inside limit.
        /// </summary>
        public abstract BoolResult IsInsideLimit(long limit, long reserveCount);

        /// <inheritdoc />
        public virtual BoolResult IsInsideHardLimit(long reserveSize = 0)
        {
            return IsInsideLimit(_quota.Hard, reserveSize);
        }

        /// <inheritdoc />
        public virtual BoolResult IsInsideSoftLimit(long reserveSize = 0)
        {
            return IsInsideLimit(_quota.Soft, reserveSize);
        }

        /// <inheritdoc />
        public virtual BoolResult IsInsideTargetLimit(long reserveSize = 0)
        {
            return IsInsideLimit(_quota.Target, reserveSize);
        }

        /// <inheritdoc />
        public virtual bool CanBeCalibrated => false;

        /// <inheritdoc />
        public virtual Task<CalibrateResult> CalibrateAsync()
        {
            return Task.FromResult(CalibrateResult.CannotCalibrate);
        }

        /// <inheritdoc />
        public virtual bool IsEnabled
        {
            get
            {
                // Always enabled.
                return true;
            }

            set
            {
                // Do nothing.
            }
        }
    }
}
