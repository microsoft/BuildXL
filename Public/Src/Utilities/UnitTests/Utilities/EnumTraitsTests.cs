// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class EnumTraitsTests
    {
        [Fact]
        public void CanConvertToInteger()
        {
            XAssert.AreEqual((ulong)TestEnum.A, EnumTraits<TestEnum>.ToInteger(TestEnum.A));
            XAssert.AreEqual((ulong)TestEnum.B, EnumTraits<TestEnum>.ToInteger(TestEnum.B));
            XAssert.AreEqual((ulong)TestEnum.C, EnumTraits<TestEnum>.ToInteger(TestEnum.C));
        }

        [Fact]
        public void CanConvertFromInteger()
        {
            XAssert.AreEqual(TestEnum.A, FromInteger((ulong)TestEnum.A));
            XAssert.AreEqual(TestEnum.B, FromInteger((ulong)TestEnum.B));
            XAssert.AreEqual(TestEnum.C, FromInteger((ulong)TestEnum.C));

            TestEnum notFound;
            EnumTraits<TestEnum>.TryConvert(123, out notFound);
            XAssert.AreEqual(default(TestEnum), notFound);
        }

        [Fact]
        public void CanGetRange()
        {
            XAssert.AreEqual<ulong>(1, EnumTraits<TestEnum>.MinValue);
            XAssert.AreEqual<ulong>(3, EnumTraits<TestEnum>.MaxValue);
        }

        private TestEnum FromInteger(ulong integerValue)
        {
            Contract.Assume(Enum.IsDefined(typeof(TestEnum), (int)integerValue));
            TestEnum value;
            bool found = EnumTraits<TestEnum>.TryConvert(integerValue, out value);
            XAssert.IsTrue(found, "Failed to find a value");
            return value;
        }

        private enum TestEnum : int
        {
            A = 1,
            B = 2,
            C = 3
        }
    }
}
