// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Common.Connectivity;
using BuildXL.Ipc.Common.Multiplexing;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Ipc
{
    public sealed class MultiplexingServerTests : IpcTestBase
    {
        public MultiplexingServerTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public async Task TestUnawaited()
        {
            await WithServer(
                nameof(TestUnawaited),
                EchoingExecutor,
                maxConcurrentClients: 10,
                testAction: async (server, tcpProvider) =>
                {
                    var actionBlock = new ActionBlock<int>(
                        i => i % 5 == 0 ? GarbageCollect() : AssertPingServerFromNewClient(tcpProvider),
                        dataflowBlockOptions: new ExecutionDataflowBlockOptions
                        {
                            MaxDegreeOfParallelism = 10
                        });
                    var postResults = Enumerable.Range(0, 20).Select(i => actionBlock.Post(i));
                    XAssert.IsTrue(postResults.All(b => b));
                    actionBlock.Complete();
                    await actionBlock.Completion;
                });
        }

        private Task GarbageCollect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return Unit.VoidTask;
        }

        [Fact]
        public async Task TestSimplePing()
        {
            await WithServerAndClient(
                nameof(TestSimplePing),
                EchoingExecutor,
                async (server, tcpProvider, clientStream) =>
                {
                    await AssertPingServer(clientStream);
                });
        }

        [Fact]
        public async Task TestSendIllFormattedRequest()
        {
            await WithServerAndClient(
                nameof(TestSendIllFormattedRequest),
                EchoingExecutor,
                maxConcurrentClients: 1, // using 1 to ensure that bogus clients are not taking up space in the processing queue
                testAction: async (server, tcpProvider, clientStream) =>
                {
                    // send a bogus request
                    await Utils.WriteStringAsync(clientStream, "bogus request...", CancellationToken.None);
                    await clientStream.FlushAsync();

                    // assert that other clients can still be served
                    await AssertPingServerFromNewClient(tcpProvider);

                    // assert that no other requests can be sent from the same client (bogus clients are automatically disconnected)
                    await XAssert.ThrowsAnyAsync(() => AssertPingServer(clientStream));

                    // assert the error was logged
                    Assert.True(server.Diagnostics.Count > 0);
                });
        }

        [Fact]
        public async Task TestSingleConcurrentClient()
        {
            await WithServerAndClient(
                nameof(TestSingleConcurrentClient),
                EchoingExecutor,
                maxConcurrentClients: 1,
                testAction: async (server, tcpProvider, client1) =>
                {
                    using (var tcpClient2 = await tcpProvider.ConnectToServerAsync())
                    using (var client2 = tcpClient2.GetStream())
                    {
                        var client2PingRequest = await SendPing(client2);
                        var client2PingResponseTask = Response.DeserializeAsync(client2);

                        var client1PingRequest = await SendPing(client1);
                        var client1PingResponseTask = Response.DeserializeAsync(client1);

                        // assert client1 returned first because it connected first, even though it issued its request after client2
                        var completedTask = await Task.WhenAny(client1PingResponseTask, client2PingResponseTask);
                        VerifyPingResponse(client1PingRequest, await completedTask);

                        // assert client2 is still waiting
                        Assert.False(client2PingResponseTask.IsCompleted);

                        // close client1 and wait for response to client2
#pragma warning disable AsyncFixer02
                        client1.Dispose();
#pragma warning restore AsyncFixer02
                        VerifyPingResponse(client2PingRequest, await client2PingResponseTask);
                    }
                });
        }

        [Fact]
        public async Task TestDisconnectResponseSentUponCompletion()
        {
            await WithServerAndClient(
                nameof(TestDisconnectResponseSentUponCompletion),
                EchoingExecutor,
                maxConcurrentClients: 1,
                testAction: async (server, tcpProvider, client) =>
                {
                    // ping server, just to make sure the client is connected
                    await AssertPingServer(client);

                    // stop server
                    server.RequestStop();
                    await server.Completion;
                    server.Dispose();

                    // assert disconnect response is received on the client
                    var response = await Response.DeserializeAsync(client);
                    Assert.True(response.IsDisconnectResponse);
                });
        }

        [Fact]
        public async Task TestCommandThatRequestsStopReceivesResponseBeforeDisconnectResponse()
        {
            const string StopCmd = "stop";
            const string StopResponse = "stopped";

            IServer theServer = null; // set at the beginning of testAction
            var ipcExecutor = new LambdaIpcOperationExecutor(op =>
            {
                Assert.Equal(StopCmd, op.Payload);
                Assert.NotNull(theServer);
                theServer.RequestStop();
                return IpcResult.Success(StopResponse);
            });

            await WithServerAndClient(
                nameof(TestCommandThatRequestsStopReceivesResponseBeforeDisconnectResponse),
                ipcExecutor,
                maxConcurrentClients: 1,
                maxConcurrentRequestsPerClient: 1,
                testAction: async (server, tcpProvider, client) =>
                {
                    theServer = server;

                    // send stop request
                    var stopRequest = new Request(new IpcOperation(StopCmd, waitForServerAck: true));
                    await stopRequest.SerializeAsync(client);

                    // assert response to the stop request is received first
                    var resp = await Response.DeserializeAsync(client);
                    Assert.Equal(stopRequest.Id, resp.RequestId);
                    Assert.True(resp.Result.Succeeded, "Expected response to succeed; error: " + resp.Result.Payload);
                    Assert.Equal(StopResponse, resp.Result.Payload);

                    // assert disconnect response is received next
                    resp = await Response.DeserializeAsync(client);
                    Assert.True(resp.IsDisconnectResponse, "Expected a disconnect response, got: " + resp);
                });
        }

        [Fact]
        public async Task TestMultipleConcurrentClients()
        {
            await WithServerAndClient(
                nameof(TestMultipleConcurrentClients),
                EchoingExecutor,
                maxConcurrentClients: 2,
                testAction: async (server, tcpProvider, client1) =>
                {
                    using (var tcpClient2 = await tcpProvider.ConnectToServerAsync())
                    using (var client2 = tcpClient2.GetStream())
                    {
                        // both clients can ping server while both are connected
                        await AssertPingServer(client2);
                        await AssertPingServer(client1);
                    }
                });
        }

        [Fact]
        public async Task TestMaxConcurrentRequestPerClient()
        {
            var mre = new ManualResetEvent(false);
            var waitReq = new Request(new IpcOperation("wait", waitForServerAck: true));
            var signalReq = new Request(new IpcOperation("signal", waitForServerAck: true));
            var waitSignalExecutor = new LambdaIpcOperationExecutor(op =>
            {
                if (op.Payload == waitReq.Operation.Payload)
                {
                    mre.WaitOne();
                    return IpcResult.Success();
                }

                if (op.Payload == signalReq.Operation.Payload)
                {
                    mre.Set();
                    return IpcResult.Success();
                }

                Assert.False(true);
                return null;
            });

            // with 2 max concurrent requests
            await WithServerAndClient(
                nameof(TestMaxConcurrentRequestPerClient) + ".2",
                waitSignalExecutor,
                maxConcurrentRequestsPerClient: 2,
                testAction: async (server, tcpProvider, clientStream) =>
                {
                    // send "wait" first, then "signal"
                    await waitReq.SerializeAsync(clientStream);
                    await signalReq.SerializeAsync(clientStream);

                    // both responses arrive
                    await Response.DeserializeAsync(clientStream);
                    await Response.DeserializeAsync(clientStream);
                });

            mre.Reset();

            // with 1 max concurrent requests
            await WithServerAndClient(
                nameof(TestMaxConcurrentRequestPerClient) + ".1",
                waitSignalExecutor,
                maxConcurrentClients: 2,
                maxConcurrentRequestsPerClient: 1,
                testAction: async (server, tcpProvider, client) =>
                {
                    // send "wait" first, then "signal"
                    await waitReq.SerializeAsync(client);
                    await signalReq.SerializeAsync(client);

                    // send signal from a new client
                    using (var tcpClient2 = await tcpProvider.ConnectToServerAsync())
                    using (var clientStream2 = tcpClient2.GetStream())
                    {
                        await signalReq.SerializeAsync(clientStream2);
                        var resp = await Response.DeserializeAsync(clientStream2);
                        Assert.True(resp.Result.Succeeded);
                    }

                    // finally both client1 tasks complete
                    var resp1 = await Response.DeserializeAsync(client);
                    var resp2 = await Response.DeserializeAsync(client);
                    Assert.True(resp1.Result.Succeeded);
                    Assert.True(resp2.Result.Succeeded);
                });
        }

        private async Task AssertPingServerFromNewClient(TcpIpConnectivity tcpIpProvider)
        {
            using (var tcpClient = await tcpIpProvider.ConnectToServerAsync())
            using (var stream = tcpClient.GetStream())
            {
                await AssertPingServer(stream);
            }
        }

        private async Task AssertPingServer(Stream stream)
        {
            var pingRequest = await SendPing(stream);
            var response = await Response.DeserializeAsync(stream);
            VerifyPingResponse(pingRequest, response);
        }

        private void VerifyPingResponse(Request pingRequest, Response response)
        {
            Assert.Equal(pingRequest.Id, response.RequestId);
            Assert.Equal(pingRequest.Operation.Payload, response.Result.Payload);
        }

        private async Task<Request> SendPing(Stream stream)
        {
            var pingOp = new IpcOperation("Ping", waitForServerAck: true);
            var pingRequest = new Request(pingOp);
            await pingRequest.SerializeAsync(stream);
            return pingRequest;
        }

        private async Task WithServer(
            string testName,
            IIpcOperationExecutor executor,
            Func<MultiplexingServer<Socket>, TcpIpConnectivity, Task> testAction,
            int maxConcurrentClients = 2,
            int maxConcurrentRequestsPerClient = 10)
        {
            var logger = VerboseLogger(testName);
            var tcpIpProvider = CreateAndStartTcpIpProvider(logger);
            var server = new MultiplexingServer<Socket>(
                "TestTcpIpServer(" + testName + ")",
                logger,
                tcpIpProvider,
                maxConcurrentClients,
                maxConcurrentRequestsPerClient);

            server.Start(executor);
            using (server)
            {
                try
                {
                    await testAction(server, tcpIpProvider);
                }
                finally
                {
                    server.RequestStop();
                    await server.Completion;
                }
            }
        }

        private TcpIpConnectivity CreateAndStartTcpIpProvider(ILogger logger, int numTimesToRetry = 3, int delayMillis = 250)
        {
            try
            {
                var port = Utils.GetUnusedPortNumber();
                var tcpIpProvider = new TcpIpConnectivity(port);
                tcpIpProvider.StartListening();
                return tcpIpProvider;
            }
            catch (SocketException e)
            {
                if (numTimesToRetry > 0)
                {
                    logger.Verbose($"Could not connect; error: '{e.GetLogEventMessage()}'. Waiting {delayMillis}ms then retrying {numTimesToRetry} more times");
                    Thread.Sleep(millisecondsTimeout: delayMillis);
                    return CreateAndStartTcpIpProvider(logger, numTimesToRetry - 1, delayMillis * 2);
                }
                else
                {
                    logger.Verbose($"Could not connect; error: '{e.GetLogEventMessage()}'. Not retrying any more.");
                    throw;
                }
            }
        }

        private Task WithServerAndClient(
            string testName,
            IIpcOperationExecutor executor,
            Func<MultiplexingServer<Socket>, TcpIpConnectivity, Stream, Task> testAction,
            int maxConcurrentClients = 2,
            int maxConcurrentRequestsPerClient = 10)
        {
            return WithServer(
                testName,
                executor,
                maxConcurrentClients: maxConcurrentClients,
                maxConcurrentRequestsPerClient: maxConcurrentRequestsPerClient,
                testAction: (server, tcpIpProvider) => WithClient(tcpIpProvider, stream => testAction(server, tcpIpProvider, stream)));
        }

        private async Task WithClient(TcpIpConnectivity tcpIpProvider, Func<NetworkStream, Task> action)
        {
            using (var tcpClient = await tcpIpProvider.ConnectToServerAsync())
            using (var stream = tcpClient.GetStream())
            {
                await action(stream);
            }
        }
    }
}
