// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

// ............................................................................................
// The classes in this file contains a truth table class that can be used in
// theories in XUnit so that you do not have to type in inline data attributes all the time,
// puts all the boolean data in one place, and avoids polluting your test cases with attributes.
//
// For example, you can annotate one of your theories with
// [MemberData(nameof(TruthTable.getTable), 3, MemberType = typeof(TruthTable))]
// to run your test cases against a 3D truth table (the second parameter is the dimension of the table).
//
// Annotating your theory with [MemberData(nameof(TruthTable.getTable), 3, MemberType = typeof(TruthTable))]
// has the same effect as adding the following InlineData Attributes:
//
// [InlineData(true, true, true)]
// [InlineData(true, true, false)]
// [InlineData(true, false, true)]
// [InlineData(false, true, true)]
// [InlineData(true, false, false)]
// [InlineData(false, false, true)]
// [InlineData(false, true, false)]
// [InlineData(false, false, false)]
// ............................................................................................
#pragma warning disable SA1649 // File name must match first type name

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Contains a permutation function to generate truth tables
    /// </summary>
    public class TruthTableUtil
    {
        private static void GetPerms(int dim, object[] row, int start, ref List<object[]> table)
        {
            if (start == dim)
            {
                object[] copy = new object[row.Length];
                Array.Copy(row, copy, row.Length);
                table.Add(copy);
                return;
            }

            row[start] = true;
            GetPerms(dim, row, start + 1, ref table);
            row[start] = false;
            GetPerms(dim, row, start + 1, ref table);
        }

        private static IEnumerable<object[]> GetTable(int dim, object[] row, int start, ref List<object[]> table)
        {
            GetPerms(dim, row, start, ref table);
            return table;
        }

        /// <summary>
        /// Returns a truth table of the given dimension (dim).
        /// </summary>
        public static IEnumerable<object[]> GetTable(int dim)
        {
            List<object[]> table = new List<object[]>();

            if (dim <= 0)
            {
                return table;
            }

            return GetTable(dim, new object[dim], 0, ref table);
        }
    }

    /// <summary>
    /// truth table
    /// </summary>
    public class TruthTable
    {
        /// <summary>
        /// Pass in the dimension of the table to get a truth table of that dimension in return
        /// </summary>
        public static IEnumerable<object[]> GetTable(int dim)
        {
            return TruthTableUtil.GetTable(dim);
        }
    }
}
