// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Ipc.MultiplexingSocketBasedIpc;
using BuildXL.Ipc.SocketBasedIpc;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
#if NET6_0_OR_GREATER
using BuildXL.Ipc.GrpcBasedIpc;
#endif

namespace Test.BuildXL.Ipc
{
    public sealed class IpcProviderTests : IpcTestBase
    {
        public IpcProviderTests(ITestOutputHelper output)
            : base(output) { }

        public static IEnumerable<IIpcProvider> ListOfProviders()
        {
            yield return new SocketBasedIpcProvider();
            yield return new MultiplexingSocketBasedIpcProvider();
#if NET6_0_OR_GREATER
            yield return new GrpcIpcProvider();
#endif
        }

        /// <summary>
        ///     Test that different invocations of <see cref="IIpcProvider.CreateNewMoniker"/> return monikers.
        /// </summary>
        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestCreateNewMonikerReturnsUniqueMonikers(IIpcProvider provider)
        {
            var m1 = provider.CreateNewMoniker();
            var m2 = provider.CreateNewMoniker();
            Assert.NotNull(m1);
            Assert.NotNull(m2);
            Assert.NotEqual(m1, m2);
        }

        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestRenderConnectionStringReturnsSameString(IIpcProvider provider)
        {
            var m1 = provider.CreateNewMoniker();
            var connStr1 = provider.RenderConnectionString(m1);
            var connStr2 = provider.RenderConnectionString(m1);
            Assert.NotNull(connStr1);
            Assert.NotNull(connStr2);
            Assert.Equal(connStr1, connStr2);
        }

        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestCreateDisposeClient(IIpcProvider provider)
        {
            var m1 = provider.CreateNewConnectionString();
            using (provider.GetClient(m1, new ClientConfig()))
            {
            }
        }

        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestCreateDisposeServer(IIpcProvider provider)
        {
            var m1 = provider.CreateNewConnectionString();
            using (var server = provider.GetServer(m1, new ServerConfig()))
            {
            }
        }

        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestServerConsideredNotCompletedBeforeStarted(IIpcProvider provider)
        {
            var m1 = provider.CreateNewConnectionString();
            using (var server = provider.GetServer(m1, new ServerConfig()))
            {
                Assert.False(server.Completion.IsCompleted);
            }
        }

        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestCreateStartStopDisposeServer(IIpcProvider provider)
        {
            var m1 = provider.CreateNewConnectionString();
            using (var server = provider.GetServer(m1, new ServerConfig()))
            {
                server.Start(EchoingExecutor);
                Assert.False(server.Completion.IsCompleted);
                server.RequestStop();
                WaitServerDone(server);
            }
        }

        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestServerMultipleRequestStop(IIpcProvider provider)
        {
            var m1 = provider.CreateNewConnectionString();
            using (var server = provider.GetServer(m1, new ServerConfig()))
            {
                server.Start(EchoingExecutor);
                server.RequestStop();
                server.RequestStop();
                WaitServerDone(server);
            }
        }

        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestServerMultipleDispose(IIpcProvider provider)
        {
            var m1 = provider.CreateNewConnectionString();
            using (var server = provider.GetServer(m1, new ServerConfig()))
            {
                server.Start(EchoingExecutor);
                server.RequestStop();
                WaitServerDone(server);
                server.Dispose();
            }
        }

        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestServerStartedThenDisposedBeforeStoppedFails(IIpcProvider provider)
        {
            var testName = nameof(TestServerStartedThenDisposedBeforeStoppedFails);
            var m1 = provider.CreateNewConnectionString();
            using (var server = provider.GetServer(m1, ServerConfigWithLogger(testName)))
            {
                server.Start(EchoingExecutor);
                var ex = Assert.Throws<IpcException>(() => server.Dispose());
                Assert.Equal(IpcException.IpcExceptionKind.DisposeBeforeCompletion, ex.Kind);
                server.RequestStop();
                WaitServerDone(server);
            }
        }

        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestServerMultipleStartFails(IIpcProvider provider)
        {
            var testName = nameof(TestServerMultipleStartFails);
            var m1 = provider.CreateNewConnectionString();
            using (var server = provider.GetServer(m1, ServerConfigWithLogger(testName)))
            {
                server.Start(EchoingExecutor);
                var ex = Assert.Throws<IpcException>(() => server.Start(EchoingExecutor));
                Assert.Equal(IpcException.IpcExceptionKind.MultiStart, ex.Kind);
                server.RequestStop();
                WaitServerDone(server);
            }
        }

        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestClientMultipleDispose(IIpcProvider provider)
        {
            var testName = nameof(TestClientMultipleDispose);
            WithIpcServer(
               provider,
               EchoingExecutor,
               ServerConfigWithLogger(testName),
               (moniker, server) =>
               {
                   using (var client = provider.GetClient(provider.RenderConnectionString(moniker), ClientConfigWithLogger(testName)))
                   {
                       client.Dispose();
                   }
               });
        }

        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestSimpleSyncOperation(IIpcProvider provider)
        {
            var testName = nameof(TestSimpleSyncOperation);
            WithIpcServer(
                provider,
                EchoingExecutor,
                ServerConfigWithLogger(testName),
                (moniker, server) =>
                {
                    using (var client = provider.GetClient(provider.RenderConnectionString(moniker), ClientConfigWithLogger(testName)))
                    {
                        var payload = "sync";
                        var syncOp = new IpcOperation(payload, waitForServerAck: true);
                        var syncResult = SendWithTimeout(client, syncOp);

                        Assert.True(syncResult.Succeeded, syncResult.Payload);
                        Assert.Equal(syncResult.Payload, payload);

                        client.RequestStop();
                        client.Completion.GetAwaiter().GetResult();
                    }
                });
        }

        /// <summary>
        ///     Concurrently issue <see cref="IClient.Send(IIpcOperation)"/> requests from the same
        ///     or different clients (one request per client) to one server.
        /// </summary>
        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestConcurrentSynchronousOperations(IIpcProvider provider)
        {
            var testName = nameof(TestConcurrentSynchronousOperations);
            WithIpcServer(
                provider,
                EchoingExecutor,
                ServerConfigWithLogger(testName),
                (moniker, server) =>
                {
                    using (IClient client = provider.GetClient(provider.RenderConnectionString(moniker), ClientConfigWithLogger(testName)))
                    {
                        var threads = Enumerable
                            .Range(1, 10)
                            .Select(i => new Thread(() =>
                            {
                                var message = "hi" + i;
                                var op = new IpcOperation(message, waitForServerAck: true);
                                var result = SendWithTimeout(client, op);
                                Assert.True(result.Succeeded, "error: " + result.Payload);
                                Assert.Equal(op.Payload, result.Payload);
                            }))
                            .ToArray();
                        Start(threads);
                        Join(threads);
                        client.RequestStop();
                        client.Completion.GetAwaiter().GetResult();
                    }
                });
        }

        /// <summary>
        ///     When execution fails, asynchronous operations still succeed, while synchronous
        ///     fail with <see cref="IpcResultStatus.ExecutionError"/>.
        /// </summary>
        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestWithExecutionError(IIpcProvider provider)
        {
            var testName = nameof(TestWithExecutionError);
            WithIpcServer(
                provider,
                CrashingExecutor,
                ServerConfigWithLogger(testName),
                (moniker, server) =>
                {
                    using (var client = provider.GetClient(provider.RenderConnectionString(moniker), ClientConfigWithLogger(testName)))
                    {
                        try
                        {
                            var syncOp = new IpcOperation("sync", waitForServerAck: true);
                            var asyncOp = new IpcOperation("async", waitForServerAck: false);
                            var syncResult = SendWithTimeout(client, syncOp);
                            var asyncResult = SendWithTimeout(client, asyncOp);

                            Assert.True(asyncResult.Succeeded, "Asynchronous operation is expected to succeed if executor crashes");
                            Assert.False(syncResult.Succeeded, "Synchronous operation is expected to fail if executor crashes");
                            Assert.Equal(IpcResultStatus.ExecutionError, syncResult.ExitCode);
                            Assert.True(syncResult.Payload.Contains("System.Exception")); // because CrashingExecutor throws System.Exception
                            Assert.True(syncResult.Payload.Contains(syncOp.Payload));     // because CrashingExecutor throws System.Exception whose message is equal to syncOp.Payload  
                        }
                        finally
                        {
                            client.RequestStop();
                            client.Completion.GetAwaiter().GetResult();
                        }
                    }
                });
        }

        /// <summary>
        ///     When connection fails, <see cref="IClient.Send(IIpcOperation)"> should fail with <see cref="IpcResultStatus.ConnectionError"/>.
        /// </summary>
        [Theory]
        [MemberData(nameof(ProvidersData))]
        public async Task TestWithConnectionErrorAsync(IIpcProvider provider)
        {
            var testName = nameof(TestWithConnectionErrorAsync);
            var connectionString = provider.CreateNewConnectionString();
            var config = new ClientConfig()
            {
                Logger = VerboseLogger(testName),
                MaxConnectRetries = 2,
                ConnectRetryDelay = TimeSpan.FromMilliseconds(100)
            };
            using var client = provider.GetClient(connectionString, config);
            var syncOpResult = await client.Send(new IpcOperation("sync hi", waitForServerAck: true));
            var asyncOpResult = await client.Send(new IpcOperation("async hi", waitForServerAck: false));

            // operations should fail because no server was started
            XAssert.AreEqual(IpcResultStatus.ConnectionError, syncOpResult.ExitCode);
            XAssert.AreEqual(IpcResultStatus.ConnectionError, asyncOpResult.ExitCode);
            XAssert.IsFalse(syncOpResult.Succeeded);
            XAssert.IsFalse(asyncOpResult.Succeeded);
            client.RequestStop();
            await client.Completion;
        }

        [Theory]
        [MemberData(nameof(ProvidersData))]
        public void TestLoadOrCreteMoniker(IIpcProvider provider)
        {
            var monikerId = "some arbitrary string";
            var moniker = provider.LoadOrCreateMoniker(monikerId);
            XAssert.AreEqual(monikerId, moniker.Id);
            var monikerClone = provider.LoadOrCreateMoniker(monikerId);
            XAssert.AreEqual(moniker, monikerClone);
            XAssert.AreEqual(moniker.GetHashCode(), monikerClone.GetHashCode());

            var differentMoniker = provider.CreateNewMoniker();
            XAssert.AreNotEqual(moniker, differentMoniker);
            XAssert.AreNotEqual(moniker.GetHashCode(), differentMoniker.GetHashCode());

        }

        public static IEnumerable<object[]> ProvidersData()
        {
            return ListOfProviders().Select(p => new object[] { p });
        }

        private static void Join(Thread[] threads)
        {
            foreach (var t in threads)
            {
                t.Join();
            }
        }

        private static void Start(Thread[] threads)
        {
            foreach (var t in threads)
            {
                t.Start();
            }
        }
    }
}
