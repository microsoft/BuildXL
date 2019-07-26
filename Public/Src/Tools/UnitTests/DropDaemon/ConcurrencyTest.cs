// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Common.Multiplexing;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.CLI;
using Test.BuildXL.TestUtilities.Xunit;
using Tool.ServicePipDaemon;
using Xunit;
using Xunit.Abstractions;
using static Tool.DropDaemon.DropDaemon;
using static Tool.ServicePipDaemon.ServicePipDaemon;

namespace Test.Tool.DropDaemon
{
    public sealed class ConcurrencyTest : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        private ITestOutputHelper Output { get; }

        public ConcurrencyTest(ITestOutputHelper output)
            : base(output)
        {
            Output = output;
        }

        /// <summary>
        ///     Stress test, mimicking how this tool would be used in practice: a number of clients
        ///     (threads here, processes in production) accessing the daemon concurrently.
        /// </summary>
        [Theory]
        [InlineData(2, 50)]
        public void TestWithThreads(int numServices, int numRequestsPerService)
        {
            var ipcProvider = new IpcProviderWithMemoization(IpcFactory.GetProvider());
            var ipcMonikers = Enumerable
                .Range(0, numServices)
                .Select(_ => ipcProvider.RenderConnectionString(ipcProvider.CreateNewMoniker()))
                .ToList();

            var serverThreads = ipcMonikers
                .Select(moniker => CreateThreadForCommand($"{StartNoDropCmd.Name} --{Moniker.LongName} " + moniker, null))
                .ToList();
            Start(serverThreads);

            Thread.Sleep(100);

            var clientThreads = GetClientThreads(ipcProvider, ipcMonikers, numServices, numRequestsPerService, $"{PingDaemonCmd.Name} --{Moniker.LongName} <moniker>");

            Start(clientThreads);
            Join(clientThreads);

            var serverShutdownThreads = GetClientThreads(ipcProvider, ipcMonikers, numServices, 1, $"{StopDaemonCmd.Name} --{Moniker.LongName} <moniker>");
            Start(serverShutdownThreads);
            Join(serverShutdownThreads);

            Join(serverThreads);
        }

        public static void Start(IEnumerable<Thread> threads)
        {
            foreach (var t in threads)
            {
                t.Start();
            }
        }

        public static void Join(IEnumerable<Thread> threads)
        {
            foreach (var t in threads)
            {
                t.Join();
            }
        }

        private IEnumerable<Thread> GetClientThreads(IIpcProvider ipcProvider, IEnumerable<string> ipcMonikers, int numServices, int numRequests, string cmdLine)
        {
            return ipcMonikers
                .SelectMany(moniker =>
                    Enumerable
                        .Range(1, numRequests)
                        .Select(i => CreateThreadForCommand(cmdLine.Replace("<moniker>", moniker), ipcProvider.GetClient(moniker, new ClientConfig())))
                        .ToList())
                .ToList();
        }

        private Thread CreateThreadForCommand(string cmdLine, IClient client)
        {
            return new Thread(() =>
            {
                Console.WriteLine($"running command: " + cmdLine);
                var logger = new LambdaLogger((level, format, args) =>
                {
                    var formatted = LoggerExtensions.Format(level, format, args);
                    Console.WriteLine(format);
                    Output.WriteLine(format);
                });
                ConfiguredCommand conf = ParseArgs(cmdLine, UnixParser.Instance, logger);
                var exitCode = conf.Command.ClientAction(conf, client);
                Assert.Equal(0, exitCode);
            });
        }
    }
}
