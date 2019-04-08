// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
