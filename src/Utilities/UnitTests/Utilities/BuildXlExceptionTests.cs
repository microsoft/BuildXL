// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class BuildXlExceptionTests : XunitBuildXLTest
    {
        public BuildXlExceptionTests(ITestOutputHelper output)
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
