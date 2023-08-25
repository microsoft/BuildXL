// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;

namespace BuildXL.Cache.BlobLifetimeManager.Library
{
    /// <summary>
    /// Provides a set of locks and a method that will aquire all of them and release them only until a provided action is complete.
    /// </summary>
    internal class LockSetAcquirer<T>
    {
        public IReadOnlyList<Lock<T>> Locks { get; private init; }

        public LockSetAcquirer(int lockCount) => Locks = Enumerable.Range(0, lockCount).Select(_ => new Lock<T>()).ToArray();

        public async Task<T> UseAsync(Func<IReadOnlyList<T?>, Task<T>> action)
        {
            var actionCompletionSource = new TaskCompletionSource<T?>();
            T? actionResult = default;
            Task<Result<T?>>[]? lockUntilDoneTasks = null;
            try
            {
                var lockAcquiredCompletionSources = Locks.Select(_ => new TaskCompletionSource<Result<T?>?>()).ToArray();

                lockUntilDoneTasks = Locks.Select((l, i) => l.UseAsync(lastOperationResult =>
                {
                    // Signal that we've acquired a lock.
                    lockAcquiredCompletionSources[i].SetResult(lastOperationResult);

                    // Wait for the action to be completed before releasing the lock.
                    return actionCompletionSource.Task;
                })).ToArray();

                // Wait until we've aquired all locks and have all the latest results.
                var latestResults = await TaskUtilities.SafeWhenAll(lockAcquiredCompletionSources.Select(tcs => tcs.Task));

                if (!latestResults.Any(r => r?.Succeeded == false))
                {
                    var result = BoolResult.Success;
                    foreach (var r in latestResults)
                    {
                        result &= (r ?? BoolResult.Success);
                    }

                    // Should always throw.
                    result.ThrowIfFailure();
                }

                actionResult = await action(latestResults.Where(r => r is not null).Select(r => r!.Value).ToList());
                return actionResult;
            }
            finally
            {
                // Release all locks
                actionCompletionSource.SetResult(actionResult);

                // Observe any errors.
                await TaskUtilities.SafeWhenAll(lockUntilDoneTasks ?? Array.Empty<Task<Result<T?>>>());
            }
        }
    }

    /// <summary>
    /// Provides an exclusive lock that stores the result of the last operation performed while holding the lock.
    /// </summary>
    internal class Lock<T>
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(initialCount: 1);

        private Result<T?>? _state;

        public async Task<Result<T?>> UseAsync(Func<Result<T?>?, Task<T?>> operation)
        {
            await _semaphore.AcquireAsync();
            try
            {
                return _state = new Result<T?>(await operation(_state));
            }
            catch (Exception e)
            {
                return _state = new Result<T?>(e);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
