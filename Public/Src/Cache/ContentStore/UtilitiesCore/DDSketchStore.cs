// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.UtilitiesCore.Sketching
{
    /// <summary>
    /// Basic methods required to be implemented in order to have a valid instance of the store needed by
    /// <see cref="DDSketch"/>. Not intended to be used elsewhere.
    /// </summary>
    public abstract class DDSketchStore
    {
        /// <summary>
        /// Adds a new element to the appropriate bucket.
        /// </summary>
        public abstract void Add(int index);

        /// <summary>
        /// Obtains index at which <paramref name="rank"/> elements have been seen.
        /// </summary>
        public abstract int IndexOf(int rank);

        /// <summary>
        /// Copies from the designated store, overriding the stored values.
        /// </summary>
        public abstract void Copy(DDSketchStore store);

        /// <summary>
        /// Merges the designated store's values with the current one.
        /// </summary>
        public abstract void Merge(DDSketchStore store);
    }
}
