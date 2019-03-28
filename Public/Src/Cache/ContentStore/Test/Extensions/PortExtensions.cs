// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;

namespace ContentStoreTest.Extensions
{
    public static class PortExtensions
    {
        private static readonly ConcurrentDictionary<int, bool> PortCollection = new ConcurrentDictionary<int, bool>();

        static PortExtensions()
        {
            PortCollection.GetOrAdd(0, true);
        }

        public static int GetNextAvailablePort()
        {
            int portNumber = 0;
            while (PortCollection.ContainsKey(portNumber))
            {
                using (Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp))
                {
                    var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
                    socket.Bind(endPoint);
                    portNumber = socket.LocalEndPoint.As<IPEndPoint>().Port;
                    portNumber.Should().NotBe(0);

                    if (PortCollection.TryAdd(portNumber, true))
                    {
                        return portNumber;
                    }
                }
            }

            return portNumber;
        }
    }
}
