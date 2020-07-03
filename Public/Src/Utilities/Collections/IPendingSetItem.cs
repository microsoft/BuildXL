// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Defines lookup and update semantics for an item in a set.
    /// </summary>
    /// <typeparam name="TItem">the item type of the set</typeparam>
    public interface IPendingSetItem<TItem>
    {
        /// <summary>
        /// Gets the hash code of the item
        /// </summary>
        int HashCode { get; }

        /// <summary>
        /// Tests for equality with the given item in the set
        /// </summary>
        /// <param name="other">the item in the set to check for equality</param>
        /// <returns>true if the item is equal, otherwise false</returns>
        bool Equals(TItem other);

        /// <summary>
        /// Creates or updates an item to be placed in the set
        /// </summary>
        /// <param name="oldItem">the old item if performing an update operation</param>
        /// <param name="hasOldItem">indicates if create is being used in an update
        /// operation and therefore has an old item</param>
        /// <param name="remove">true to remove the item, otherwise false. This value should only be set to true during an update operation.</param>
        /// <returns>the item to be placed in the set</returns>
        TItem CreateOrUpdateItem(TItem oldItem, bool hasOldItem, out bool remove);
    }
}
