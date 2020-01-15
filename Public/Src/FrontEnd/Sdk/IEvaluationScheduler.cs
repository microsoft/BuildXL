// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Abstraction that controls tasks scheduling for evaluation phase.
    /// </summary>
    public interface IEvaluationScheduler
    {
        /// <summary>
        /// Evaluate value using a given factory method.
        /// </summary>
        Task<object> EvaluateValue(Func<Task<object>> evaluateValueFunction);

        /// <summary>
        /// Token for cooperative cancellation.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Used by the interpreter to request early cancellation.
        /// </summary>
        void Cancel();

        /// <summary>
        /// Similar to <see cref="ConcurrentDictionary{TKey, TValue}.GetOrAdd(TKey, Func{TKey, TValue})"/>
        /// except that it guarantees that <paramref name="factory"/> is called at most once for any given key.
        ///
        /// Value cache is global per DScript evaluation.
        /// </summary>
        T ValueCacheGetOrAdd<T>(string key, Func<T> factory);
    }
}
