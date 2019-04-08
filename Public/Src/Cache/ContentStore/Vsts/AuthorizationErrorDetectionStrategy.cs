// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net;
using System.Net.Http;
using Microsoft.Practices.TransientFaultHandling;
using Microsoft.VisualStudio.Services.Common;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    /// Retry strategy for getting VSS credentials.
    /// </summary>
    public class AuthorizationErrorDetectionStrategy : ITransientErrorDetectionStrategy
    {
        /// <inheritdoc />
        public bool IsTransient(Exception ex)
        {
            bool isTransient = VssNetworkHelper.IsTransientNetworkException(ex, new VssHttpRetryOptions());

            // Naively retry all authorization exceptions.
            isTransient |= ex is HttpRequestException &&
                      (ex.Message.Contains("408") || // Request Timeout
                       ex.Message.Contains("429") || // Too Many Requests
                       ex.Message.Contains("502") || // Bad Gateway
                       ex.Message.Contains("503") || // Service Unavailable
                       ex.Message.Contains("504") || // Gateway Timeout
                       ex.InnerException is WebException // The request was aborted
                       );

            isTransient |= ex is VssUnauthorizedException;
            return isTransient;
        }
    }
}
