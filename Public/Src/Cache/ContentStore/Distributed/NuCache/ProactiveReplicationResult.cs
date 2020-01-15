// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Results;

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
        public int TotalLocalContent { get; }

        /// <nodoc />
        public int TotalContentScanned { get; }

        /// <nodoc />
        public ProactiveReplicationResult(int succeeded, int failed, int totalLocalContent, int totalContentScanned)
        {
            SuccessCount = succeeded;
            FailCount = failed;
            TotalLocalContent = totalLocalContent;
            TotalContentScanned = totalContentScanned;
        }

        /// <nodoc />
        public ProactiveReplicationResult(ResultBase other, string message = null) : base(other, message)
        {
        }

        /// <inheritdoc />
        protected override string GetSuccessString() => $"{base.GetSuccessString()} (Succeeded={SuccessCount}, Failed={FailCount}, Total={SuccessCount + FailCount}, TotalContentScanned={TotalContentScanned}, TotalLocalContent={TotalLocalContent})";
    }
}
