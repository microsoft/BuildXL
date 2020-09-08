// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// Utilities for operations against multi-level caches
    /// </summary>
    public static class MultiLevelUtilities
    {
        /// <summary>
        /// Processes given inputs with on first level then optionally calls subset for second level
        /// </summary>
        /// <remarks>
        /// * Call first function in given inputs
        /// * Call specified inputs (based on first function results) with second function
        /// * Fix indices for results of second function
        /// * Merge the results
        /// </remarks>
        public static Task<IEnumerable<Task<Indexed<TResult>>>> RunManyLevelAsync<TSession, TSource, TResult>(
            IReadOnlyList<TSession> sessions,
            IReadOnlyList<TSource> inputs,
            Func<TSession, IReadOnlyList<TSource>, Task<IEnumerable<Task<Indexed<TResult>>>>> runAsync,
            Func<TResult, bool> useResult)
        {
            Contract.Requires(sessions.Count != 0);

            Task<IEnumerable<Task<Indexed<TResult>>>> results = runAsync(sessions[0], inputs);

            foreach (var session in sessions.Skip(1))
            {
                results = RunMultiLevelAsync(
                    inputs,
                    h => results,
                    hashes => runAsync(session, hashes),
                    useResult);
            }

            return results;
        }

        /// <summary>
        /// Processes given inputs with on first level then optionally calls subset for second level
        /// </summary>
        /// <remarks>
        /// * Call first function in given inputs
        /// * Call specified inputs (based on first function results) with second function
        /// * Fix indices for results of second function
        /// * Merge the results
        /// </remarks>
        public static async Task<IEnumerable<Task<Indexed<TResult>>>> RunMultiLevelAsync<TSource, TResult>(
            IReadOnlyList<TSource> inputs,
            GetIndexedResults<TSource, TResult> runFirstLevelAsync,
            GetIndexedResults<TSource, TResult> runSecondLevelAsync,
            Func<TResult, bool> useFirstLevelResult,
            Func<IReadOnlyList<Indexed<TResult>>, Task> handleFirstLevelOnlyResultsAsync = null
            )
        {
            // Get results from first method
            IEnumerable<Task<Indexed<TResult>>> initialResults = await runFirstLevelAsync(inputs);

            // Determine which inputs can use the first level results based on useFirstLevelResult()
            List<Indexed<TResult>> indexedFirstLevelOnlyResults = new List<Indexed<TResult>>();
            List<int> nextLevelIndices = null;

            foreach (var resultTask in initialResults)
            {
                var result = await resultTask;
                if (useFirstLevelResult(result.Item))
                {
                    indexedFirstLevelOnlyResults.Add(result);
                }
                else
                {
                    nextLevelIndices = nextLevelIndices ?? new List<int>();
                    nextLevelIndices.Add(result.Index);
                }
            }

            // Optional action to process hits from first attempt
            if (handleFirstLevelOnlyResultsAsync != null)
            {
                await handleFirstLevelOnlyResultsAsync(indexedFirstLevelOnlyResults);
            }

            // Return early if no misses
            if (nextLevelIndices == null)
            {
                return initialResults;
            }

            // Try fallback for items that failed in first attempt
            IReadOnlyList<TSource> missedInputs = nextLevelIndices.Select(index => inputs[index]).ToList();
            IEnumerable<Task<Indexed<TResult>>> fallbackResults = await runSecondLevelAsync(missedInputs);

            // Fix indices for fallback results to corresponding indices from original input
            IList<Indexed<TResult>> fixedFallbackResults = new List<Indexed<TResult>>(missedInputs.Count);
            foreach (var resultTask in fallbackResults)
            {
                Indexed<TResult> result = await resultTask;

                int originalIndex = nextLevelIndices[result.Index];
                fixedFallbackResults.Add(result.Item.WithIndex(originalIndex));
            }

            // Merge original successful results with fallback results
            return indexedFirstLevelOnlyResults
                    .Concat(fixedFallbackResults)
                    .AsTasks();
        }

        /// <summary>
        /// Processes given inputs with on first level then optionally calls subset for second level
        /// </summary>
        /// <remarks>
        /// * Call first function in given inputs
        /// * Call specified inputs (based on first function results) with second function
        /// * Fix indices for results of second function
        /// * Merge the results
        /// </remarks>
        public static async Task<IReadOnlyList<TResult>> RunMultiLevelWithMergeAsync<TSource, TResult>(
            IReadOnlyList<TSource> inputs,
            Func<IReadOnlyList<TSource>, Task<IReadOnlyList<TResult>>> runFirstLevelAsync,
            Func<IReadOnlyList<TSource>, Task<IReadOnlyList<TResult>>> runSecondLevelAsync,
            Func<TResult, TResult, TResult> mergeResults,
            Func<TResult, bool> useFirstLevelResult)
        {
            TResult[] results = new TResult[inputs.Count];

            // Get results from first method
            IReadOnlyList<TResult> initialResults = await runFirstLevelAsync(inputs);

            // Determine which inputs can use the first level results based on useFirstLevelResult()
            List<Indexed<TResult>> indexedFirstLevelOnlyResults = new List<Indexed<TResult>>();
            List<int> nextLevelIndices = null;

            for (int i = 0; i < initialResults.Count; i++)
            {
                var result = initialResults[i];
                if (useFirstLevelResult(result))
                {
                    results[i] = result;
                }
                else
                {
                    nextLevelIndices = nextLevelIndices ?? new List<int>();
                    nextLevelIndices.Add(i);
                }
            }

            // Return early if no misses
            if (nextLevelIndices == null)
            {
                return initialResults;
            }

            // Try fallback for items that failed in first attempt
            IReadOnlyList<TSource> missedInputs = nextLevelIndices.SelectList(index => inputs[index]);
            IReadOnlyList<TResult> fallbackResults = await runSecondLevelAsync(missedInputs);

            for (int i = 0; i < nextLevelIndices.Count; i++)
            {
                var originalIndex = nextLevelIndices[i];
                var result = fallbackResults[i];
                results[originalIndex] = mergeResults(initialResults[originalIndex], result);
            }

            return results;
        }
    }
}
