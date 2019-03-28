// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Host.Configuration;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Static factory to create objects capable of sharing and reading a shared port to use for GRPC.
    /// Useful for making sure that both exposer and reader are compatible.
    /// </summary>
    public class MemoryMappedFileGrpcPortSharingFactory : IGrpcPortSharingFactory
    {
        private readonly string _fileName;
        private readonly ILogger _logger;

        /// <nodoc />
        public MemoryMappedFileGrpcPortSharingFactory(ILogger logger, string fileName = null)
        {
            _fileName = fileName ?? LocalCasServiceSettings.DefaultFileName;
            _logger = logger;
        }

        /// <summary>
        /// Gets the default object in charge of exposing the GRCP port used.
        /// </summary>
        public IPortExposer GetPortExposer() => new MemoryMappedFilePortExposer(_fileName, _logger);

        /// <summary>
        /// Gets the default <see cref="IPortReader"/> object.
        /// </summary>
        public IPortReader GetPortReader() => new MemoryMappedFilePortReader(_fileName, _logger);
    }
}
