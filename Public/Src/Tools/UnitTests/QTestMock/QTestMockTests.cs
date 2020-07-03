// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.Tool.QTestMock
{
    public sealed class QTestMockTests
    {
        [Fact]
        public void TestAddition()
        {
            XAssert.IsTrue(4 == 2 + 2);
        }

    }
}
