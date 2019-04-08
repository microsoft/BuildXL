// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Gets objects responisble for port sharing between processes.
    /// </summary>
    public interface IGrpcPortSharingFactory
    {
        /// <summary>
        /// Gets the port exposer to start sharing the port.
        /// </summary>
        IPortExposer GetPortExposer();

        /// <summary>
        /// Gets an object for reading a port that is being shared.
        /// </summary>
        IPortReader GetPortReader();
    }

    /// <summary>
    /// Exposes methods to read an already-shared GRPC port.
    /// </summary>
    public interface IPortReader
    {
        /// <summary>
        /// Reads and returns the port to use for GRPC
        /// </summary>
        /// <returns>The GRPC port to use, or null if reading was not possible.</returns>
        int ReadPort();
    }

    /// <summary>
    /// Exposes methods to share a GRPC port.
    /// </summary>
    public interface IPortExposer
    {
        /// <summary>
        /// Starts sharing a GRPC port.
        /// </summary>
        /// <param name="port">The port to start sharing.</param>
        /// <returns>An <see cref="IDisposable"/> that, when disposed, will stop sharing the port.</returns>
        IDisposable Expose(int port);
    }

    /// <nodoc />
    public class PortReaderException : Exception
    {
        /// <nodoc />
        public PortReaderException(string message, Exception innerException = null)
            : base(message, innerException)
        {

        }
    }
}
