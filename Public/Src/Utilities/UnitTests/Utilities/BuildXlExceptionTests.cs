// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class BuildXLExceptionTests : XunitBuildXLTest
    {
        public BuildXLExceptionTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void Constructor()
        {
            const string Message = "Hello";

            var ex = new BuildXLException(Message);
            XAssert.AreEqual(Message, ex.Message);

            var inner = new ArgumentException();
            ex = new BuildXLException(Message, inner);
            XAssert.AreEqual(Message, ex.Message);
            Assert.Equal(inner, ex.InnerException);
        }
    }
}
