// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Engine.Distribution;
using BuildXL.Scheduler;
using BuildXL.Utilities.Core;
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

        [Fact]
        public void PipExecutionStepCode()
        {
            foreach (var group in EnumTraits<PipExecutionStep>.EnumerateValues().ToLookup(s => s.AsCode()))
            {
                XAssert.IsTrue((group.Key & 0x80) != 0, $"PipExecutionStep code '{group.Key:X2}' should have first bit set.");
                XAssert.IsTrue(group.Count() == 1, $"Conflicting code {group.Key:X2} for [{string.Join(", ", group)}]");
            }
        }
    }
}
