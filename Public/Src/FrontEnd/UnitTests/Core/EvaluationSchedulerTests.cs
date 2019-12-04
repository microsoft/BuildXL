// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using System.Threading;
using BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Core
{
    public class EvaluationSchedulerTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public EvaluationSchedulerTests(ITestOutputHelper output) 
            : base(output)
        { }

        [Theory]
        [InlineData(100)]
        public void ValueCacheGetOrAddInvokesFactoryOnlyOnce(int count)
        {
            var evalScheduler = new EvaluationScheduler(degreeOfParallelism: 1);
            var counter = 0;
            var result = Enumerable
                .Range(1, count)
                .ToArray()
                .AsParallel()
                .Select(i => evalScheduler.ValueCacheGetOrAdd("some-key", () => Interlocked.Increment(ref counter)))
                .ToArray();
            XAssert.AreEqual(1, counter);
            var expectedResult = Enumerable.Range(1, count).Select(i => counter).ToArray();
            XAssert.ArrayEqual(expectedResult, result);
        }

        [Fact]
        public void ValueCacheIsNotStatic()
        {
            var es1 = new EvaluationScheduler(degreeOfParallelism: 1);
            var es2 = new EvaluationScheduler(degreeOfParallelism: 1);
            var key = "key";
            var counter = 0;
            var res1 = es1.ValueCacheGetOrAdd(key, () => Interlocked.Increment(ref counter));
            var res2 = es2.ValueCacheGetOrAdd(key, () => Interlocked.Increment(ref counter));
            XAssert.AreEqual(2, counter);
            XAssert.AreEqual(1, res1);
            XAssert.AreEqual(2, res2);
        }
    }
}
