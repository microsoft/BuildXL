// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <nodoc />
    public class TransientErrorDetectionStrategy
    {
        /// <inheritdoc />
        public bool IsTransient(Exception ex)
        {
            if (ex is not ClientCanRetryException e)
            {
                return false;
            }

            e.Context?.TraceMessage(Severity.Debug, $"Retryable error: {e.Message}", component: nameof(TransientErrorDetectionStrategy));
            return true;
        }
    }
}
