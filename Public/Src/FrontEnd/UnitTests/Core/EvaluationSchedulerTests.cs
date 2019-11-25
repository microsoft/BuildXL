// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
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
            var key = "some-key";
            var result = Enumerable
                .Range(1, count)
                .ToArray()
                .AsParallel()
                .Select(i => evalScheduler.ValueCacheGetOrAdd(key, () => Interlocked.Increment(ref counter)))
                .ToArray();
            XAssert.AreEqual(1, counter);
            var expectedResult = Enumerable.Range(1, count).Select(i => counter).ToArray();
            XAssert.ArrayEqual(expectedResult, result);
        }
    }
}