// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Xunit.Abstractions;

namespace BuildXL.Cache.Tests
{
    [ExcludeFromCodeCoverage]
    public class TestBasicFilesystemGcSharded : TestBasicFilesystemGc
    {
        public TestBasicFilesystemGcSharded(ITestOutputHelper output)
            : base(output)
        {
            TestType = new TestBasicFilesystemSharded();
        }

        protected override TestCacheCore TestType { get; }
    }
}
