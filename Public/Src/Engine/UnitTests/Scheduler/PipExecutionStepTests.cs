// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Engine.Distribution;
using BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public sealed class PipExecutionStepTests : XunitBuildXLTest
    {
        public PipExecutionStepTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void PipExecutionStepAsString()
        {
            foreach (PipExecutionStep val in Enum.GetValues(typeof(PipExecutionStep)))
            {
                XAssert.IsFalse(string.IsNullOrEmpty(val.AsString()));
            }
        }
    }
}
