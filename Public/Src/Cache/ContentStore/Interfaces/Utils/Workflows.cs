// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Interfaces.Utils
{
    /// <summary>
    /// Delegate which given a processes given input and returns a sequence of indexed results
    /// </summary>
    /// <typeparam name="TSource">Input type</typeparam>
    /// <typeparam name="TResult">Output type</typeparam>
    /// <param name="inputs">List of inputs</param>
    /// <returns>Sequence of indexed results</returns>
    public delegate Task<IEnumerable<Task<Indexed<TResult>>>> GetIndexedResults<in TSource, TResult>(IReadOnlyList<TSource> inputs);

    /// <summary>
    /// Class representing abstract workflows
    /// </summary>
    public static class Workflows
    {
        /// <summary>
        /// Processes given inputs with a fallback for failures
        /// </summary>
        /// <remarks>
        /// * Call first function in given inputs
        /// * Retry failures with fallback function
        /// * Fix indices for results of fallback function
        /// * Merge the results
        /// </remarks>
        public static async Task<IEnumerable<Task<Indexed<TResult>>>> RunWithFallback<TSource, TResult>(
            IReadOnlyList<TSource> inputs,
            GetIndexedResults<TSource, TResult> initialFunc,
            GetIndexedResults<TSource, TResult> fallbackFunc,
            Func<TResult, bool> isSuccessFunc,
            Func<IReadOnlyList<Indexed<TResult>>, Task> initialSuccessTaskFunc = null
            )
        {
            // Get results from first method
            IEnumerable<Task<Indexed<TResult>>> initialResults = await initialFunc(inputs);

            // Determine hits / misses based on given isSuccessFunc
            ILookup<bool, Indexed<TResult>> resultLookup = await initialResults.ToLookupAwait(r => isSuccessFunc(r.Item));

            IReadOnlyList<Indexed<TResult>> indexedSuccesses = resultLookup[true].ToList();
            IReadOnlyList<Indexed<TResult>> indexedFailures = resultLookup[false].ToList();

            // Optional action to process hits from first attempt
            if (initialSuccessTaskFunc != null)
            {
                await initialSuccessTaskFunc(indexedSuccesses);
            }

            // Return early if no misses
            if (indexedFailures.Count == 0)
            {
                return indexedSuccesses.AsTasks();
            }

            // Try fallback for items that failed in first attempt
            IReadOnlyList<TSource> missedInputs = indexedFailures.Select(r => inputs[r.Index]).ToList();
            IEnumerable<Task<Indexed<TResult>>> fallbackResults = await fallbackFunc(missedInputs);

            // Fix indices for fallback results to corresponding indices from original input
            IList<Indexed<TResult>> fixedFallbackResults = new List<Indexed<TResult>>(missedInputs.Count);
            foreach (var resultTask in fallbackResults)
            {
                Indexed<TResult> result = await resultTask;

                int originalIndex = indexedFailures[result.Index].Index;
                fixedFallbackResults.Add(result.Item.WithIndex(originalIndex));
            }

            // Merge original successful results with fallback results
            return indexedSuccesses
                    .Concat(fixedFallbackResults)
                    .AsTasks();
        }
    }
}
