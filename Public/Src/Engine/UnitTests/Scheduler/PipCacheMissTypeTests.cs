// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    internal class PipCacheMissTypeTests : XunitBuildXLTest
    {
        public PipCacheMissTypeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void AllCacheMissesHaveCounters()
        {
            foreach (PipCacheMissType type in PipCacheMissTypeExtensions.AllCacheMisses) 
            {
                try
                {
                    var counter = type.ToCounter();
                    var frontier = counter.ToFrontierPipCacheMissCounter();
                }
                catch (Exception e)
                {
                    XAssert.Fail($"{nameof(PipCacheMissType)} {type} has no associated counter: {e}");
                }
            }
        }

        [Fact]
        public void CacheHitTypeHasCounterButNotFrontierCounter()
        {
            PipExecutorCounter counter = default;

            try
            {
                counter = PipCacheMissType.Hit.ToCounter();
            }
            catch (Exception e)
            {
                XAssert.Fail($"{nameof(PipCacheMissType.Hit)} has no associated counter: {e}");
            }

            Assert.Throws<ArgumentException>(() => counter.ToFrontierPipCacheMissCounter());
        }

        [Fact]
        public void InvalidTypeHasNoAssociatedCounter()
        {
            Assert.Throws<ArgumentException>(() => PipCacheMissType.Invalid.ToCounter());
        }

        [Fact]
        public void AllCacheMissesExcludeHitAndInvalid()
        {
            Assert.DoesNotContain(PipCacheMissType.Hit, PipCacheMissTypeExtensions.AllCacheMisses);
            Assert.DoesNotContain(PipCacheMissType.Invalid, PipCacheMissTypeExtensions.AllCacheMisses);
        }
    }
}
