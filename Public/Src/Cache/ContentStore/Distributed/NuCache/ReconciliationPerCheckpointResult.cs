// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Result of reconciliation per checkpoint operation
    /// </summary>
    public class ReconciliationPerCheckpointResult : BoolResult
    { 
        /// <nodoc />
        public enum ResultCode
        {
            /// <nodoc />
            Error,

            /// <nodoc />
            Skipped,

            /// <nodoc />
            Success
        }

        /// <nodoc />
        public ResultCode Code { get; }

        /// <nodoc />
        public int AddsSent { get; }

        /// <nodoc />
        public int RemovesSent { get; }

        /// <summary>
        /// Missing records on a db where the local content store has the content
        /// </summary>
        public int TotalMissingRecord { get; }

        /// <summary>
        /// Missing content on the local content store where the db has a record
        /// </summary>
        public int TotalMissingContent { get; }

        /// <summary>
        /// Hash range looked through to find add events
        /// </summary>
        public string AddHashRange { get; }

        /// <summary>
        /// Hash range looked through to find remove events
        /// </summary>
        public string RemoveHashRange { get; }

        /// <nodoc />
        public ShortHash? LastProcessedAddHash { get; }

        /// <nodoc />
        public ShortHash? LastProcessedRemoveHash { get; }

        /// <summary>
        /// Whether we went through the complete enumerable and reached the end for both adding and removing
        /// </summary>
        public bool ReachedEnd { get; }

        /// <summary>
        /// Count of hashes that were not reconciled due to being recently added 
        /// </summary>
        public int SkippedRecentAdds { get; }

        /// <summary>
        /// Count of hashes that were not reconciled due to being recently removed 
        /// </summary>
        public int SkippedRecentRemoves { get; }

        /// <summary>
        /// Count of hashes marked as recently added at time of reconciliation
        /// </summary>
        public int RecentAddCount { get; }

        /// <summary>
        /// Count of hashes marked as recently removed at time of reconciliation
        /// </summary>
        public int RecentRemoveCount { get; }

        /// <summary>
        /// Traced sample of reconcile hashes that are limited by a config amount, includes add and remove hashes.
        /// </summary>
        public List<ShortHash> SampleHashes { get; }


        /// <nodoc />
        public static ReconciliationPerCheckpointResult Error(ResultBase other, string message = null) => new ReconciliationPerCheckpointResult(other, message);

        /// <nodoc />
        public static ReconciliationPerCheckpointResult Skipped() => new ReconciliationPerCheckpointResult(ResultCode.Skipped);

        /// <nodoc />
        public static new ReconciliationPerCheckpointResult Success(int addedCount, int removedCount, int totalAdds, int totalRemoves, string addHashRange, string removeHashRange, ShortHash? lastProcessedAddHash, ShortHash? lastProcessedRemoveHash, bool reachedEnd, int skippedRecentAdds, int skippedRecentRemoves, int recentAddCount, int recentRemoveCount, List<ShortHash> sampleHashes)
            => new ReconciliationPerCheckpointResult(ResultCode.Success, addedCount, removedCount, totalAdds, totalRemoves, addHashRange, removeHashRange, lastProcessedAddHash, lastProcessedRemoveHash, reachedEnd, skippedRecentAdds, skippedRecentRemoves, recentAddCount, recentRemoveCount, sampleHashes);

        private ReconciliationPerCheckpointResult(ResultCode code)
        {
            Code = code;
        }

        /// <nodoc />
        private ReconciliationPerCheckpointResult(ResultCode code, int addedCount, int removedCount, int totalAdds, int totalRemoves)
            : this(code)
        {
            AddsSent = addedCount;
            RemovesSent = removedCount;
            TotalMissingRecord = totalAdds;
            TotalMissingContent = totalRemoves;
        }

        private ReconciliationPerCheckpointResult(ResultCode code, int addedCount, int removedCount, int totalAdds, int totalRemoves, string addHashRange, string removeHashRange,
            ShortHash? lastProcessedAddHash, ShortHash? lastProcessedRemoveHash, bool reachedEnd, int skippedRecentAdds, int skippedRecentRemoves, int recentAddCount, int recentRemoveCount, List<ShortHash> sampleHashes)
            : this(code, addedCount, removedCount, totalAdds, totalRemoves)
        {
            AddHashRange = addHashRange;
            RemoveHashRange = removeHashRange;
            LastProcessedAddHash = lastProcessedAddHash;
            LastProcessedRemoveHash = lastProcessedRemoveHash;
            ReachedEnd = reachedEnd;
            SkippedRecentAdds = skippedRecentAdds;
            SkippedRecentRemoves = skippedRecentRemoves;
            RecentAddCount = recentAddCount;
            RecentRemoveCount = recentRemoveCount;
        }

        /// <nodoc />
        public ReconciliationPerCheckpointResult(ResultBase other, string message = null) : base(other, message)
        {
            Code = ResultCode.Error;
        }

        /// <inheritdoc />
        protected override string GetSuccessString()
        {
            switch (Code)
            {
                case ResultCode.Skipped:
                    return $"{base.GetSuccessString()} Code=[{Code}]";
                case ResultCode.Success:
                    return $"{base.GetSuccessString()} AddsSent=[{AddsSent}] RemovesSent=[{RemovesSent}] TotalMissingRecord=[{TotalMissingRecord}] TotalMissingContent=[{TotalMissingContent}] " +
                        $"AddHashRange=[{AddHashRange}] RemoveHashRange[{RemoveHashRange}] SkippedRecentAdds=[{SkippedRecentAdds}] SkippedRecentRemoves=[{SkippedRecentRemoves}] RecentAdds=[{RecentAddCount}] RecentRemoves=[{RecentRemoveCount}] SampleHashes=[{SampleHashes}]";
                default:
                    return $"{base.GetSuccessString()} Code=[{Code}]";
            }
        }

        /// <inheritdoc />
        protected override string GetErrorString() => $"Code=[{Code}] {base.GetErrorString()}";
    }
}
