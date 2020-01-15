// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace ExternalToolTest.BuildXL.Scheduler
{
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true, requiresSymlinkPermission: true)]
    public class ExternalVmExecutionTests : ExternalToolExecutionTests
    {
        public ExternalVmExecutionTests(ITestOutputHelper output) : base(output)
        {
            Configuration.Sandbox.AdminRequiredProcessExecutionMode = global::BuildXL.Utilities.Configuration.AdminRequiredProcessExecutionMode.ExternalVM;
            Configuration.Sandbox.RedirectedTempFolderRootForVmExecution = CreateUniqueDirectory(ObjectRootPath);
        }
    }
}
