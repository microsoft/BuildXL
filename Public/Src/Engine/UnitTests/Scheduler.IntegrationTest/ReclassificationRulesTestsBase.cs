// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.Scheduler;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    /// <nodoc />
    public class ReclassificationRulesTestsBase : SchedulerIntegrationTestBase
    {
        public ReclassificationRulesTestsBase(ITestOutputHelper output) : base(output)
        {
        }

        protected static DiscriminatingUnion<ObservationType, UnitValue> Rt(ObservationType? observationType)
        {
            if (observationType == null)
            {
                return new DiscriminatingUnion<ObservationType, UnitValue>(UnitValue.Unit);
            }

            return new DiscriminatingUnion<ObservationType, UnitValue>(observationType.Value);
        }
    }
}
