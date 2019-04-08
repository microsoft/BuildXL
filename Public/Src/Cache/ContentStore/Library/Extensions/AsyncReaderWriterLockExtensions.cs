// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Synchronization;

namespace BuildXL.Cache.ContentStore.Extensions
{
    /// <summary>
    ///     Extensions method for <see cref="AsyncReaderWriterLock"/>.
    /// </summary>
    public static class AsyncReaderWriterLockExtensions
    {
        /// <summary>
        ///     Runs <paramref name="func"/> with read lock.
        /// </summary>
        public static T WithReadLock<T>(this AsyncReaderWriterLock locker, Func<T> func)
        {
            using (locker.ReaderLockAsync().Result)
            {
                return func();
            }
        }

        /// <summary>
        ///     Runs <paramref name="func"/> with read lock.
        /// </summary>
        public static T WithWriteLock<T>(this AsyncReaderWriterLock locker, Func<T> func)
        {
            using (locker.ReaderLockAsync().Result)
            {
                return func();
            }
        }

        /// <summary>
        ///     Runs <paramref name="funcAsync"/> with read lock.
        /// </summary>
        public static async Task<T> WithReadLockAsync<T>(this AsyncReaderWriterLock locker, Func<Task<T>> funcAsync)
        {
            using (await locker.ReaderLockAsync())
            {
                return await funcAsync();
            }
        }

        /// <summary>
        ///     Runs <paramref name="funcAsync"/> with write lock.
        /// </summary>
        public static async Task<T> WithWriteLockAsync<T>(this AsyncReaderWriterLock locker, Func<Task<T>> funcAsync)
        {
            using (await locker.WriterLockAsync())
            {
                return await funcAsync();
            }
        }

        /// <summary>
        ///     Runs <paramref name="funcAsync"/> with read lock.
        /// </summary>
        public static async Task WithReadLockAsync(this AsyncReaderWriterLock locker, Func<Task> funcAsync)
        {
            using (await locker.ReaderLockAsync())
            {
                await funcAsync();
            }
        }

        /// <summary>
        ///     Runs <paramref name="funcAsync"/> with write lock.
        /// </summary>
        public static async Task WithWriteLockAsync(this AsyncReaderWriterLock locker, Func<Task> funcAsync)
        {
            using (await locker.WriterLockAsync())
            {
                await funcAsync();
            }
        }
    }
}
