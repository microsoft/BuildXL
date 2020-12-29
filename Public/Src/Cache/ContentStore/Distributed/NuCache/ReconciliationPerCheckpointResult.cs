// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// A record that contains all the relevant information about reconciliation per checkpoint operation.
    /// </summary>
    public record ReconciliationPerCheckpointData
    {
        /// <nodoc />
        public int AddsSent { get; init; }

        /// <nodoc />
        public int RemovesSent { get; init; }

        /// <summary>
        /// Missing records on a db where the local content store has the content
        /// </summary>
        public int TotalMissingRecord { get; init; }

        /// <summary>
        /// Missing content on the local content store where the db has a record
        /// </summary>
        public int TotalMissingContent { get; init; }

        /// <summary>
        /// Hash range looked through to find add events
        /// </summary>
        public string? AddHashRange { get; init; }

        /// <summary>
        /// Hash range looked through to find remove events
        /// </summary>
        public string? RemoveHashRange { get; init; }

        /// <nodoc />
        public ShortHash? LastProcessedAddHash { get; init; }

        /// <nodoc />
        public ShortHash? LastProcessedRemoveHash { get; init; }

        /// <summary>
        /// Whether we went through the complete enumerable and reached the end for both adding and removing
        /// </summary>
        public bool ReachedEnd { get; init; }

        /// <summary>
        /// Count of hashes that were not reconciled due to being recently added 
        /// </summary>
        public int SkippedRecentAdds { get; init; }

        /// <summary>
        /// Count of hashes that were not reconciled due to being recently removed 
        /// </summary>
        public int SkippedRecentRemoves { get; init; }

        /// <summary>
        /// Count of hashes marked as recently added at time of reconciliation
        /// </summary>
        public int RecentAddCount { get; init; }

        /// <summary>
        /// Count of hashes marked as recently removed at time of reconciliation
        /// </summary>
        public int RecentRemoveCount { get; init; }

        /// <inheritdoc />
        public override string ToString()
        {
            // Changing the implementation by excluding the type name and '{' from the final result.
            var sb = new StringBuilder();
            PrintMembers(sb);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Result of reconciliation per checkpoint operation
    /// </summary>
    public class ReconciliationPerCheckpointResult : BoolResult
    {
        private readonly ReconciliationPerCheckpointData? _data;

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
        public static ReconciliationPerCheckpointResult Error(ResultBase other, string? message = null) => new ReconciliationPerCheckpointResult(other, message);

        /// <nodoc />
        public static ReconciliationPerCheckpointResult Skipped() => new ReconciliationPerCheckpointResult(ResultCode.Skipped, data: null);

        /// <nodoc />
        public static new ReconciliationPerCheckpointResult Success(ReconciliationPerCheckpointData data) =>
            new ReconciliationPerCheckpointResult(ResultCode.Success, data);

        private ReconciliationPerCheckpointResult(ResultCode code, ReconciliationPerCheckpointData? data)
        {
            _data = data;
            Code = code;
        }

        /// <inheritdoc />
        public ReconciliationPerCheckpointResult(ResultBase other, string? message = null)
            : base(other, message)
        {
        }

        [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(SuccessData))]
        public bool IsSuccessCode => Code == ResultCode.Success;

        public ReconciliationPerCheckpointData? SuccessData => _data;

        /// <inheritdoc />
        protected override string GetSuccessString()
        {
            switch (Code)
            {
                case ResultCode.Skipped:
                    return $"{base.GetSuccessString()} Code=[{Code}]";
                case ResultCode.Success:
                    return $"{base.GetSuccessString()} {_data}";
                default:
                    return GetErrorString();
            }
        }

        /// <inheritdoc />
        protected override string GetErrorString() => $"Code=[{Code}] {base.GetErrorString()}";
    }
}
