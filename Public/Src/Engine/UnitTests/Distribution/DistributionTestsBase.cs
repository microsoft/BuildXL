// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Distribution
{
    public class DistributionTestsBase : XunitBuildXLTest
    {
        protected ConfigurationImpl Configuration;

        protected void ResetConfiguration()
        {
            Configuration = new ConfigurationImpl();
        }
    
        public DistributionTestsBase(ITestOutputHelper output) : base(output)
        {
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            ResetConfiguration();
        }
    }
}