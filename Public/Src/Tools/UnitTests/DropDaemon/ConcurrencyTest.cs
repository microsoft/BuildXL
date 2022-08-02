// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Common.Multiplexing;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.CLI;
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
        public async Task TestWithThreadsAsync(int numServices, int numRequestsPerService)
        {
            var ipcProvider = new IpcProviderWithMemoization(IpcFactory.GetProvider());
            var ipcMonikers = Enumerable
                .Range(0, numServices)
                .Select(_ => ipcProvider.RenderConnectionString(IpcMoniker.CreateNew()))
                .ToList();

            var serverTasks = ipcMonikers
                .Select(moniker => CreateTaskForCommand($"{StartNoDropCmd.Name} --{Moniker.LongName} " + moniker, null))
                .ToList();

            // make sure that the daemons are running
            await Task.Delay(200);

            var clientThreads = GetClientTasks(ipcProvider, ipcMonikers, numRequestsPerService, $"{PingDaemonCmd.Name} --{Moniker.LongName} <moniker>");

            await Task.WhenAll(clientThreads);

            var serverShutdownThreads = GetClientTasks(ipcProvider, ipcMonikers, 1, $"{StopDaemonCmd.Name} --{Moniker.LongName} <moniker>");

            await Task.WhenAll(serverShutdownThreads);
            await Task.WhenAll(serverTasks);
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

        private IEnumerable<Task> GetClientTasks(IIpcProvider ipcProvider, IEnumerable<string> ipcMonikers, int numRequests, string cmdLine)
        {
            return ipcMonikers
                .SelectMany(moniker =>
                    Enumerable
                        .Range(1, numRequests)
                        .Select(i => CreateTaskForCommand(cmdLine.Replace("<moniker>", moniker), ipcProvider.GetClient(moniker, new ClientConfig())))
                        .ToList())
                .ToList();
        }

        private Task CreateTaskForCommand(string cmdLine, IClient client)
        {
            return Task.Factory.StartNew(() =>
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
            }, creationOptions: TaskCreationOptions.LongRunning);
        }
    }
}
