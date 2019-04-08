// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Grpc.Core;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Gets objects responsible for port sharing between processes.
    /// </summary>
    public interface IGrpcService
    {
        /// <summary>
        /// Creates GRPC server binding definition for the server.
        /// </summary>
        ServerServiceDefinition Bind();
    }
}
