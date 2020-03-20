// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using Microsoft.Practices.TransientFaultHandling;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    internal static class RetryPolicyExtensions
    {
        public static async Task ExecuteAsync(
            this RetryPolicy policy,
            Context context,
            Func<Task> func,
            CancellationToken token,
            bool traceFailure,
            [CallerMemberName] string? caller = null)
        {
            if (!traceFailure)
            {
                await policy.ExecuteAsync(func, token);
                return;
            }

            await policy.ExecuteAsync(
                context,
                async () =>
                {
                    await func();
                    return true;
                },
                token,
                traceFailure: true,
                caller);
        }

        public static async Task<T> ExecuteAsync<T>(
            this RetryPolicy policy,
            Context context,
            Func<Task<T>> func,
            CancellationToken token,
            bool traceFailure,
            string? databaseName,
            [CallerMemberName] string? caller = null)
        {
            if (!traceFailure)
            {
                return await policy.ExecuteAsync(func, token);
            }

            try
            {
                return await func();
            }
            catch (Exception e)
            {
                string databaseText = string.IsNullOrEmpty(databaseName) ? string.Empty : $" against '{databaseName}'";
                // Intentionally tracing only message, because if the issue is transient, its not very important to see the full stack trace (we never seen them before)
                // and if the issue is not transient, then the client of this class is responsible for properly tracing the full stack trace.
                context.Debug($"Redis operation '{caller}'{databaseText} failed: {e.Message}.");
                ExceptionDispatchInfo.Capture(e).Throw();
                throw; // unreachable
            }
        }
    }
}
