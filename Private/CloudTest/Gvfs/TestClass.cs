// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Xunit.Abstractions;

namespace BuildXL.CloudTest.Gvfs
{
    public class TestSample
    {
        [Fact]
        public void SamplePass()
        {
            Assert.True(true, "Testing success");
        }
    }
}