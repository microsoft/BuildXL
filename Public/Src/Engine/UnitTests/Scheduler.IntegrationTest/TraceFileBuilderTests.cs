// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class TraceFileBuilderTests : SchedulerIntegrationTestBase
    {
        public TraceFileBuilderTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void AddingTraceFileCausesCacheMiss()
        {
            var builder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(CreateSourceFile()),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            var pip = SchedulePipBuilder(builder).Process;
            RunScheduler().AssertCacheMiss(pip.PipId);
            // Nothing has changed, so we should have a cache hit.
            RunScheduler().AssertCacheHit(pip.PipId);

            ResetPipGraphBuilder();

            // Now, add a trace file to the pip.
            builder.SetTraceFile(CreateOutputFileArtifact().Path);
            pip = SchedulePipBuilder(builder).Process;
            // We should not have a hit because the pip must have a different fingerprint now.
            RunScheduler().AssertCacheMiss(pip.PipId);
            // However, if we re-run the pip, we should get a hit.
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        [Fact]
        public void TraceFileExistsAndContainsFileAccessesPerformedByPipAndItIsReplayedOnCacheHit()
        {
            var input = CreateSourceFile();
            var inputPath = input.Path.ToString(Context.PathTable);
            var output = CreateOutputFileArtifact();
            var outputPath = output.Path.ToString(Context.PathTable);
            var traceFile = CreateOutputFileArtifact();
            var traceFilePath = traceFile.Path.ToString(Context.PathTable);
            var builder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(input),
                Operation.WriteFile(output),
            });

            XAssert.IsFalse(File.Exists(traceFilePath));

            builder.SetTraceFile(traceFile);
            var pip = SchedulePipBuilder(builder).Process;
            RunScheduler().AssertSuccess();

            // The file must be on disk now.
            XAssert.IsTrue(File.Exists(traceFilePath));

            var traceFileContent = File.ReadAllText(traceFilePath);

            // Obviously, the path of a trace file itself is not in the trace file.
            XAssert.IsFalse(traceFileContent.Contains(traceFilePath));

            // On Windows, the following paths must appear exactly two times -- once in the command line of a test process (test infra
            // puts details about all operations there) and once at their appropriate place in the file. On Unix, our sandbox is not
            // reporting process arguments, so there should be exactly one match.
            var expectedMatchCount = OperatingSystemHelper.IsWindowsOS ? 2 : 1;
            var regexOptions = OperatingSystemHelper.IsPathComparisonCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            XAssert.AreEqual(expectedMatchCount, Regex.Matches(traceFileContent, Regex.Escape(inputPath), regexOptions).Count);
            XAssert.AreEqual(expectedMatchCount, Regex.Matches(traceFileContent, Regex.Escape(outputPath), regexOptions).Count);

            // There should be more than one Read operation
            // (",0,0,\r?$" is for a successful, non-augmented operation without any enumeration pattern).
            XAssert.IsTrue(Regex.Matches(traceFileContent, @$",{(byte)RequestedAccess.Read},0,0,\r?$", RegexOptions.Multiline).Count > 1);
            if (OperatingSystemHelper.IsWindowsOS)
            {
                // On Windows, there must be exactly one write operation (for the 'output' file).
                XAssert.AreEqual(1, Regex.Matches(traceFileContent, @$",{(byte)RequestedAccess.Write},0,0,\r?$", RegexOptions.Multiline).Count);
            }
            else
            {
                // On Unix, the sandbox reports writes to in/out pipes, so we have more than one write access.
                XAssert.IsTrue(Regex.Matches(traceFileContent, @$",{(byte)RequestedAccess.Write},0,0,\r?$", RegexOptions.Multiline).Count > 1);
            }

            // Now delete the file.
            File.Delete(traceFilePath);
            // Sanity check.
            XAssert.IsFalse(File.Exists(traceFilePath));

            // Run the second time, the trace file  must be replayed from cache.
            RunScheduler().AssertCacheHit(pip.PipId);
            XAssert.IsTrue(File.Exists(traceFilePath));
        }
    }
}
