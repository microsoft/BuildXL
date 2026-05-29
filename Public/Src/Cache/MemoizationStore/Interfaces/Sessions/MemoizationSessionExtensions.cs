// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
#if NET10_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif
using System.Threading;
using System.Threading.Tasks;
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
        /// <remarks>
        /// .NET 10 promoted <see cref="System.Linq.AsyncEnumerable"/> into the BCL, which collides with
        /// the System.Linq.Async NuGet package's same-named type (CS0433). The package is therefore
        /// skipped for net10 builds (see Public\Sdk\SelfHost\BuildXL\BuildXLSdk.Packages.dsc), so the
        /// AsyncEnumerable.Create / AsyncEnumerator.Create factories from System.Linq.Async are not
        /// available under NET10_0_OR_GREATER. The C#-native async IAsyncEnumerable +
        /// `yield return` form used in the NET10_0_OR_GREATER branch is functionally equivalent;
        /// `[EnumeratorCancellation]` ensures the consumer's cancellation token reaches the inner
        /// enumeration the same way the original `AsyncEnumerable.Create(token => ...)` plumbed it.
        /// </remarks>
#if NET10_0_OR_GREATER
        private static async IAsyncEnumerable<T> StopAfter<T>(
            this IAsyncEnumerable<T> enumerable,
            Func<T, bool> predicate,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // GetAsyncEnumerator(token) binds the cancellation token to the enumerator, so the
            // BCL's IAsyncEnumerator<T>.MoveNextAsync() — which (unlike System.Linq.Async's extension
            // method) takes no arguments — observes cancellation via that bound token.
            await using var enumerator = enumerable.GetAsyncEnumerator(cancellationToken);
            bool stop = false;
            while (!stop && await enumerator.MoveNextAsync())
            {
                if (!predicate(enumerator.Current))
                {
                    stop = true;
                }

                yield return enumerator.Current;
            }
        }
#else
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
#endif

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

