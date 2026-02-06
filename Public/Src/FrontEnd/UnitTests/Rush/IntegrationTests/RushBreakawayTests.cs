// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush.IntegrationTests
{
    [Trait("Category", "RushIntegrationTests")]
    public class RushBreakawayTest : RushIntegrationTestBase
    {
        public RushBreakawayTest(ITestOutputHelper output)
            : base(output)
        {
        }

        // We need to execute in order to verify the presence of DFAs
        protected override EnginePhases Phase => EnginePhases.Execute;

        private static readonly string s_executable = OperatingSystemHelper.IsWindowsOS ? "cmd.exe" : "sh";

        // TODO [pgunasekara]: Remove ebpf flag
        private static IEnumerable<object[]> BreakawayMemberData()
        {
            // The process has 'hi' in its arguments and should breakaway
            yield return new object[] { $"[ {{ processName: a`{s_executable}`, requiredArguments: 'hi' }} ]", true};
            // The process does not have 'bye' in its arguments and should not breakaway
            yield return new object[] { $"[ {{ processName: a`{s_executable}`, requiredArguments: 'bye' }} ]", false};
            // The argument comparison should be case sensitive by default
            yield return new object[] { $"[ {{ processName: a`{s_executable}`, requiredArguments: 'HI' }} ]", false };
            // The case sensitive knob should be honored
            yield return new object[] { $"[ {{ processName: a`{s_executable}`, requiredArguments: 'HI', requiredArgumentsIgnoreCase: true }} ]", true };
        }

        // Bug #2354886: Test is flaky with eBPF on Linux
        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [MemberData(nameof(BreakawayMemberData))]
        public void BreakawayArgumentsAreHonored(string breakawayData, bool expectedToBreakaway)
        {
            // A write to a path outside of the package root should trigger a DFA
            var disallowedWrite = Path.Combine(TestOutputDirectory, "out.txt").Replace("\\", "/");

            // Create a process that spawns a new process that writes into the disallowed path
            // The process has 'hi' in its arguments.
            var config = Build(childProcessesToBreakawayFromSandbox: breakawayData)
                .AddJavaScriptProject("@ms/project-A", "src/A",
$@"const {{ spawn }} = require('node:child_process');
const p = spawn('echo', ['hi', '>', '{disallowedWrite}'], {{ shell: true }})
")
                .PersistSpecsAndGetConfiguration();

            // Let's not block the access, just warn about it
            ((UnsafeSandboxConfiguration)config.Sandbox.UnsafeSandboxConfiguration).UnexpectedFileAccessesAreErrors = false;
            
            var engineResult = RunRushProjects(
                config, 
                new[] {
                    ("src/A", "@ms/project-A"),
                });

            Assert.True(engineResult.IsSuccess);

            // The warning should be there iff the writing process didn't break away
            AssertWarningEventLogged(global::BuildXL.Scheduler.Tracing.LogEventId.FileMonitoringWarning, expectedToBreakaway ? 0 : 1);
        }
    }
}
