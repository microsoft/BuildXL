// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Container class for storing an item along with an index
    /// </summary>
    /// <typeparam name="T">Type of inner item</typeparam>
    public class Indexed<T>
    {
        /// <summary>
        /// Gets the index of the item
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets the item
        /// </summary>
        public T Item { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Indexed{T}"/> class.
        /// </summary>
        public Indexed(T item, int index)
        {
            Contract.Requires(item != null);
            Contract.Requires(index >= 0);

            Item = item;
            Index = index;
        }
    }

#pragma warning disable SA1402 // File may only contain a single class
#pragma warning disable SA1204 // Static elements must appear before instance elements

    /// <summary>
    /// Extension methods for <see cref="Indexed{T}"/> type
    /// </summary>
    public static class IndexedExtensions
    {
        /// <summary>
        /// Creates an <see cref="Indexed{T}"/> item
        /// </summary>
        public static Indexed<T> WithIndex<T>(this T item, int index)
        {
            return new Indexed<T>(item, index);
        }
    }

#pragma warning restore SA1204 // Static elements must appear before instance elements
#pragma warning restore SA1402 // File may only contain a single class

}
