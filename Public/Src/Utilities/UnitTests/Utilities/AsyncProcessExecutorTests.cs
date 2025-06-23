// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class AsyncProcessExecutorTests : XunitBuildXLTest
    {
        private readonly ITestOutputHelper m_output;
        private readonly Action<string> m_logger;
        

        public AsyncProcessExecutorTests(ITestOutputHelper output)
            : base(output)
        {
            m_output = output;
            m_logger = message => m_output.WriteLine(message);
        }

        [FactIfSupported(requiresLinuxBasedOperatingSystem: true)]
        public async Task TestAsyncProcessExecutorGentleKill()
        {
            var scriptPath = Path.Combine(TestOutputDirectory, "sigtermtest.sh");
            File.WriteAllLines(scriptPath,
            [
                "#!/bin/bash",
                "trap 'kill $sleep_pid 2>/dev/null;exit 5' SIGTERM",
                "sleep 10 &",
                "sleep_pid=$!",
                "wait"
            ]);

            // Create bash process to trap SIGTERM and return 1
            var process = new Process
            {
                StartInfo = new ProcessStartInfo("/bin/bash", scriptPath)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };

            var processExecutor = new AsyncProcessExecutor(process, TimeSpan.FromSeconds(2), logger: m_logger);
            processExecutor.Start();

            // Allow the process to initialize
            await Task.Delay(500);

            // Call gentle kill (sends SIGTERM)
            await processExecutor.KillAsync(dumpProcessTree: false, gentleKill: true);

            // Assert the process exited with 5 (trap handled SIGTERM)
            Assert.Equal(5, process.ExitCode);
        }
    }
}