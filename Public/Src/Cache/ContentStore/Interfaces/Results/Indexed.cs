// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Container class for storing an item along with an index
    /// </summary>
    public readonly record struct Indexed<T>
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

        /// <summary>
        /// Deconstructs an instance for pattern matching.
        /// </summary>
        public void Deconstruct(
            out T item,
            out int index
        ) => (item, index) = (Item, Index);

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Index={Index}, Item={Item}";
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

        /// <nodoc />
        public static async Task<Indexed<T>> WithIndexAsync<T>(this Task<T> task, int index)
        {
            var item = await task;
            return new Indexed<T>(item, index);
        }
    }

#pragma warning restore SA1204 // Static elements must appear before instance elements
#pragma warning restore SA1402 // File may only contain a single class

}
