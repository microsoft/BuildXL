// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
