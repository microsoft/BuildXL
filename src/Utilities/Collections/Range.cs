// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Range validation utilities.
    /// </summary>
    public static class Range
    {
        /// <summary>
        /// Validates components of a range specification.
        /// </summary>
        [Pure]
        public static bool IsValid(int index, int count, int available)
        {
            Contract.Requires(available >= 0);

            // This code explicitly allows the case of index == available, but only when
            // count == 0. This degenerate case is permitted in order to avoid problems
            // for developers consuming APIs that are range-checked using this routine.
            // Particularly interesting is the fact {index == 0, count == 0, available == 0} is
            // considered valid.
            unchecked
            {
                return ((uint)index <= (uint)available) && ((uint)count <= (uint)(available - index));
            }

            // The above is equivalent to
            // return index >= 0 && index <= available && count >= 0 && count <= available - index;
        }

        /// <summary>
        /// Validates components of a range specification.
        /// </summary>
        [Pure]
        public static bool IsValid(int index, int count)
        {
            Contract.Requires(count >= 0);

            return unchecked((uint)index < (uint)count);
        }
    }
}
