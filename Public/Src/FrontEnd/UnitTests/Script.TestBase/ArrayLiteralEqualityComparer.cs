// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Values;

#pragma warning disable 1591

namespace Test.DScript.Ast.Interpretation
{
    public static class ArrayLiteralEqualityComparer
    {
        /// <nodoc />
        public static bool AreEqual(object expected, EvaluationResult actual) => AreEqual(expected, actual.Value);

        /// <summary>
        /// Returns true if two instances are array like and equal.
        /// </summary>
        public static bool AreEqual(object expected, object actual)
        {
            if (expected == null)
            {
                return actual == null;
            }

            if (actual == null)
            {
                return false;
            }

            if (expected is int[] expectedAsIntArray)
            {
                if (!(actual is ArrayLiteral actualAsArrayLiteral))
                {
                    return false;
                }

                if (expectedAsIntArray.Length != actualAsArrayLiteral.Length)
                {
                    return false;
                }

                for (int i = 0; i < expectedAsIntArray.Length; i++)
                {
                    if (!AreEqual(expectedAsIntArray[i], actualAsArrayLiteral.Values[i].Value))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (expected is object[] expectedAsArray)
            {
                if (!(actual is ArrayLiteral actualAsArrayLiteral))
                {
                    return false;
                }

                if (expectedAsArray.Length != actualAsArrayLiteral.Length)
                {
                    return false;
                }

                for (int i = 0; i < expectedAsArray.Length; i++)
                {
                    if (!AreEqual(expectedAsArray[i], actualAsArrayLiteral.Values[i].Value))
                    {
                        return false;
                    }
                }

                return true;
            }

            return Equals(expected, actual);
        }
    }
}
