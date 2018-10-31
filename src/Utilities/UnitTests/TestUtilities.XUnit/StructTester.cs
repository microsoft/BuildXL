// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Xunit;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Helper class for testing equality on structs.
    /// </summary>
    public static class StructTester
    {
        /// <summary>
        /// Tests equality operations on the given struct.
        /// </summary>
        public static void TestEquality<TStruct>(
            TStruct baseValue,
            TStruct equalValue,
            TStruct[] notEqualValues,
            Func<TStruct, TStruct, bool> eq,
            Func<TStruct, TStruct, bool> neq,
            bool skipHashCodeForNotEqualValues = false)
            where TStruct : struct, IEquatable<TStruct>
        {
            StructTesterImplementation.TestEquality<TStruct>(
                baseValue,
                equalValue,
                notEqualValues,
                eq,
                neq,
                AssertIsTrue,
                AssertIsFalse,
                skipHashCodeForNotEqualValues);
        }

        private static void AssertIsTrue(bool condition, string message = null)
        {
            if (message == null)
            {
                Assert.True(condition);
            }
            else
            {
                Assert.True(condition, message);
            }
        }

        private static void AssertIsFalse(bool condition, string message = null)
        {
            if (message == null)
            {
                Assert.False(condition);
            }
            else
            {
                Assert.False(condition, message);
            }
        }
    }
}
