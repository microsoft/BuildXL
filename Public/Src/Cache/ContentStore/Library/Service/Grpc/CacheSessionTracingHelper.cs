// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    public static class CacheSessionTracingHelper
    {
        /// <summary>
        /// Gets a string that represents a session id in a consistent manner.
        /// </summary>
        /// <remarks>
        /// It is important to trace session id in many places in a same way to make the analysis based on session information easier.
        /// </remarks>
        public static string AsTraceableSessionId(this int sessionId)
        {
            return $"SessionId=[{sessionId}]";
        }
    }
}
