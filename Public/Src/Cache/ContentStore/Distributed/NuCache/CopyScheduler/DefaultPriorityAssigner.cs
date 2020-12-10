// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Utilities;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling
{
    internal class DefaultPriorityAssigner : ICopySchedulerPriorityAssigner
    {
        internal static readonly int MaxCopyReason = (int)EnumTraits<CopyReason>.MaxValue;

        internal static readonly int MaxLocationSource = (int)EnumTraits<ProactiveCopyLocationSource>.MaxValue;

        internal static readonly int MaxAttempt = 1;

        internal static readonly int MaxPriorityStatic = GetPriority((CopyReason)MaxCopyReason, 0, (ProactiveCopyLocationSource)MaxLocationSource);

        /// <inheritdoc />
        public int MaxPriority => MaxPriorityStatic;

        /// <inheritdoc />
        public int Prioritize(CopyOperationBase request)
        {
            if (request is OutboundPullCopy outboundPullRequest)
            {
                return GetPriority(
                    reason: outboundPullRequest.Reason,
                    attempt: outboundPullRequest.Attempt,
                    proactiveLocationSource: ProactiveCopyLocationSource.None);
            }
            else if (request is OutboundPushCopyBase outboundPushRequest)
            {
                return GetPriority(
                    reason: outboundPushRequest.Reason,
                    attempt: outboundPushRequest.Attempt,
                    proactiveLocationSource: outboundPushRequest.LocationSource);
            }
            else
            {
                throw new InvalidOperationException($"Attempt to prioritize a request with unhandled type `{request.GetType()}`");
            }
        }

        internal static int GetPriority(CopyReason reason, int attempt, ProactiveCopyLocationSource proactiveLocationSource)
        {
            Contract.Requires(attempt >= 0);
            // This prioritization has a few constraints:
            //  - Reason's prioritization is ascending
            //  - Source's prioritization is ascending
            //  - Attempt's prioritization is descending
            // The resulting priorities are ascending (i.e. if a request has priority 0, it is less important than
            // another with priority 1).
            attempt = Math.Min(attempt, MaxAttempt);
            return
                (MaxLocationSource + MaxAttempt) * (int)reason +
                MaxLocationSource * (MaxAttempt - attempt) +
                (int)proactiveLocationSource;
        }
    }
}
