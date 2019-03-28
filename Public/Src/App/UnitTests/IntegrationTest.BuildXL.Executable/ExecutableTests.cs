// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Executable
{
    /// <summary>
    /// Unit tests that run the BuildXL executable directly.
    /// </summary>
    public class ExecutableTests : ExecutableTestBase
    {
        public ExecutableTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void CleanOnlyBuild()
        {
            RunBuild("/cleanonly /viewer:disable /TraceInfo:RunCheckInTests=CleanOnly").AssertSuccess();
        }

        [Fact]
        public void FullyCachedBuild()
        {
            // Use a cache config that disables the shared cache
            // Building detached to produce stable fingerprints file
            var build1 = RunBuild($"/incrementalScheduling- /TraceInfo:RunCheckInTests=CompareFingerprints1").AssertSuccess();

            var firstFingerprintOut = CreateUniquePath();
            // Build a second time to ensure that all tasks are cached
            RunAnalyzer($"/m:FingerprintText /xl:{build1.LogsDirectory} /compress- /o:{firstFingerprintOut}");

            var build2 = RunBuild($"/incrementalScheduling- /TraceInfo:RunCheckInTests=CompareFingerprints2 /cacheGraph-").AssertSuccess();

            var secondFingerprintOut = CreateUniquePath();
            RunAnalyzer($"/m:FingerprintText /xl:{build2.LogsDirectory} /compress- /o:{secondFingerprintOut}");

            var firstFingerprintText = File.ReadAllText(firstFingerprintOut);
            var secondFingerprintText = File.ReadAllText(secondFingerprintOut);

            XAssert.AreEqual(firstFingerprintText, secondFingerprintText, $"Fingerprints files differ.\nFirst build fingerprints: {firstFingerprintOut}\nSecond build Fingerprints: {secondFingerprintOut}");

            var logFile2 = File.ReadAllText(build2.LogFile);
            XAssert.IsTrue(VerifyBuildIsFullyCached(logFile2), $"Build was not fully cached. Logs: {logFile2}");
            XAssert.Fail();
        }

        public bool VerifyBuildIsFullyCached(string logFileText)
        {
            if (!logFileText.Contains("Running from existing BuildXL server process"))
            {
                return false;
            }

            var processLaunchedString = "Processes that were launched: ";
            var processLaunchedCount = ParseStringForInt(logFileText, processLaunchedString, Environment.NewLine);

            return 0 == processLaunchedCount;
        }
    }
}