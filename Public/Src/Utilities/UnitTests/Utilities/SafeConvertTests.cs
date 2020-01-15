// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
