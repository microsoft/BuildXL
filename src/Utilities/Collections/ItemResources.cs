// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Information about the resource usage of an item
    /// </summary>
    public readonly struct ItemResources : IEquatable<ItemResources>
    {
        /// <summary>
        /// For each registered semaphore, indicating the increment.
        /// </summary>
        /// <remarks>
        /// <code>null</code> indicates that no semaphores are incremented.
        /// Design note: We only ever expect a handful of semaphores, that's why we keep the increments in an array, instead of a different more complex data structure that would be more efficient with sparse data.
        /// </remarks>
        public readonly ReadOnlyArray<int> SemaphoreIncrements;

        /// <summary>
        /// The empty value, using no resources.
        /// </summary>
        public static readonly ItemResources Empty = new ItemResources(ReadOnlyArray<int>.Empty);

        /// <summary>
        /// Whether this value is valid.
        /// </summary>
        public bool IsValid => SemaphoreIncrements.IsValid;

        /// <summary>
        /// Creates an instance of this structure.
        /// </summary>
        public static ItemResources Create(int[] semaphoreIncrements)
        {
            // chop off trailing zeros to normalize
            if (semaphoreIncrements != null)
            {
                for (int i = semaphoreIncrements.Length - 1; i >= 0; i--)
                {
                    if (semaphoreIncrements[i] != 0)
                    {
                        return new ItemResources(ReadOnlyArray<int>.From(semaphoreIncrements, 0, i + 1));
                    }
                }
            }

            return Empty;
        }

        private ItemResources(ReadOnlyArray<int> semaphoreIncrements)
        {
            SemaphoreIncrements = semaphoreIncrements;
        }

        /// <summary>
        /// Indicates if a given object is a ItemResources equal to this one. See <see cref="Equals(ItemResources)" />.
        /// </summary>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public bool Equals(ItemResources other)
        {
            return SemaphoreIncrements == other.SemaphoreIncrements;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return SemaphoreIncrements.GetHashCode();
        }

        /// <summary>
        /// Equality operator for two SemaphoreIncrements
        /// </summary>
        public static bool operator ==(ItemResources left, ItemResources right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for two SemaphoreIncrements
        /// </summary>
        public static bool operator !=(ItemResources left, ItemResources right)
        {
            return !left.Equals(right);
        }
    }
}
