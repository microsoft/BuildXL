// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
