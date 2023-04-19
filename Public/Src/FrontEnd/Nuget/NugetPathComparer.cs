// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// Compares two relative paths in hierarchical order, starting with the atom closer to the root. Each atom is compared as a string, case insensitive.
    /// </summary>
    /// <remarks>
    /// For example, consider these two paths:
    /// 
    /// 1- lib/net6.0-android31/Microsoft.Identity.Client.dll
    /// 2- lib/net6.0/Microsoft.Identity.Client.dll
    /// 
    /// A regular string-based comparison would determine 1 &lt; 2, because the prefix string 'lib/net6.0-' is lexicographically smaller than 'lib/net6.0/'. On the other hand
    /// this comparer will determine that 2 &lt; 1, because the second atom on both paths is the first one that differs (starting from the root) and the string 'net6.0' is less than the string 'net6.0-android31'.
    /// </remarks>
    internal class NugetRelativePathComparer : IComparer<RelativePath>
    {
        private readonly StringTable m_stringTable;

        /// <nodoc/>
        public NugetRelativePathComparer(StringTable stringTable) 
        {
            Contract.Requires(stringTable != null); 
            m_stringTable = stringTable;
        }

        /// <inheritdoc/>
        public int Compare(RelativePath left, RelativePath right)
        {
            Contract.Requires(left.IsValid);
            Contract.Requires(right.IsValid);

            var leftAtoms = left.GetAtoms();
            var rightAtoms = right.GetAtoms();
            // Let's go in order, starting from the atom closer to the root
            for(var i = 0; i < Math.Min(leftAtoms.Length, rightAtoms.Length); i++)
            {
                // Each pair is compared as strings - case insensitive (even on Linux, nuget is case insensitive across the board, so path differing in casing should be
                // understood as the same path
                var comparison = StringComparer.OrdinalIgnoreCase.Compare(leftAtoms[i].ToString(m_stringTable), rightAtoms[i].ToString(m_stringTable));
                
                // If the pair of atoms are different, that determines the comparison of the whole path
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            // If all atoms are the same up to the minimum length that is present on both sides,
            // the one with less atoms is smaller
            return leftAtoms.Length - rightAtoms.Length;

        }
    }
}
