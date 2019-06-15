// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading;
using Grpc.Core;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Environment initialization for GRPC
    /// </summary>
    /// <remarks>
    /// GRPC is special and needs to initialize the environment
    /// before it is started. Also needs to be initialized only
    /// once.
    /// </remarks>
    public static class GrpcEnvironment
    {
        /// <summary>
        /// The local host
        /// </summary>
        public const string Localhost = "localhost";

        private static int _isInitialized;

        /// <summary>
        /// Allow sent and received message to have (essentially) unbounded length. This does not cause GRPC to send larger packets, but it does allow larger packets to exist.
        /// </summary>
        public static readonly List<ChannelOption> DefaultConfiguration = new List<ChannelOption>() { new ChannelOption(ChannelOptions.MaxSendMessageLength, int.MaxValue), new ChannelOption(ChannelOptions.MaxReceiveMessageLength, int.MaxValue) };

        /// <summary>
        /// Initialize the GRPC environment if not yet initialized.
        /// </summary>
        public static void InitializeIfNeeded(int numThreads = 70, bool handlerInliningEnabled = true)
        {
            if (Interlocked.CompareExchange(ref _isInitialized, 1, 0) == 0)
            {
                if (handlerInliningEnabled)
                {
                    global::Grpc.Core.GrpcEnvironment.SetThreadPoolSize(numThreads);
                    global::Grpc.Core.GrpcEnvironment.SetCompletionQueueCount(numThreads);
                }

                // By default, gRPC's internal event handlers get offloaded to .NET default thread pool thread (inlineHandlers=false).
                // Setting inlineHandlers to true will allow scheduling the event handlers directly to GrpcThreadPool internal threads.
                // That can lead to significant performance gains in some situations, but requires user to never block in async code
                // (incorrectly written code can easily lead to deadlocks). Inlining handlers is an advanced setting and you should
                // only use it if you know what you are doing. Most users should rely on the default value provided by gRPC library.
                // Note: this method is part of an experimental API that can change or be removed without any prior notice.
                // Note: inlineHandlers=true was the default in gRPC C# v1.4.x and earlier.
                global::Grpc.Core.GrpcEnvironment.SetHandlerInlining(handlerInliningEnabled);
            }
        }
    }
}
