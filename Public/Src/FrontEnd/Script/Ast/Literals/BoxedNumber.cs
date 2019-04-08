// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Literals
{
    internal static class BoxedNumber
    {
        private const int BoxedNumbersCount = 1024;
        private static readonly object[] s_values = CreateValues();

        private static object[] CreateValues()
        {
            var values = new object[BoxedNumbersCount];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = i;
            }

            return values;
        }

        /// <summary>
        /// Boxes an integer
        /// </summary>
        public static object Box(int value)
        {
            var boxedNumbers = s_values;
            return unchecked((uint)value < (uint)boxedNumbers.Length) ? boxedNumbers[value] : (object)value;
        }
    }
}
