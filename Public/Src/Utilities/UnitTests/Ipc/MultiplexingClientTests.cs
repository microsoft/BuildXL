// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Common.Connectivity;
using BuildXL.Ipc.Common.Multiplexing;
using BuildXL.Ipc.Interfaces;
using BuildXL.Ipc.MultiplexingSocketBasedIpc;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Ipc
{
    public sealed class MultiplexingClientTests : IpcTestBase
    {
        public MultiplexingClientTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public Task TestMultipleDispose()
        {
            return WithSetupAndTeardownAssertingClientCompleted(
                nameof(TestMultipleDispose),
                async (client, _) =>
                {
                    await WaitClientDone(client);
                });
        }

        [Fact]
        public void TestMultiplexingIpcProviderCreatesNewClientEveryTime()
        {
            IIpcProvider provider = new MultiplexingSocketBasedIpcProvider();
            var connectionString = provider.CreateNewConnectionString();
            var config1 = new ClientConfig { MaxConnectRetries = 1 };
            var config2 = new ClientConfig { MaxConnectRetries = 2 };
            var client1 = provider.GetClient(connectionString, config1);
            var client2 = provider.GetClient(connectionString, config2);
            Assert.Equal(config1, client1.Config);
            Assert.Equal(config2, client2.Config);
            Assert.NotEqual(client1, client2);
        }

        [Fact]
        public void TestIpcProviderWithMemoizationReturnsSameClientsForSameMoniker()
        {
            using (var provider = new IpcProviderWithMemoization(new MultiplexingSocketBasedIpcProvider()))
            {
                var connectionString = provider.CreateNewConnectionString();
                var config1 = new ClientConfig { MaxConnectRetries = 1 };
                var config2 = new ClientConfig { MaxConnectRetries = 2 };
                var client1 = provider.GetClient(connectionString, config1);
                var client2 = provider.GetClient(connectionString, config2);
                Assert.Equal(config1, client1.Config);
                Assert.Equal(config1, client2.Config);
                Assert.Equal(client1, client2);
            }
        }

        [Fact]
        public void TestDifferentIpcProvidersWithMemoizationReturnClientsWithExclusiveConnection()
        {
            using (var provider1 = new IpcProviderWithMemoization(new MultiplexingSocketBasedIpcProvider()))
            using (var provider2 = new IpcProviderWithMemoization(new MultiplexingSocketBasedIpcProvider()))
            {
                var connectionString = provider1.CreateNewConnectionString();
                var config1 = new ClientConfig { MaxConnectRetries = 1 };
                var config2 = new ClientConfig { MaxConnectRetries = 2 };
                var client1 = provider1.GetClient(connectionString, config1);
                var client2 = provider2.GetClient(connectionString, config2);
                Assert.Equal(config1, client1.Config);
                Assert.Equal(config2, client2.Config);
            }
        }

        [Fact]
        public async Task TestRequestsFailAfterCompletion()
        {
            await WithSetupAndTeardownAssertingClientCompleted(
                nameof(TestRequestsFailAfterCompletion),
                async (client, serverStream) =>
                {
                    client.RequestStop();
                    await client.Completion;
                    var result = await client.Send(new IpcOperation("Hi", waitForServerAck: true));
                    Assert.False(result.Succeeded);
                    Assert.Equal(IpcResultStatus.TransmissionError, result.ExitCode);
                });
        }

        [Fact]
        public async Task TestIntertwinedTwoOperations()
        {
            await WithSetupAndTeardownAssertingClientCompleted(
                nameof(TestIntertwinedTwoOperations),
                async (client, serverStream) =>
                {
                    // create 2 operations
                    var hiOp = new IpcOperation("hi", waitForServerAck: true);
                    var helloOp = new IpcOperation("hello", waitForServerAck: true);

                    // send both operations via the same client
                    var hiTask = Task.Run(() => client.Send(hiOp));
                    var helloTask = Task.Run(() => client.Send(helloOp));

                    // receive 2 requests
                    var req1 = await Request.DeserializeAsync(serverStream);
                    var req2 = await Request.DeserializeAsync(serverStream);

                    // aux functions
                    var matchFn = new Func<IIpcOperation, Request>((op) =>
                        req1.Operation.Payload == op.Payload ? req1 :
                        req2.Operation.Payload == op.Payload ? req2 :
                        null);

                    var respondFn = new Func<Request, Task>((req) =>
                    {
                        var resp = new Response(req.Id, IpcResult.Success(req.Operation.Payload.ToUpperInvariant()));
                        return resp.SerializeAsync(serverStream);
                    });

                    var waitAndCheckResultFn = new Func<Task<IIpcResult>, IIpcOperation, Task>(async (task, operation) =>
                    {
                        var result = await task;
                        Assert.Equal(operation.Payload.ToUpperInvariant(), result.Payload);
                        Assert.True(result.Succeeded);
                    });

                    // match received requests to sent operations
                    var hiReq = matchFn(hiOp);
                    Assert.NotNull(hiReq);
                    Assert.Equal(hiOp.Payload, hiReq.Operation.Payload);

                    var helloReq = matchFn(helloOp);
                    Assert.NotNull(helloReq);
                    Assert.Equal(helloOp.Payload, helloReq.Operation.Payload);

                    // respond to helloReq, assert helloTask completes
                    await respondFn(helloReq);
                    await waitAndCheckResultFn(helloTask, helloOp);

                    // respond to hiReq, assert hiTask completes
                    await respondFn(hiReq);
                    await waitAndCheckResultFn(hiTask, hiOp);
                });
        }

        [Fact]
        public async Task TestSendIllFormattedResponse()
        {
            await WithSetupAndTeardownAssertingClientFaultedWithIpcException(
                nameof(TestSendIllFormattedResponse),
                IpcException.IpcExceptionKind.Serialization,
                async (client, serverStream) =>
                {
                    var op = new IpcOperation("hi", waitForServerAck: true);
                    var sendTask = Task.Run(() => client.Send(op));
                    var req = await Request.DeserializeAsync(serverStream);
                    Assert.Equal(op.Payload, req.Operation.Payload);

                    // send ill-formatted response
                    await Utils.WriteStringAsync(serverStream, "bogus response", CancellationToken.None); // integer is expected on the other end
                    await serverStream.FlushAsync();

                    // assert the task fails with a GenericError (and the client fails with IpcException)
                    await AssertGenericErrorIpcResult(sendTask);
                });
        }

        [Fact]
        public async Task TestSendResponseNotMatchingRequestId()
        {
            await WithSetupAndTeardownAssertingClientFaultedWithIpcException(
                nameof(TestSendResponseNotMatchingRequestId),
                IpcException.IpcExceptionKind.SpuriousResponse,
                async (client, serverStream) =>
                {
                    var op = new IpcOperation("hi", waitForServerAck: true);
                    var sendTask = Task.Run(() => client.Send(op));
                    var req = await Request.DeserializeAsync(serverStream);
                    Assert.Equal(op.Payload, req.Operation.Payload);

                    // send response whose RequestId doesn't match the Id of the request
                    await new Response(req.Id + 100, IpcResult.Success("ok")).SerializeAsync(serverStream);

                    // assert the task fails with a GenericError (and the client fails with IpcException)
                    await AssertGenericErrorIpcResult(sendTask);
                });
        }

        [Fact]
        public async Task TestSendResponseTwice()
        {
            await WithSetupAndTeardownAssertingClientFaultedWithIpcException(
                nameof(TestSendResponseTwice),
                IpcException.IpcExceptionKind.SpuriousResponse,
                async (client, serverStream) =>
                {
                    // issue two operations
                    var hiOp = new IpcOperation("hi", waitForServerAck: true);
                    var helloOp = new IpcOperation("hello", waitForServerAck: true);
                    var hiTask = Task.Run(() => client.Send(hiOp));
                    var helloTask = Task.Run(() => client.Send(helloOp));

                    // receive 1 request sent by the client
                    var req = await Request.DeserializeAsync(serverStream);

                    // match it to corresponding task
                    var match =
                        req.Operation.Payload == hiOp.Payload ? Tuple.Create(hiTask, helloTask) :
                        req.Operation.Payload == helloOp.Payload ? Tuple.Create(helloTask, hiTask) :
                        null;
                    Assert.NotNull(match);

                    var matchedTask = match.Item1;
                    var otherTask = match.Item2;

                    // send a good response
                    var resp = new Response(req.Id, IpcResult.Success("ok"));
                    await resp.SerializeAsync(serverStream);

                    // assert the matched task completed
                    var result = await matchedTask;
                    Assert.True(result.Succeeded);
                    Assert.Equal("ok", result.Payload);

                    // send the same response again causing the client to fail
                    // with IpcException because the corresponding request has been completed.
                    await resp.SerializeAsync(serverStream);

                    // assert the other task failed
                    var otherResult = await otherTask;
                    Assert.False(otherResult.Succeeded);
                });
        }

        [Fact]
        public async Task TestConnectionLost()
        {
            await WithSetup(
                nameof(TestConnectionLost),
                async (client, serverSocket) =>
                {
                    using (client)
                    {
                        // issue an operation
                        var op = new IpcOperation("hi", waitForServerAck: true);
                        var sendTask = Task.Run(() => client.Send(op));

                        // close connection
                        serverSocket.Disconnect(false);
                        serverSocket.Close();

                        // the task should fail
                        var result = await sendTask;
                        XAssert.IsFalse(result.Succeeded);

                        // assert client fails
                        await Assert.ThrowsAsync<AggregateException>(() => WaitClientDone(client));
                    }
                });
        }

        [Fact]
        public async Task TestStopRequestSentUponCompletion()
        {
             await WithSetup(
                nameof(TestStopRequestSentUponCompletion),
                async (client, serverSocket) =>
                {
                    using (var serverStream = new NetworkStream(serverSocket))
                    {
                        using (client)
                        {
                            client.RequestStop();
                            await WaitClientDone(client);
                        }

                        var request = await Request.DeserializeAsync(serverStream);
                        Assert.True(request.IsStopRequest);
                    }
                });
        }

        private Task WithSetupAndTeardownAssertingClientCompleted(string testName, Func<MultiplexingClient, Stream, Task> testAction)
        {
            return WithSetup(
                testName,
                async (client, serverStream) =>
                {
                    using (client)
                    {
                        await testAction(client, serverStream);
                        await WaitClientDone(client);
                    }
                });
        }

        private async Task WithSetupAndTeardownAssertingClientFaultedWithIpcException(
            string testName,
            IpcException.IpcExceptionKind kind,
            Func<MultiplexingClient, Stream, Task> testAction)
        {
            var aggEx = await WithSetupAndTeardownAssertingClientFaulted<AggregateException>(testName, testAction);
            var ex = aggEx.InnerException as IpcException;
            XAssert.IsNotNull(ex);
            XAssert.AreEqual(kind, ex.Kind, "Wrong IpcException kind.  Original stack trace: \n" + ex.ToString());
        }

        private async Task<TException> WithSetupAndTeardownAssertingClientFaulted<TException>(string testName, Func<MultiplexingClient, Stream, Task> testAction)
            where TException : Exception
        {
            TException ex = null;
            await WithSetup(
                testName,
                async (client, serverStream) =>
                {
                    using (client)
                    {
                        await testAction(client, serverStream);
                        ex = await Assert.ThrowsAsync<TException>(() => WaitClientDone(client));
                    }
                });
            return ex;
        }

        private Task WithSetup(string testName, Func<MultiplexingClient, Stream, Task> testAction)
        {
            return WithSetup(
                testName,
                async (client, serverSocket) =>
                {
                    using (var streamBundle = new NetworkStream(serverSocket))
                    {
                        await testAction(client, streamBundle);
                    }
                });
        }

        [SuppressMessage("AsyncFixer04", "AsyncFixer04:Fire & Forget 'tcpConnectivity'", Justification = "It's not forgotten, since the task is awaited 2 lines below")]
        private async Task WithSetup(string testName, Func<MultiplexingClient, Socket, Task> testAction)
        {
            using (var tcpConnectivity = new TcpIpConnectivity(Utils.GetUnusedPortNumber()))
            {
                var serverSocketTask = Task.Run(() => tcpConnectivity.AcceptClientAsync(CancellationToken.None));
                using (var tcpClient = await tcpConnectivity.ConnectToServerAsync())
                using (var serverSocket = await serverSocketTask)
                {
                    var client = new MultiplexingClient(ClientConfigWithLogger(testName), tcpClient.GetStream());
                    await testAction(client, serverSocket);
                }
            }
        }

        private async Task AssertGenericErrorIpcResult(Task<IIpcResult> task)
        {
            var result = await task;
            Assert.False(result.Succeeded);
            Assert.Equal(IpcResultStatus.GenericError, result.ExitCode);
        }

        private Task WaitClientDone(MultiplexingClient client)
        {
            client.RequestStop();
            return client.Completion;
        }
    }
}
