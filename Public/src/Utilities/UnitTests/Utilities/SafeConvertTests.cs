// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class SafeConvertTests
    {
        [Fact]
        public void TestNoOverflows()
        {
            XAssert.AreEqual(0, SafeConvert.ToInt32(double.MaxValue), "bigInt");
            XAssert.AreEqual(0, SafeConvert.ToInt32(double.MinValue), "smallInt");
            XAssert.AreEqual(15, SafeConvert.ToInt32(15.3234));

            XAssert.AreEqual(0, SafeConvert.ToLong(double.MaxValue), "bigLong");
            XAssert.AreEqual(0, SafeConvert.ToLong(double.MinValue), "smallLong");
            XAssert.AreEqual(15, SafeConvert.ToLong(15.3234));
        }
    }
}
