// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace TypeScript.Net.Extensions
{
    internal static class ConcurrencyUtilities
    {
        /// <summary>
        /// Gets or sets the value from the object in a thread-safe manner.
        /// </summary>
        /// <remarks>
        /// This is function is never used so far, but can be used in the future if race condition will keep happening in the checker.
        ///
        /// If two threads will call this function on the same instance when the property is null, the function can call a factory method
        /// more than once but the return value would be the same for all threads.
        ///
        /// </remarks>
        public static TResult GetOrSetAtomic<TItem, TResult, TState>(
            TItem item,
            TState state,
            Func<TItem, TResult> getter,
            Action<TItem, TResult> setter,
            Func<TItem, TState, TResult> factory) where TResult : class
        {
            var candidate = getter(item);

            if (candidate != null)
            {
                return candidate;
            }

            var factoryResult = factory(item, state);

            lock (item)
            {
                candidate = getter(item);
                if (candidate != null)
                {
                    return candidate;
                }

                setter(item, factoryResult);
                return factoryResult;
            }
        }
    }
}
