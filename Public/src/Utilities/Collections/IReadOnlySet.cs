// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Readonly view of a set.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    public interface IReadOnlySet<T> : IReadOnlyCollection<T>
    {
        /// <summary>
        /// Check whether given data exist in the set.
        /// </summary>
        bool Contains(T item);
    }

    /// <summary>
    /// Readonly representation of a set.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    public sealed class ReadOnlyHashSet<T> : HashSet<T>, IReadOnlySet<T>
    {
        /// <nodoc/>
        public ReadOnlyHashSet()
        {
        }

        /// <nodoc/>
        public ReadOnlyHashSet(IEqualityComparer<T> comparer)
            : base(comparer)
        {
        }

        /// <nodoc/>
        public ReadOnlyHashSet(IEnumerable<T> collection)
            : base(collection)
        {
        }
    }
}
