// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    /// Set of extension methods for <see cref="IMemoizationSession"/> interface.
    /// </summary>
    public static class MemoizationSessionExtensions
    {
        /// <summary>
        /// Enumerate known selectors for a given weak fingerprint.
        /// </summary>
        public static IAsyncEnumerable<GetSelectorResult> GetSelectorsAsAsyncEnumerable(
            this ILevelSelectorsProvider session,
            Context context,
            Fingerprint weakFingerprint,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            // This method is a backward compatible method that was used before the implementation was moved to use levels explicitly.
            return AsyncEnumerable.Range(0, int.MaxValue)
                .SelectMany(level => GetLevelSelectorsEnumerableAsync(session, context, weakFingerprint, cts, level))
                .StopAfter(item => item.Succeeded && item.Value.HasMore)
                .SelectMany(ToSelectorResults);
        }

        /// <summary>
        /// Enumerates a given <paramref name="enumerable"/> until the predicate <paramref name="predicate"/> returns true.
        /// </summary>
        private static IAsyncEnumerable<T> StopAfter<T>(
            this IAsyncEnumerable<T> enumerable,
            Func<T, bool> predicate)
        {
            return AsyncEnumerable.Create(
                token =>
                {
                    var enumerator = enumerable.GetAsyncEnumerator(token);
                    bool stop = false;
                    return AsyncEnumerator.Create(
                        async () =>
                        {
                            if (stop)
                            {
                                return false;
                            }

                            if (await enumerator.MoveNextAsync(token))
                            {
                                if (!predicate(enumerator.Current))
                                {
                                    stop = true;
                                }

                                return true;
                            }

                            return false;
                        },
                        () => enumerator.Current,
                        () => enumerator.DisposeAsync());
                });
        }

        private static IAsyncEnumerable<GetSelectorResult> ToSelectorResults(Result<LevelSelectors> levelResult)
        {
            IEnumerable<GetSelectorResult> selectorResults;
            if (!levelResult)
            {
                selectorResults = new[] { new GetSelectorResult(levelResult) };
            }
            else
            {
                selectorResults = levelResult.Value.Selectors.Select(selector => new GetSelectorResult(selector));
            }

            return selectorResults.ToAsyncEnumerable();
        }

        private static IAsyncEnumerable<Result<LevelSelectors>> GetLevelSelectorsEnumerableAsync(
            this ILevelSelectorsProvider session,
            Context context,
            Fingerprint weakFingerprint,
            CancellationToken cts,
            int level)
        {
            return AsyncEnumerableExtensions.CreateSingleProducerTaskAsyncEnumerable<Result<LevelSelectors>>(
                async () =>
                {
                    var result = await session.GetLevelSelectorsAsync(context, weakFingerprint, cts, level);
                    return new[] { result };
                });
        }
    }
}

