// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using BuildXL.Engine.Cache.KeyValueStores;
using RocksDbSharp;

namespace BuildXL.Engine.Cache
{
    /// <summary>
    /// Interface to enforce the types that BuildXL key-value stores must support.
    /// </summary>
    public interface IBuildXLKeyValueStore : IKeyValueStore<string, string>, IKeyValueStore<byte[], byte[]>
    {
        /// <summary>
        /// Iterates through all the keys in a column family and garbage collects.
        /// </summary>
        /// <param name="canCollect">Whether the key's entry can be garbage collected.</param>
        /// <param name="primaryColumnFamilyName">The column family to use. If null, the store's default column is used.</param>
        /// <param name="additionalColumnFamilyNames">
        /// If provided, any keys determined to be garbage collectable will also be removed across the column families specified by this parameter.
        /// The key removal will happen atomically across the primary and additional column families passed into this function.
        /// </param>
        /// <param name="cancellationToken">Cancellation token for terminating garbage collection (optional)</param>
        /// <param name="startValue">The start value for iterating keys to garbage collect (optional)</param>
        GarbageCollectResult GarbageCollect(
            Func<byte[], bool> canCollect, 
            string primaryColumnFamilyName = null, 
            IEnumerable<string> additionalColumnFamilyNames = null, 
            CancellationToken cancellationToken = default, 
            byte[] startValue = null);
        
        /// <summary>
        /// Iterates through all the keys in a column family and garbage collects.
        /// </summary>
        /// <param name="canCollect">Whether the key's entry can be garbage collected.</param>
        /// <param name="primaryColumnFamilyName">The column family to use. If null, the store's default column is used.</param>
        /// <param name="additionalColumnFamilyNames">
        /// If provided, any keys determined to be garbage collectable will also be removed across the column families specified by this parameter.
        /// The key removal will happen atomically across the primary and additional column families passed into this function.
        /// </param>
        /// <param name="cancellationToken">Cancellation token for terminating garbage collection (optional)</param>
        /// <param name="startValue">The start value for iterating keys to garbage collect (optional)</param>
        GarbageCollectResult GarbageCollectByKeyValue(
            Func<Iterator, bool> canCollect, 
            string primaryColumnFamilyName = null, 
            IEnumerable<string> additionalColumnFamilyNames = null, 
            CancellationToken cancellationToken = default, 
            byte[] startValue = null);

        /// <summary>
        /// Iterates through all the keys in a column family and garbage collects.
        /// </summary>
        /// <param name="canCollect">Whether the entry can be garbage collected.</param>
        /// <param name="columnFamilyName">The column family to use. If null, the store's default column is used.</param>
        /// <param name="cancellationToken">Cancellation token for terminating garbage collection (optional)</param>
        /// <param name="startValue">the start value for iterating keys to garbage collect (optional)</param>
        GarbageCollectResult GarbageCollect(Func<byte[], byte[], bool> canCollect, string columnFamilyName = null, CancellationToken cancellationToken = default, byte[] startValue = null);

        /// <summary>
        /// Iterates through all the keys in a column family and garbage collects.
        /// </summary>
        /// <param name="canCollect">Whether the key's entry can be garbage collected.</param>
        /// <param name="columnFamilyName">The column family to use. If null, the store's default column is used.</param>
        /// <param name="additionalColumnFamilies">
        /// If provided, any keys determined to be garbage collectable will also be removed across the column families specified by this parameter.
        /// The key removal will happen atomically across the primary and additional column families passed into this function.
        /// </param>
        /// <param name="cancellationToken">Cancellation token for terminating garbage collection (optional)</param>
        /// <param name="startValue">The start value for iterating keys to garbage collect (optional)</param>
        GarbageCollectResult GarbageCollect(
            Func<string, bool> canCollect, 
            string columnFamilyName = null, 
            IEnumerable<string> additionalColumnFamilies = null, 
            CancellationToken cancellationToken = default, 
            string startValue = null);

        /// <summary>
        /// Save a checkpoint into a given directory.
        /// </summary>
        void SaveCheckpoint(string targetDirectory);

        /// <summary>
        /// Create snapshot
        /// </summary>
        IBuildXLKeyValueStore CreateSnapshot();

        /// <summary>
        /// Retrieves the value of a given property for the database. These are values internal to the database, thus
        /// depending on the particular implementation.
        /// </summary>
        /// <param name="propertyName">Name of the property to fetch</param>
        /// <param name="columnFamilyName">The column family to use. If null, the store's default column is used.</param>
        string GetProperty(string propertyName, string columnFamilyName = null);
    }

    /// <summary>
    /// Stats about garbage collecting a <see cref="IBuildXLKeyValueStore"/>.
    /// </summary>
    public struct GarbageCollectResult
    {
        /// <summary>
        /// Number of keys garbage collected.
        /// </summary>
        public int RemovedCount;

        /// <summary>
        /// Total number of keys iterated through during garbage collection.
        /// </summary>
        public int TotalCount;

        /// <summary>
        /// The last key encountered during garbage collection.
        /// </summary>
        public byte[] LastKey;

        /// <summary>
        /// Batch size for removing keys.
        /// </summary>
        public int BatchSize;

        /// <summary>
        /// The maximum time to evict one batch of keys.
        /// </summary>
        public TimeSpan MaxBatchEvictionTime;

        /// <summary>
        /// Whether garbage collection was cancelled.
        /// </summary>
        public bool Canceled;
    }
}
