// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
