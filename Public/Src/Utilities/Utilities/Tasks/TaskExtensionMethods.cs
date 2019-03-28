// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Tasks
{
    /// <summary>
    /// Useful extension methods for tasks
    /// </summary>
    public static class TaskExtensionMethods
    {
        /// <summary>
        /// Repeatedly await a list of tasks, until their values don't change
        /// </summary>
        /// <remarks>
        /// The default value of <code>T</code> is assumed to be the initial value.
        /// </remarks>
        public static async Task<T[]> WhenStable<T>(this Func<Task<T>>[] taskProducers, IEqualityComparer<T> comparer)
        {
            Contract.Requires(comparer != null);
            Contract.Requires(taskProducers != null);
            Contract.RequiresForAll(taskProducers, producer => producer != null);

            T[] lastValues;
            var newValues = new T[taskProducers.Length];
            do
            {
                lastValues = newValues;
                newValues = new T[taskProducers.Length];
                for (int i = 0; i < taskProducers.Length; i++)
                {
                    newValues[i] = await taskProducers[i]();
                }
            }
            while (!lastValues.SequenceEqual(newValues, comparer));
            return newValues;
        }

        /// <summary>
        /// Repeatedly await a list of tasks, until their values don't change
        /// </summary>
        /// <remarks>
        /// The default value of <code>T</code> is assumed to be the initial value.
        /// </remarks>
        public static Task<T[]> WhenStable<T>(this Func<Task<T>>[] taskProducers)
        {
            Contract.Requires(taskProducers != null);

            return WhenStable(taskProducers, EqualityComparer<T>.Default);
        }
    }
}
