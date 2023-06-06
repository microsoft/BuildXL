// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.ParallelAlgorithms;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities.ParallelAlgorithmsTests
{
    public class ResultBasedActionBlockSlimTests
    {
        private readonly ITestOutputHelper m_helper;

        public ResultBasedActionBlockSlimTests(ITestOutputHelper helper) => m_helper = helper;

        [Fact]
        public async Task TestProcessAsync()
        {
            var block = new ResultBasedActionBlockSlim<int>(Environment.ProcessorCount, CancellationToken.None);
            var tasks = Enumerable.Range(0, 100).Select(i => block.ProcessAsync(() => Task.FromResult(i)));
            var results = await Task.WhenAll(tasks);
            XAssert.AreSetsEqual(Enumerable.Range(0, 100), results, true);
        }
    }
}
