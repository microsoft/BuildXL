// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.ParallelAlgorithms
{
    /// <summary>
    /// Accepts items for asynchronous processing.
    /// Disposing the queue stops accepting items and waits for all accepted work to complete.
    /// </summary>
    /// <typeparam name="T">The type of items accepted for processing.</typeparam>
    public interface IWorkQueue<T> : IAsyncDisposable, IDisposable
    {
        /// <summary>
        /// Adds an <paramref name="item"/> for asynchronous processing.
        /// </summary>
        void Enqueue(T item);
    }
}
