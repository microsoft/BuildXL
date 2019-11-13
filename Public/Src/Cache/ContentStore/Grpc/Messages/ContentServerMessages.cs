// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
#pragma warning disable IDE0040 // Add accessibility modifiers

// We can't rename the Protobuff namespace so we'll have to keep these old global namespaces around.
namespace ContentStore.Grpc
{
    // The set of partial types declared here are required for adding interface implementations for grpc types.

    /// <summary>
    /// A special base type without request header.
    /// </summary>
    public class HeaderlessRequest : IGrpcRequest
    {
        /// <nodoc />
        RequestHeader IGrpcRequest.Header => null;
    }

    /// <summary>
    /// A special response type without response header.
    /// </summary>
    public class HeaderlessResponse : IGrpcResponse
    {
        /// <nodoc />
        ResponseHeader IGrpcResponse.Header
        {
            get => null;
            set { }
        }
    }

    partial class PinRequest : IGrpcRequest { }
    partial class PinResponse : IGrpcResponse { }

    partial class PinBulkRequest : IGrpcRequest { }
    partial class PinBulkResponse : HeaderlessResponse { }

    partial class PlaceFileRequest : IGrpcRequest { }
    partial class PlaceFileResponse : IGrpcResponse { }

    partial class PutFileRequest : IGrpcRequest { }
    partial class PutFileResponse : IGrpcResponse { }

    partial class ShutdownRequest : IGrpcRequest { }
    
    partial class HeartbeatRequest : IGrpcRequest { }
    partial class HeartbeatResponse : IGrpcResponse { }

    partial class CreateSessionRequest : HeaderlessRequest, IGrpcRequest { }
    partial class CreateSessionResponse : HeaderlessResponse, IGrpcResponse { }

    partial class GetStatsRequest : HeaderlessRequest { }

    partial class GetStatsResponse : HeaderlessResponse, IGrpcResponse
    {
        public static GetStatsResponse Failure() => new GetStatsResponse() { Success = false };

        public static GetStatsResponse Create(IDictionary<string, long> stats)
        {
            var result = new GetStatsResponse() { Success = true };
            result.Statistics.Add(stats);
            return result;
        }
    }
}

#pragma warning restore IDE0040 // Add accessibility modifier
