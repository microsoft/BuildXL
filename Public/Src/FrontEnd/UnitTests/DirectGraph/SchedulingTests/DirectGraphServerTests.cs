// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.DirectGraph;
using Grpc.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.FrontEnd.DirectGraph
{
    /// <summary>
    /// Drives <see cref="DirectGraphServer"/> over loopback gRPC with a graph builder that only records what it is given.
    /// </summary>
    public sealed class DirectGraphServerTests : XunitBuildXLTest
    {
        private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(1);

        public DirectGraphServerTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task OperationsSentAheadAreHandledOneAtATimeAndAnsweredInOrder()
        {
            const int Operations = 200;
            const ulong Rejected = 57;

            var graphBuilder = new RecordingGraphBuilder(Rejected);
            int received = 0;

            using var server = new DirectGraphServer(graphBuilder, _ => Interlocked.Increment(ref received));
            server.Start();

            // Everything is sent, Complete included, before a single response is read.
            var responses = await SendThenReadAsync(server, Operations, sendComplete: true);
            await server.Completion.WaitAsync(CallTimeout);

            var expectedOrder = Enumerable.Range(1, Operations + 1).Select(id => (ulong)id).ToList();
            Assert.Equal(expectedOrder, responses.Select(response => response.OperationId));
            Assert.Equal(expectedOrder, graphBuilder.Handled);
            Assert.False(graphBuilder.Overlapped);
            Assert.Equal(Operations + 1, received);

            // A rejection is an answer like any other: the stream stays open and later operations are still handled.
            Assert.Equal(GraphOperationResponseStatus.GraphError, responses[(int)Rejected - 1].Status);
            Assert.Equal(GraphOperationResponseStatus.Success, responses[(int)Rejected].Status);

            // A graph is built over one stream: a second call is refused before anything reaches the builder.
            var secondCall = await Assert.ThrowsAsync<RpcException>(() => SendThenReadAsync(server, 1, sendComplete: true));
            Assert.Equal(StatusCode.FailedPrecondition, secondCall.StatusCode);
            Assert.Equal(expectedOrder, graphBuilder.Handled);

            // An emitter that closes the stream without Complete leaves the graph unfinished.
            using var unfinished = new DirectGraphServer(new RecordingGraphBuilder(rejected: 0));
            unfinished.Start();

            Assert.Equal(3, (await SendThenReadAsync(unfinished, 3, sendComplete: false)).Count);
            Assert.False(unfinished.Completion.IsCompleted);

            // Disposing a server does not wait for a client that never ends its call, and can be done twice.
            using var hanging = new DirectGraphServer(new RecordingGraphBuilder(rejected: 0));
            hanging.Start();

            var hangingChannel = new Channel(hanging.Endpoint, ChannelCredentials.Insecure, DirectGraphServer.MessageLengthChannelOptions);
            try
            {
                // Nothing ends this call but the test, so a dispose that waits for it cannot meet the bound below.
                using var openCall = new DirectGraphService.DirectGraphServiceClient(hangingChannel).BuildGraph();
                await openCall.RequestStream.WriteAsync(new GraphOperation
                {
                    OperationId = 1,
                    ProtocolVersion = DirectGraphProtocol.Version,
                    AddWriteFile = new AddWriteFileRequest(),
                });
                Assert.True(await openCall.ResponseStream.MoveNext(CancellationToken.None).WaitAsync(CallTimeout));

                await Task.Run(hanging.Dispose).WaitAsync(TimeSpan.FromSeconds(30));
            }
            finally
            {
                await hangingChannel.ShutdownAsync();
            }
        }

        private static async Task<List<GraphOperationResponse>> SendThenReadAsync(DirectGraphServer server, int operations, bool sendComplete)
        {
            var channel = new Channel(server.Endpoint, ChannelCredentials.Insecure, DirectGraphServer.MessageLengthChannelOptions);
            try
            {
                using var timeout = new CancellationTokenSource(CallTimeout);
                var client = new DirectGraphService.DirectGraphServiceClient(channel);
                using var call = client.BuildGraph(cancellationToken: timeout.Token);

                for (ulong id = 1; id <= (ulong)operations; id++)
                {
                    await call.RequestStream.WriteAsync(new GraphOperation
                    {
                        OperationId = id,
                        ProtocolVersion = DirectGraphProtocol.Version,
                        AddWriteFile = new AddWriteFileRequest(),
                    });
                }

                if (sendComplete)
                {
                    await call.RequestStream.WriteAsync(new GraphOperation
                    {
                        OperationId = (ulong)operations + 1,
                        ProtocolVersion = DirectGraphProtocol.Version,
                        Complete = new CompleteRequest(),
                    });
                }
                else
                {
                    await call.RequestStream.CompleteAsync();
                }

                var responses = new List<GraphOperationResponse>();
                while (await call.ResponseStream.MoveNext(timeout.Token))
                {
                    responses.Add(call.ResponseStream.Current);
                }

                return responses;
            }
            finally
            {
                await channel.ShutdownAsync();
            }
        }

        private sealed class RecordingGraphBuilder : IDirectGraphGraphBuilder
        {
            private readonly ulong m_rejected;
            private int m_active;

            public List<ulong> Handled { get; } = new();

            public bool Overlapped { get; private set; }

            public RecordingGraphBuilder(ulong rejected) => m_rejected = rejected;

            public async Task<GraphOperationResponse> HandleOperationAsync(GraphOperation operation, CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref m_active) > 1)
                {
                    Overlapped = true;
                }

                // Gives the server's reader the chance to run ahead while this operation is still being handled.
                await Task.Yield();

                lock (Handled)
                {
                    Handled.Add(operation.OperationId);
                }

                Interlocked.Decrement(ref m_active);

                return new GraphOperationResponse
                {
                    OperationId = operation.OperationId,
                    Status = operation.OperationId == m_rejected ? GraphOperationResponseStatus.GraphError : GraphOperationResponseStatus.Success,
                };
            }
        }
    }
}
