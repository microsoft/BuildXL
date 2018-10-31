// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class HashCodeHelperTests : XunitBuildXLTest
    {
        public HashCodeHelperTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void Basic()
        {
            // One guaranteed property of the HashCodeHelper class is that the hash code are stable across runs.
            // That's why we can just check for particular values here.
            XAssert.AreEqual(0, HashCodeHelper.GetOrdinalHashCode((string)null));
            XAssert.AreEqual(-2128831035, HashCodeHelper.GetOrdinalHashCode(string.Empty));
            XAssert.AreEqual(966025949, HashCodeHelper.GetOrdinalHashCode("abc"));

            XAssert.AreEqual(0, HashCodeHelper.GetOrdinalHashCode64((string)null));
            XAssert.AreEqual(-3750763034362895579, HashCodeHelper.GetOrdinalHashCode64(string.Empty));
            XAssert.AreEqual(-1983763625871892739, HashCodeHelper.GetOrdinalHashCode64("abc"));

            XAssert.AreEqual(0, HashCodeHelper.GetOrdinalIgnoreCaseHashCode((string)null));
            XAssert.AreEqual(-2128831035, HashCodeHelper.GetOrdinalIgnoreCaseHashCode(string.Empty));
            XAssert.AreEqual(1635911229, HashCodeHelper.GetOrdinalIgnoreCaseHashCode("abc"));
            XAssert.AreEqual(1635911229, HashCodeHelper.GetOrdinalIgnoreCaseHashCode("ABC"));

            XAssert.AreEqual(0, HashCodeHelper.GetOrdinalIgnoreCaseHashCode64((string)null));
            XAssert.AreEqual(-3750763034362895579, HashCodeHelper.GetOrdinalIgnoreCaseHashCode64(string.Empty));
            XAssert.AreEqual(7190942587306597981, HashCodeHelper.GetOrdinalIgnoreCaseHashCode64("abc"));
            XAssert.AreEqual(7190942587306597981, HashCodeHelper.GetOrdinalIgnoreCaseHashCode64("ABC"));

            XAssert.AreEqual(80953896, HashCodeHelper.GetHashCode(10000000000000));
            XAssert.AreEqual(-373379628, HashCodeHelper.Combine(1, 2));
            XAssert.AreEqual(1335152885, HashCodeHelper.Combine(1, 2, 3));
            XAssert.AreEqual(1724866073, HashCodeHelper.Combine(1, 2, 3, 4));
            XAssert.AreEqual(-1796374390, HashCodeHelper.Combine(1, 2, 3, 4, 5));
            XAssert.AreEqual(347556712, HashCodeHelper.Combine(1, 2, 3, 4, 5, 6));
            XAssert.AreEqual(-1444314891, HashCodeHelper.Combine(1, 2, 3, 4, 5, 6, 7));
            XAssert.AreEqual(-2111553299, HashCodeHelper.Combine(1, 2, 3, 4, 5, 6, 7, 8));

            XAssert.AreEqual(0, HashCodeHelper.Combine((int[])null));
            XAssert.AreEqual(-2128831035, HashCodeHelper.Combine(new int[0]));
            XAssert.AreEqual(-2123527734, HashCodeHelper.Combine(new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));

            XAssert.AreEqual(0, HashCodeHelper.Combine((byte[])null));
            XAssert.AreEqual(-2128831035, HashCodeHelper.Combine(new byte[0]));
            XAssert.AreEqual(172248326, HashCodeHelper.Combine(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));

            XAssert.AreEqual(0, HashCodeHelper.Combine((uint[])null, x => (int)x));
            XAssert.AreEqual(-2128831035, HashCodeHelper.Combine(new uint[0], x => (int)x));
            XAssert.AreEqual(-2123527734, HashCodeHelper.Combine(new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, x => (int)x));

            XAssert.AreEqual(-2623929010628771212, HashCodeHelper.Combine(1L, 2L));
            XAssert.AreEqual(1511739644222804357, HashCodeHelper.Combine(1L, 2L, 3L));
            XAssert.AreEqual(-5283981538138724167, HashCodeHelper.Combine(1L, 2L, 3L, 4L));

            XAssert.AreEqual(0, HashCodeHelper.Combine((ulong[])null, x => (long)x));
            XAssert.AreEqual(-3750763034362895579, HashCodeHelper.Combine(new ulong[0], x => (long)x));
            XAssert.AreEqual(5692564275626808042, HashCodeHelper.Combine(new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, x => (long)x));
        }
    }
}
