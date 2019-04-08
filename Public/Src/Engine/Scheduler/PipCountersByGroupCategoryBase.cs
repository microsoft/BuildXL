// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Base class for grouping pip counters by some category.
    /// </summary>
    public abstract class PipCountersByGroupCategoryBase<Category> : IPipCountersByGroupCategory
    {
        /// <summary>
        /// Logging context.
        /// </summary>
        protected readonly LoggingContext LoggingContext;

        /// <summary>
        /// Mappings from categories to <see cref="CounterCollection{PipCountersByGroup}"/>.
        /// </summary>
        protected readonly ConcurrentDictionary<Category, CounterCollection<PipCountersByGroup>> CountersByGroup = new ConcurrentDictionary<Category, CounterCollection<PipCountersByGroup>>();

        /// <summary>
        /// Creates an instance of <see cref="PipCountersByGroupCategoryBase{Category}"/>.
        /// </summary>
        protected PipCountersByGroupCategoryBase(LoggingContext loggingContext)
        {
            Contract.Requires(loggingContext != null);
            LoggingContext = loggingContext;
        }

        /// <inheritdoc />
        public virtual void AddToCounter(Process process, PipCountersByGroup counter, long value)
        {
            foreach (var pipCounters in GetPipCounters(process))
            {
                pipCounters.AddToCounter(counter, value);
            }
        }

        /// <inheritdoc />
        public virtual void AddToCounter(Process process, PipCountersByGroup counter, TimeSpan timeSpan)
        {
            foreach (var pipCounters in GetPipCounters(process))
            {
                pipCounters.AddToCounter(counter, timeSpan);
            }
        }

        /// <inheritdoc />
        public virtual void AddToCounters(Process process, (PipCountersByGroup counter, long value)[] countersAndValues, (PipCountersByGroup counter, TimeSpan timeSpan)[] countersAndTimeSpans)
        {
            foreach (var pipCounters in GetPipCounters(process))
            {
                foreach (var (counter, value) in countersAndValues)
                {
                    pipCounters.AddToCounter(counter, value);
                }

                foreach (var (counter, timeSpan) in countersAndTimeSpans)
                {
                    pipCounters.AddToCounter(counter, timeSpan);
                }
            }
        }

        /// <inheritdoc />
        public virtual long GetCounterValue(Process process, PipCountersByGroup counter)
        {
            long result = 0;

            foreach (var pipCounters in GetPipCounters(process))
            {
                result += pipCounters.GetCounterValue(counter);
            }

            return result;
        }

        /// <inheritdoc />
        public virtual TimeSpan GetElapsedTime(Process process, PipCountersByGroup counter)
        {
            TimeSpan result = TimeSpan.Zero;

            foreach (var pipCounters in GetPipCounters(process))
            {
                result += pipCounters.GetElapsedTime(counter);
            }

            return result;
        }

        /// <inheritdoc />
        public virtual void IncrementCounter(Process process, PipCountersByGroup counter)
        {
            foreach (var pipCounters in GetPipCounters(process))
            {
                pipCounters.IncrementCounter(counter);
            }
        }

        /// <inheritdoc />
        public virtual void IncrementCounters(Process process, PipCountersByGroup[] counters)
        {
            foreach (var pipCounters in GetPipCounters(process))
            {
                foreach (var counter in counters)
                {
                    pipCounters.IncrementCounter(counter);
                }
            }
        }

        /// <inheritdoc />
        public virtual Dictionary<string, long> ToDictionarry()
        {
            var pipCounters = new Dictionary<string, long>();
            foreach (var categoryAndPipCounter in CountersByGroup)
            {
                foreach (var counter in categoryAndPipCounter.Value.AsStatistics(CategoryToString(categoryAndPipCounter.Key)))
                {
                    pipCounters.Add(counter.Key, counter.Value);
                }

            }
            return pipCounters;
        }

        /// <summary>
        /// Translates category to its string representation.
        /// </summary>
        protected abstract string CategoryToString(Category category);

        private IEnumerable<CounterCollection<PipCountersByGroup>> GetPipCounters(Process process)
        {
            foreach (var category in GetCategories(process))
            {
                var counter = CountersByGroup.GetOrAdd(category, c => new CounterCollection<PipCountersByGroup>());
                yield return counter;
            }
        }

        /// <summary>
        /// Extracts categories from a <see cref="Process"/>.
        /// </summary>
        protected abstract IEnumerable<Category> GetCategories(Process process);
    }
}
