// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using FluentAssertions;

#nullable enable

namespace ContentStoreTest.Extensions
{
    public static class PortExtensions
    {
        private static readonly Tracer _tracer = new Tracer(nameof(PortExtensions));

        private static readonly ConcurrentDictionary<int, bool> PortCollection = new ConcurrentDictionary<int, bool>();

        static PortExtensions()
        {
            PortCollection.GetOrAdd(0, true);
        }

        public static int GetNextAvailablePort(Context? context = null)
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
                        if (context != null)
                        {
                            _tracer.Debug(context, $"Obtained next available port {portNumber}.");
                        }

                        return portNumber;
                    }
                }
            }

            return portNumber;
        }
    }
}
