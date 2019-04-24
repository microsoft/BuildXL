// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using Microsoft.Practices.TransientFaultHandling;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <nodoc />
    public class TransientErrorDetectionStrategy : ITransientErrorDetectionStrategy
    {
        /// <inheritdoc />
        public bool IsTransient(Exception ex)
        {
            var e = ex as ClientCanRetryException;
            if (e == null)
            {
                return false;
            }

            e.Context?.TraceMessage(Severity.Debug, $"Retryable error: {e.Message}");
            return true;
        }
    }
}
