// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.CLI;
using Test.BuildXL.TestUtilities.Xunit;
using Tool.ServicePipDaemon;
using Xunit;

namespace Test.Tool.DropDaemon
{
    /// <summary>
    /// Tests that a logged <see cref="IIpcResult"/> reports how long the work that produced it took.
    /// A server action logs its result before returning it, so the duration cannot be supplied by the code that invokes
    /// the action; the caller passes the timestamp the work started from instead (for a command, that is
    /// <see cref="ConfiguredCommand.StartTimestamp"/>). Without that, every logged line reports
    /// 'ActionDuration: 00:00:00' (the bug this fixes).
    /// </summary>
    public sealed class ActionDurationTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public ActionDurationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task LoggedResultReportsElapsedTimeSinceTheGivenStartTimestamp()
        {
            var startTimestamp = Stopwatch.GetTimestamp();
            await Task.Delay(20);

            var result = new IpcResult(IpcResultStatus.Success, "payload");
            XAssert.AreEqual(TimeSpan.Zero, result.ActionDuration);

            var logger = new CapturingLogger();
            TestServicePipDaemon.InvokeLogIpcResult(logger, LogLevel.Info, "[TEST] ", result, startTimestamp);

            XAssert.IsTrue(
                result.ActionDuration > TimeSpan.Zero,
                $"Expected a non-zero ActionDuration, but got {result.ActionDuration}");

            XAssert.Contains(logger.LastMessage, "[TEST] ");

            // A zero duration renders as exactly "00:00:00" followed by the closing brace. Match that precisely:
            // a non-zero sub-second duration (e.g. 00:00:00.0318011) shares the "00:00:00" prefix.
            XAssert.ContainsNot(logger.LastMessage, $"ActionDuration: {TimeSpan.Zero.ToString("c")}}}");
        }

        [Fact]
        public void LoggingReplacesTheSummedDurationOfAMergedResult()
        {
            // Merged results carry the sum of their parts' durations, which is not a meaningful elapsed time for work
            // done in parallel. Logging should report how long the work actually took instead.
            var merged = IpcResult.Merge(
                new IpcResult(IpcResultStatus.Success, "a", TimeSpan.FromHours(1)),
                new IpcResult(IpcResultStatus.Success, "b", TimeSpan.FromHours(1)));

            XAssert.AreEqual(TimeSpan.FromHours(2), merged.ActionDuration);

            TestServicePipDaemon.InvokeLogIpcResult(new CapturingLogger(), LogLevel.Info, "[TEST] ", merged, Stopwatch.GetTimestamp());

            XAssert.IsTrue(
                merged.ActionDuration < TimeSpan.FromMinutes(1),
                $"Expected the summed duration to be replaced by the elapsed time, but got {merged.ActionDuration}");
        }

        [Fact]
        public void ConfiguredCommandCapturesAStartTimestamp()
        {
            var first = CreateConfiguredCommand();
            var second = CreateConfiguredCommand();

            XAssert.IsTrue(first.StartTimestamp > 0, "Expected a start timestamp to be captured on construction.");
            XAssert.IsTrue(
                second.StartTimestamp >= first.StartTimestamp,
                "Expected a later command to carry a start timestamp that is not earlier than an earlier one's.");
        }

        private static ConfiguredCommand CreateConfiguredCommand()
            => new ConfiguredCommand(new Command(name: "test", options: Array.Empty<Option>()), config: null, logger: null);

        /// <summary>
        /// Minimal subclass used only to reach the protected static <see cref="ServicePipDaemon.LogIpcResult"/>.
        /// It is never instantiated.
        /// </summary>
        private sealed class TestServicePipDaemon : ServicePipDaemon
        {
            private TestServicePipDaemon()
                : base(null, null, null)
            {
            }

            public static void InvokeLogIpcResult(IIpcLogger logger, LogLevel level, string prefix, IIpcResult result, long startTimestamp)
                => LogIpcResult(logger, level, prefix, result, startTimestamp);
        }

        private sealed class CapturingLogger : IIpcLogger
        {
            public string LastMessage { get; private set; }

            public void Log(LogLevel level, string format, params object[] args) => LastMessage = string.Format(format, args);

            public void Log(LogLevel level, StringBuilder message) => LastMessage = message.ToString();

            public void Log(LogLevel level, string header, IEnumerable<string> items, bool placeItemsOnSeparateLines) => LastMessage = header;

            public void Dispose()
            {
            }
        }
    }
}
