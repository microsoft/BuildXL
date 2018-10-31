// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Processes.SandboxedProcessReports;

namespace Test.BuildXL.Processes
{
    public class SandboxedProcessReportsTest : XunitBuildXLTest
    {
        public SandboxedProcessReportsTest(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void TestParseFileAccessReportLineFencePost()
        {
            var line = "Process:1|1|1|1|1|1|1|1|1|1|1|1";
            XAssert.AreEqual(12, line.Split('|').Length);
            var ok = FileAccessReportLine.TryParse(line, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out string error);
            XAssert.IsTrue(ok, error);
        }
    }
}
