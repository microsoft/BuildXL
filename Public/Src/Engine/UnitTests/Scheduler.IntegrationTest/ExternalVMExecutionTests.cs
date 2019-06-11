// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
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
