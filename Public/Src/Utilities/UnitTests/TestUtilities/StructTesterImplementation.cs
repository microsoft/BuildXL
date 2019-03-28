// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Implementation for StructTester
    /// </summary>
    public static class StructTesterImplementation
    {
        /// <summary>
        /// Tests equality operations on the given struct.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1026:DefaultParametersShouldNotBeUsed", Justification = "Convenience function for testing")]
        public static void TestEquality<TStruct>(
            TStruct baseValue,
            TStruct equalValue,
            TStruct[] notEqualValues,
            Func<TStruct, TStruct, bool> eq,
            Func<TStruct, TStruct, bool> neq,
            Action<bool, string> assertIsTrue,
            Action<bool, string> assertIsFalse,
            bool skipHashCodeForNotEqualValues = false)
            where TStruct : struct, IEquatable<TStruct>
        {
            Contract.Requires(notEqualValues != null);
            Contract.Requires(eq != null);
            Contract.Requires(neq != null);

            // IEquatable
            var equatableBase = (IEquatable<TStruct>)baseValue;
            assertIsTrue(equatableBase.Equals(equalValue), null);
            foreach (TStruct notEqualValue in notEqualValues)
            {
                assertIsFalse(equatableBase.Equals(notEqualValue), null);
            }

            // Equals on object
            var objectBase = (object)baseValue;
            assertIsFalse(objectBase.Equals(null), null);
            assertIsFalse(objectBase.Equals("StructsShouldNotBeEqualToAString"), null);
            assertIsTrue(objectBase.Equals(equalValue), null);
            foreach (TStruct notEqualValue in notEqualValues)
            {
                assertIsFalse(objectBase.Equals(notEqualValue), null);
            }

            // GetHashCode
            assertIsTrue(baseValue.GetHashCode() == equalValue.GetHashCode(), "Equal structs must have equal hashcode");
            foreach (TStruct notEqualValue in notEqualValues)
            {
                if (!skipHashCodeForNotEqualValues)
                {
                    assertIsFalse(
                        baseValue.GetHashCode() == notEqualValue.GetHashCode(),
                        "It should be highly unlikely that two values have the same hashcode.");
                }
            }

            // Operator ==
            assertIsTrue(eq(baseValue, equalValue), null);
            foreach (TStruct notEqualValue in notEqualValues)
            {
                assertIsFalse(eq(baseValue, notEqualValue), null);
            }

            // Operator !=
            assertIsFalse(neq(baseValue, equalValue), null);
            foreach (TStruct notEqualValue in notEqualValues)
            {
                assertIsTrue(neq(baseValue, notEqualValue), null);
            }
        }
    }
}
