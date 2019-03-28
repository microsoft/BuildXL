// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    internal static class MultiLevelUtilities
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
    }
}
