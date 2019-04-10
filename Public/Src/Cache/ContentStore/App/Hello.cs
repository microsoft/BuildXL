// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using Microsoft.Practices.TransientFaultHandling;
using CLAP;
using BuildXL.Cache.ContentStore.Sessions;
using Grpc.Core;
using ContentStore.Grpc;
using System.Linq;

namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        public static ChannelOption[] DefaultChannelOptions = new ChannelOption[] { new ChannelOption(ChannelOptions.MaxSendMessageLength, -1), new ChannelOption(ChannelOptions.MaxReceiveMessageLength, -1) };

        /// <summary>
        /// Attempt to connect to a grpc port and send 'Hello'
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        [Verb(Description = "Send 'Hello' to another CASaaS")]
        internal void Hello(
            string hash,
            [Required, Description("Machine to copy from")] string host,
            [Description("The GRPC port"), DefaultValue(0)] int grpcPort)
        {
            Initialize();

            if (grpcPort <= 0)
            {
                throw new Exception("Must define grpc port greater than 0");
            }

            var _channel = new Channel(host, grpcPort, ChannelCredentials.Insecure, DefaultChannelOptions);
            var _client = new ContentServer.ContentServerClient(_channel);
            var helloResponse = _client.Hello(new HelloRequest(), new CallOptions(deadline: DateTime.UtcNow + TimeSpan.FromSeconds(2)));

            Console.WriteLine(helloResponse.ToString());
        }
    }
}
