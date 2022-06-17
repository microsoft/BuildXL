// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Service.Grpc;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <summary>
    /// Represents a result of a proactive copy.
    /// </summary>
    /// <remarks>
    /// The operation is considered unsuccessful only when both the operations (inside the ring or outside the ring)
    /// ended up with an error.
    /// </remarks>
    public class ProactiveCopyResult : ResultBase
    {
        private readonly Error? _error;

        public int SuccessCount
        {
            get
            {
                int result = 0;
                if (InsideRingCopyResult != null && InsideRingCopyResult.Succeeded && !InsideRingCopyResult.Skipped)
                {
                    result += 1;
                }

                if (OutsideRingCopyResult != null && OutsideRingCopyResult.Succeeded && !OutsideRingCopyResult.Skipped)
                {
                    result += 1;
                }

                return result;
            }
        }

        public bool Skipped { get; }

        /// <nodoc />
        public ProactivePushResult? InsideRingCopyResult { get; }

        /// <nodoc />
        public ProactivePushResult? OutsideRingCopyResult { get; }

        /// <inheritdoc />
        public override Error? Error
        {
            get
            {
                return IsSuccess() ? null : (base.Error ?? _error);
            }
        }

        /// <nodoc />
        public ContentLocationEntry? Entry { get; }

        /// <summary>
        /// Total Number of retries in a Proactive Copy Task
        /// This represents the sum of total retries made by ProactiveCopyInsideBuildRing and ProactiveCopyInsideBuildRing Task
        /// Retry here is different than Attempts (i.e If the copy is successful in first attempt, retry count is 0)
        /// </summary>
        public int TotalRetries { get; }

        /// <nodoc />
        public static ProactiveCopyResult CopyNotRequiredResult { get; } = new ProactiveCopyResult();

        private ProactiveCopyResult()
        {
            Skipped = true;
        }

        /// <nodoc />
        public ProactiveCopyResult(ProactivePushResult insideRingCopyResult, ProactivePushResult outsideRingCopyResult, int retries, ContentLocationEntry? entry = null)
        {
            InsideRingCopyResult = insideRingCopyResult;
            OutsideRingCopyResult = outsideRingCopyResult;
            TotalRetries = retries;
            Entry = entry ?? ContentLocationEntry.Missing;

            Skipped = insideRingCopyResult.Skipped && outsideRingCopyResult.Skipped;

            // Proactive copy is considered failed only when both inside and outside ring copyresult fails
            if (!Skipped & SuccessCount == 0)
            {
                var error = GetErrorString();
                _error = Error.FromErrorMessage(error!);
            }
        }

        /// <nodoc />
        public ProactiveCopyResult(ResultBase other, string? message = null)
            : base(other, message)
        {
        }

        /// <inheritdoc />
        protected override string GetSuccessString()
        {
            return GetResultString();
        }

        /// <inheritdoc />
        protected override string GetErrorString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(GetResultString());
            if (InsideRingCopyResult?.PushFileResult?.Succeeded == false)
            {
                sb.Append($"InsideRingError=[{InsideRingCopyResult.PushFileResult.ErrorMessage}] ");
            }

            if (OutsideRingCopyResult?.PushFileResult?.Succeeded == false)
            {
                sb.Append($"OutsideRingError=[{OutsideRingCopyResult.PushFileResult.ErrorMessage}] ");
            }

            return sb.ToString();
        }

        /// <summary>
        /// This is the common log message for all possible outcome of a proactive copy
        /// When a Copy Result is "Not Applicable", it means that Copy was never attempted, hence logging a status for this would be misleading
        /// </summary>
        private string GetResultString()
        {
            return
                $"Success Count=[{SuccessCount}] " +
                $"InsideRing=[{(InsideRingCopyResult?.Status ?? "Not Applicable")}], " +
                $"OutsideRing=[{(OutsideRingCopyResult?.Status ?? "Not Applicable")}] ";
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{base.ToString()} Entry=[{Entry}]";
        }

        private bool IsSuccess() => Skipped || InsideRingCopyResult?.Succeeded == true || OutsideRingCopyResult?.Succeeded == true;
    }
}
