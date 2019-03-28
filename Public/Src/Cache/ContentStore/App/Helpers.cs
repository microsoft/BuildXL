// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Service.Grpc;

namespace BuildXL.Cache.ContentStore.App
{
    public static class Helpers
    {
        public static int GetGrpcPortFromFile(ILogger logger, string grpcPortFileName)
        {
            Contract.Assert(!string.IsNullOrWhiteSpace(grpcPortFileName));

            var portReaderFactory = new MemoryMappedFileGrpcPortSharingFactory(logger, grpcPortFileName);
            var portReader = portReaderFactory.GetPortReader();
            return portReader.ReadPort();
        }
    }
}
