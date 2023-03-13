// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Result of proactive replication for customized result logging 
    /// </summary>
    public class ProactiveReplicationResult : ResultBase
    {
        /// <nodoc />
        public required int SuccessCount { get; init; }

        /// <nodoc />
        public required int FailCount { get; init; }

        /// <nodoc />
        public required int SkippedCount { get; init; }

        /// <nodoc />
        public int TotalCount => SuccessCount + FailCount + SkippedCount + RejectedCount;
        
        /// <nodoc />
        public required int RejectedCount { get; init; }

        /// <nodoc />
        public required int TotalLocalContent { get; init; }

        /// <nodoc />
        public required int TotalContentScanned { get; init; }

        /// <nodoc />
        public required ContentEvictionInfo? LastVisited { get; init; }

        /// <nodoc />
        [SetsRequiredMembers]
        public ProactiveReplicationResult(int succeeded, int failed, int skipped, int rejected, int totalLocalContent, int totalContentScanned, ContentEvictionInfo? lastVisited)
        {
            SuccessCount = succeeded;
            FailCount = failed;
            RejectedCount = rejected;
            SkippedCount = skipped;
            TotalLocalContent = totalLocalContent;
            TotalContentScanned = totalContentScanned;
            LastVisited = lastVisited;
        }

        /// <nodoc />
        public ProactiveReplicationResult()
        { }

        /// <nodoc />
        public ProactiveReplicationResult(ResultBase other, string message = null) : base(other, message)
        {
        }

        /// <inheritdoc />
        protected override string GetSuccessString() => $"{base.GetSuccessString()} (Succeeded={SuccessCount}, Failed={FailCount}, Skipped={SkippedCount}, Rejected={RejectedCount}, Total={SuccessCount + FailCount}, TotalContentScanned={TotalContentScanned}, TotalLocalContent={TotalLocalContent}, LastVisited=[{LastVisited}])";
    }
}
