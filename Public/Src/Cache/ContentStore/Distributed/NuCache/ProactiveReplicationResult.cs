// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        public int SuccessCount { get; }

        /// <nodoc />
        public int FailCount { get; }

        /// <nodoc />
        public int RejectedCount { get; }

        /// <nodoc />
        public int TotalLocalContent { get; }

        /// <nodoc />
        public int TotalContentScanned { get; }

        /// <nodoc />
        public ContentEvictionInfo? LastVisited { get; }

        /// <nodoc />
        public ProactiveReplicationResult(int succeeded, int failed, int rejected, int totalLocalContent, int totalContentScanned, ContentEvictionInfo? lastVisited)
        {
            SuccessCount = succeeded;
            FailCount = failed;
            RejectedCount = rejected;
            TotalLocalContent = totalLocalContent;
            TotalContentScanned = totalContentScanned;
            LastVisited = lastVisited;
        }

        /// <nodoc />
        public ProactiveReplicationResult(ResultBase other, string message = null) : base(other, message)
        {
        }

        /// <inheritdoc />
        protected override string GetSuccessString() => $"{base.GetSuccessString()} (Succeeded={SuccessCount}, Failed={FailCount}, Rejected={RejectedCount}, Total={SuccessCount + FailCount}, TotalContentScanned={TotalContentScanned}, TotalLocalContent={TotalLocalContent}, LastVisited=[{LastVisited}])";
    }
}
