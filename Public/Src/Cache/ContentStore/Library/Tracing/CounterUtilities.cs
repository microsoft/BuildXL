// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    /// Helper class for converting from <see cref="CounterCollection{TEnum}"/> and <see cref="CounterSet"/>
    /// </summary>
    public static class CounterUtilities
    {
        /// <nodoc />
        public static CounterSet ToCounterSet<TEnum>(this CounterCollection<TEnum> counters)
            where TEnum : struct
        {
            CounterSet counterSet = new CounterSet();
            foreach (var counterEnum in EnumTraits<TEnum>.EnumerateValues())
            {
                var counter = counters[counterEnum];
                var counterName = counterEnum.ToString();

                counterSet.Add($"{counterName}.Count", (long)counter.Value, counter.Name);

                if (counter.IsStopwatch)
                {
                    counterSet.Add($"{counterName}.AverageMs", counter.Value != 0 ? (long)counter.Duration.TotalMilliseconds / counter.Value : 0);
                    counterSet.Add($"{counterName}.DurationMs", (long)counter.Duration.TotalMilliseconds);
                }
            }

            return counterSet;
        }

        private static readonly ConcurrentDictionary<Type, Func<CounterCollection, CounterSet>> CachedRelfectionMethods = new ConcurrentDictionary<Type, Func<CounterCollection, CounterSet>>();
        private static MethodInfo CachedGenericMethodDefiinition = null;

        /// <nodoc />
        public static CounterSet ToCounterSet(this CounterCollection counters, Type enumType)
        {
            // We cache the method since using Reflection is an expensive operation.
            var method = CachedRelfectionMethods.GetOrAdd(enumType, theEnumType =>
            {
                CachedGenericMethodDefiinition = CachedGenericMethodDefiinition ?? typeof(CounterUtilities).GetMethods().Single(method => method.Name == nameof(ToCounterSet) && method.IsGenericMethodDefinition);
                var genericMethod = CachedGenericMethodDefiinition.MakeGenericMethod(theEnumType);
                return counterCollection => (CounterSet)genericMethod.Invoke(null, new object[] { counterCollection });
            });

            return method(counters);
        }
    }
}
