// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Result of the Purge call.
    /// </summary>
    public class PurgeResult : BoolResult
    {
        private long? _reserveSize;
        private int? _hashesToPurgeCount;
        private string _quotaDescription;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PurgeResult"/> class.
        /// </summary>
        public PurgeResult()
        {
        }

        /// <nodoc />
        public PurgeResult(long reserveSize, int hashesToPurgeCount, string quotaDescription)
        {
            _reserveSize = reserveSize;
            _hashesToPurgeCount = hashesToPurgeCount;
            _quotaDescription = quotaDescription;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PurgeResult"/> class.
        /// </summary>
        public PurgeResult(string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Contract.Requires(errorMessage != null);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PurgeResult"/> class.
        /// </summary>
        public PurgeResult(ResultBase other, string message = null)
            : base(other, message)
        {
        }

        /// <summary>
        /// Number of files visited during a purge operation.
        /// </summary>
        public long VisitedFiles { get; set; }

        /// <summary>
        /// The reason why a successful eviction process has stopped.
        /// </summary>
        public string StopReason { get; set; }

        /// <summary>
        ///     Gets number of bytes evicted.
        /// </summary>
        public long EvictedSize { get; private set; }

        /// <summary>
        ///     Gets number of files evicted.
        /// </summary>
        public long EvictedFiles { get; private set; }

        /// <summary>
        ///     Gets byte count remaining pinned across all replicas for associated content hash.
        /// </summary>
        public long PinnedSize { get; private set; }

        /// <summary>
        /// Gets or sets the size of the entire content.
        /// </summary>
        public long CurrentContentSize { get; set; }

        /// <summary>
        ///     Merge in results from an eviction.
        /// </summary>
        public void Merge(EvictResult evictResult)
        {
            VisitedFiles++;
            EvictedSize += evictResult.EvictedSize;
            EvictedFiles += evictResult.EvictedFiles;
            PinnedSize += evictResult.PinnedSize;
        }

        /// <summary>
        ///     Merge in results from another purge.
        /// </summary>
        public void Merge(PurgeResult result)
        {
            VisitedFiles += result.VisitedFiles;
            EvictedSize += result.EvictedSize;
            EvictedFiles += result.EvictedFiles;
            PinnedSize += result.PinnedSize;

            _quotaDescription = _quotaDescription ?? result._quotaDescription;
            StopReason = StopReason ?? result.StopReason;

            if (result._reserveSize != null)
            {
                _reserveSize = result._reserveSize + (_reserveSize ?? 0);
            }

            if (result._hashesToPurgeCount != null)
            {
                _hashesToPurgeCount = result._hashesToPurgeCount + (_hashesToPurgeCount ?? 0);
            }
        }

        /// <summary>
        ///     Merge pinned size.
        /// </summary>
        public void MergePinnedSize(long pinnedSize)
        {
            PinnedSize += pinnedSize;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (Succeeded)
            {
                var result = $"Success EvictedSize={EvictedSize} EvictedFiles={EvictedFiles} PinnedSize={PinnedSize} CurrentContentSize={CurrentContentSize}";
                if (_quotaDescription != null)
                {
                    result += $" StopReason={StopReason} HashesToPurgeCount={_hashesToPurgeCount} VisitedFiles={VisitedFiles} Quota={_quotaDescription} ReserveSize={_reserveSize}";
                }

                return result;
            }
            else
            {
                return GetErrorString();
            }
        }

        
    }
}
