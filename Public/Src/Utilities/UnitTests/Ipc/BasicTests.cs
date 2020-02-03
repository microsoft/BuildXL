// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Common.Connectivity;
using BuildXL.Ipc.Interfaces;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Ipc
{
    public sealed class BasicTests : IpcTestBase
    {
        public BasicTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task TestIcpIpConectToServerWhenNoServerIsRunningAsync()
        {
            using var tcpIp = new TcpIpConnectivity(port: 12345);
            var ex = await Assert.ThrowsAnyAsync<SocketException>(async () => await tcpIp.ConnectToServerAsync(numTimesToRetry: 2, delayMillis: 50));
            XAssert.IsNotNull(ex);
        }

        [Theory]
        [InlineData("not-a-number")]
        [InlineData("-123")]
        public void TestParseInvalidPortNumber(string port)
        {
            var ex = Assert.Throws<IpcException>(() => TcpIpConnectivity.ParsePortNumber(port));
            XAssert.AreEqual(IpcException.IpcExceptionKind.InvalidMoniker, ex.Kind);
        }

        [Fact]
        public async Task TestConnectWithRetriesFailsAsync()
        {
            var errMessage = "can't connect";
            var maybeConnection = await Utils.ConnectAsync<int>(
                maxRetry: 2,
                waitTimeBetweenAttempts: TimeSpan.FromMilliseconds(1),
                connectionFactory: () => throw new InvalidOperationException(errMessage));
            XAssert.IsFalse(maybeConnection.Succeeded);
            XAssert.Contains(maybeConnection.Failure.DescribeIncludingInnerFailures(), errMessage);
        }

        [Fact]
        public async Task TestConnectWithRetriesSucceedsAsync()
        {
            var attempt = 0;
            var connection = 42;
            var maybeConnection = await Utils.ConnectAsync<int>(
                maxRetry: 2,
                waitTimeBetweenAttempts: TimeSpan.FromMilliseconds(1),
                connectionFactory: () =>
                {
                    if (attempt++ == 0)
                    {
                        throw new InvalidOperationException("cannot connect from the first attempt");
                    }
                    return Task.FromResult(connection);
                });

            XAssert.PossiblySucceeded(maybeConnection);
            XAssert.AreEqual(connection, maybeConnection.Result);
        }

        [Theory]
        [InlineData(IpcResultStatus.ConnectionError)]
        [InlineData(IpcResultStatus.TransmissionError)]
        [InlineData(IpcResultStatus.InvalidInput)]
        public void TestHandleExceptions(IpcResultStatus status)
        {
            var exceptionMessage = "invalid operation";
            var ipcResult = Utils.HandleExceptions(status, () => throw new InvalidOperationException(exceptionMessage));
            XAssert.AreEqual(status, ipcResult.ExitCode);
            XAssert.Contains(ipcResult.Payload, exceptionMessage);
        }
    }
}
