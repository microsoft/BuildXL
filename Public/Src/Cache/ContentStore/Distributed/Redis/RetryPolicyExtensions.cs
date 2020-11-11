// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Utils;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Set of extension methods for <see cref="IRetryPolicy"/>.
    /// </summary>
    public static class RetryPolicyExtensions
    {
        /// <summary>
        /// Execute a given <paramref name="func"/> func and trace transient failures.
        /// </summary>
        public static async Task ExecuteAsync(
            this IRetryPolicy policy,
            Context context,
            Func<Task> func,
            CancellationToken token,
            string? databaseName,
            [CallerMemberName] string? caller = null)
        {
            await policy.ExecuteAsync(
                context,
                async () =>
                {
                    await func();
                    return true;
                },
                token,
                databaseName: databaseName,
                caller);
        }

        /// <summary>
        /// Execute a given <paramref name="func"/> func and trace transient failures.
        /// </summary>
        public static async Task<T> ExecuteAsync<T>(
            this IRetryPolicy policy,
            Context context,
            Func<Task<T>> func,
            CancellationToken token,
            string? databaseName,
            [CallerMemberName] string? caller = null)
        {
            int attempt = 0;
            Func<Task<T>> outerFunc = async () =>
            {
                string databaseText = string.IsNullOrEmpty(databaseName) ? string.Empty : $" against '{databaseName}'";
                try
                {
                    attempt++;

                    var result = await func();
                    return result;
                }
                catch (Exception e)
                {
                    // Intentionally tracing only message, because if the issue is transient, its not very important to see the full stack trace (we never seen them before)
                    // and if the issue is not transient, then the client of this class is responsible for properly tracing the full stack trace.
                    context.Debug($"RetryPolicy.ExecuteAsync: attempt #{attempt}, Redis operation '{caller}'{databaseText} failed with: {e.Message}.");
                    ExceptionDispatchInfo.Capture(e).Throw();
                    throw; // unreachable
                }
            };

            return await policy.ExecuteAsync(outerFunc, token);
        }
    }
}
