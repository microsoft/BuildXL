// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// We can't rename the Protobuff namespace so we'll have to keep these old global namespaces around.
namespace ContentStore.Grpc
{
    /// <summary>
    /// Defines common interface for Grpc request types.
    /// </summary>
    /// <remarks>
    /// Note that not every request type actually have headers and in this case the Header property may return null.
    /// </remarks>
    public interface IGrpcRequest
    {
        /// <summary>
        /// Optional request header.
        /// </summary>
        RequestHeader Header { get; }
    }
}
