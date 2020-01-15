// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace ContentStore.Grpc
{
    /// <summary>
    /// Defines common interface for Grpc response types.
    /// </summary>
    /// <remarks>
    /// Note that not every response type actually have header and in this case the Header property may return null.
    /// </remarks>
    public interface IGrpcResponse
    {
        /// <summary>
        /// Optional response header with some common information like processing duration and success/failure.
        /// </summary>
        ResponseHeader Header { get; set; }
    }
}
