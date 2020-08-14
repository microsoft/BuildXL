// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <nodoc />
    public sealed class DistributedPinResult : PinResult
    {
        /// <summary>
        /// A code for different results in copy operations
        /// </summary>
        public enum DistributedPinResultCode
        {
            /// <summary>
            /// Enough replicas to not copy
            /// </summary>
            EnoughReplicas,

            /// <summary>
            /// Copied synchronously
            /// </summary>
            Copy,

            /// <summary>
            /// Copied Asynchronously
            /// </summary>
            CopyAsync,

            /// <summary>
            /// Failure to copy due to an error
            /// </summary>
            Fail,
        }

        private readonly string? _extraMessage;

        /// <summary>
        /// True when the remote pin succeeded after the content was copied locally.
        /// </summary>
        public bool CopyLocally => DistributedPinCode == DistributedPinResultCode.Copy;

        /// <nodoc />
        public DistributedPinResultCode DistributedPinCode { get; private set; }

        /// <nodoc />
        public int ReplicaCount { get; private set; }

        private DistributedPinResult(DistributedPinResultCode code, int replicaCount, string? extraMessage = null, ResultCode internalCode = ResultCode.Success)
            : base(internalCode)
        {
            DistributedPinCode = code;
            ReplicaCount = replicaCount;
            _extraMessage = extraMessage;
        }

        /// <nodoc />
        public DistributedPinResult(int replicaCount, ResultBase other, string? message = null)
            : this(other, message)
        {
            DistributedPinCode = DistributedPinResultCode.Fail;
            ReplicaCount = replicaCount;
        }

        /// <nodoc />
        public DistributedPinResult(ResultBase other, string? message = null)
            : base(other, message)
        {
        }

        /// <nodoc />
        public static DistributedPinResult EnoughReplicas(int replicaCount, string? extraMessage = null) => new DistributedPinResult(DistributedPinResultCode.EnoughReplicas, replicaCount, extraMessage);

        /// <nodoc />
        public static DistributedPinResult SynchronousCopy(int replicaCount, string? extraMessage = null) => new DistributedPinResult(DistributedPinResultCode.Copy, replicaCount, extraMessage);

        /// <nodoc />
        public static DistributedPinResult AsynchronousCopy(int replicaCount, string? extraMessage = null) => new DistributedPinResult(DistributedPinResultCode.CopyAsync, replicaCount, extraMessage);

        /// <nodoc />
        public static new DistributedPinResult ContentNotFound(int replicaCount, string? extraMessage = null) => new DistributedPinResult(DistributedPinResultCode.Fail, replicaCount, extraMessage, ResultCode.ContentNotFound);

        /// <inheritdoc />
        protected override string GetSuccessString()
        {
            return $"{DistributedPinCode}/{ReplicaCount}" + (string.IsNullOrEmpty(_extraMessage) ? "" : $" ({_extraMessage})");
        }

        /// <inheritdoc />
        protected override string GetErrorString()
        {
            return $"{DistributedPinCode}/{ReplicaCount} " + base.GetErrorString() + (string.IsNullOrEmpty(_extraMessage) ? string.Empty : $" ({_extraMessage})");
        }
    }
}
