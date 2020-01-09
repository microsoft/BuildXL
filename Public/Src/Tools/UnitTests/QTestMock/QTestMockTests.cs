// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

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
