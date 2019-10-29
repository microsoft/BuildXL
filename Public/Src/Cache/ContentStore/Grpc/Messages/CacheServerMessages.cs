// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
#pragma warning disable IDE0040 // Add accessibility modifiers

// We can't rename the Protobuff namespace so we'll have to keep these old global namespaces around.
namespace ContentStore.Grpc
{
    // The set of partial types declared here are required for adding interface implementations for grpc types.
    partial class AddOrGetContentHashListRequest : IGrpcRequest { }

    partial class AddOrGetContentHashListResponse : IGrpcResponse { }

    partial class GetContentHashListRequest : IGrpcRequest { }
    partial class GetContentHashListResponse : IGrpcResponse { }

    partial class GetSelectorsRequest : IGrpcRequest { }

    partial class GetSelectorsResponse : IGrpcResponse
    {
        /// <nodoc />
        public GetSelectorsResponse(bool hasMore, IEnumerable<SelectorData> selectors)
        {
            hasMore_ = hasMore;
            Selectors.AddRange(selectors);
        }
    }

    partial class IncorporateStrongFingerprintsRequest : IGrpcRequest { }
    partial class IncorporateStrongFingerprintsResponse : IGrpcResponse { }
}

#pragma warning restore IDE0040 // Add accessibility modifiers
