// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

// We can't rename the Protobuff namespace so we'll have to keep these old global namespaces around.
namespace ContentStore.Grpc
{
    /// <nodoc />
    public partial class RequestHeader
    {
        /// <nodoc />
        public RequestHeader(Guid traceId, int sessionId)
        {
            TraceId = traceId.ToString();
            SessionId = sessionId;
        }
    }
}
