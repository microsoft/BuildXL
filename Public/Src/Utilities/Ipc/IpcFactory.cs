// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;

namespace BuildXL.Ipc
{
    /// <summary>
    /// Static class providing a factory method for obtaining an instance of <see cref="IIpcProvider"/>.
    /// </summary>
    public static class IpcFactory
    {
        /// <summary>
        /// Factory method for obtaining an instance of the <see cref="IIpcProvider"/>.
        /// interface.  It is at this library's sole discretion to decide what implementation
        /// of said interface it will return; any client code should never have to rely
        /// on any specificities of a concrete implementation returned by this factory.
        /// </summary>
        public static IIpcProvider GetProvider()
        {
            return new MultiplexingSocketBasedIpc.MultiplexingSocketBasedIpcProvider();
        }

        /// <summary>
        /// Gets a fixed moniker.
        /// </summary>
        public static IIpcMoniker GetFixedMoniker()
        {
            return new StringMoniker("BuildXL.Ipc");
        }
    }
}
