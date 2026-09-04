// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using Azure.Core;
using Azure.Core.Pipeline;

namespace Tool.BlobDaemon
{
    /// <summary>
    /// Pipeline policy that counts Azure Storage throttling responses.
    /// </summary>
    /// <remarks>
    /// The Azure SDK absorbs 503 and 429 inside its retry policy and only surfaces an exception once every
    /// attempt has failed, so a per-call policy would only see throttling that had already become fatal.
    /// Register at <see cref="HttpPipelinePosition.PerRetry"/> to observe every attempt.
    ///
    /// The count is what says whether a given maxDegreeOfParallelism is leaving headroom on the storage
    /// account or saturating it, which is the input to tuning it.
    /// </remarks>
    internal sealed class ThrottleObserverPolicy : HttpPipelineSynchronousPolicy
    {
        private const int TooManyRequests = 429;
        private const int ServiceUnavailable = 503;

        private long m_throttleSignals;

        /// <summary>Number of throttling responses observed, counting every attempt.</summary>
        public long ThrottleSignals => Interlocked.Read(ref m_throttleSignals);

        /// <inheritdoc />
        public override void OnReceivedResponse(HttpMessage message)
        {
            int status = message.Response?.Status ?? 0;
            if (status == ServiceUnavailable || status == TooManyRequests)
            {
                Interlocked.Increment(ref m_throttleSignals);
            }
        }
    }
}
