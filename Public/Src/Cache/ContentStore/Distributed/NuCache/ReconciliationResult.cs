// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Result of reconciliation operation for customized result logging 
    /// </summary>
    public class ReconciliationResult : Result<(int addedCount, int removedCount, int totalLocalContentCount)>
    {
        /// <nodoc />
        public ReconciliationResult(int addedCount, int removedCount, int totalLocalContentCount) : base((addedCount, removedCount, totalLocalContentCount))
        {
        }

        /// <nodoc />
        public ReconciliationResult(ResultBase other, string message = null) : base(other, message)
        {
        }

        /// <inheritdoc />
        protected override string GetSuccessString()
        {
            return $"{base.GetSuccessString()} (Added={Value.addedCount}, Removed={Value.removedCount}, Total Local Content={Value.totalLocalContentCount})";
        }
    }
}
