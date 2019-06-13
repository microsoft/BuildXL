// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Cache-aware integer counters. Note that counters should be monotonic, since
    /// counter additions may be re-ordered from the perspective of <see cref="GetCounterValueInternal" />.
    /// </summary>
    /// <remarks>
    /// A counter collection aggregates multiple counters such that their memory layout
    /// is compact while also cache aware. One could implement a counter naively via a
    /// single <c>ulong</c> and having all threads update it with e.g. <c>Interlocked.Increment</c>.
    /// With many threads, this is cache antagonistic since the cache line containing the <c>ulong</c>
    /// will be contended by many cores; each write must invalidate that line (the writing core
    /// needs it exclusively) so the line ownership ping-pongs among cores.
    /// One could instead shard the counter such that N threads use N adjacent cache lines; that
    /// addresses the cache contention problem (while making reads - assumed rare - more expensive).
    /// However, that approach is not space efficient since a single counter then needs C * N bytes
    /// of space (for a cache line size C, often 64 bytes, and N threads).
    /// This implementation interlaces multiple counters to limit the space waste.
    /// - Let I indicate a numeric counter ID, monotonic from zero.
    /// - Each counter I has N shards, each shard on a different cache line.
    /// - Each cache line contains shards for C / sizeof(ulong) counters.
    /// - All of the cache lines for a single core are adjacent (maybe nice for prefetching).
    /// </remarks>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public class CounterCollection
    {
        private const int AssumedCacheLineSize = 64;
        private const int ValuesPerCacheLine = AssumedCacheLineSize / sizeof(ulong);

        /// <summary>
        /// Assumed number of logical processors ('N' in remarks above).
        /// </summary>
        /// <remarks>
        /// Right now we assume 64, which is the number of processor IDs per 'processor group'
        /// in NT. GetCurrentProcessorNumber is cheap but returns a value 0 - 63 without indicating
        /// the owning processor group. This is fine, since on a fancy 128 core machine we'd only be
        /// contending each cache line among perhaps two cores if unlucky (rather than 128!).
        /// TODO: This is somewhat wasteful on machines with many fewer than 64 cores.
        /// </remarks>
        private const int AssumedLogicalProcessorCount = 64;

        /// <summary>
        /// Tick frequency (copied from stopwatch).
        /// </summary>
        private static readonly double s_tickFrequency = ((double)TimeSpan.TicksPerSecond) / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>
        /// Array for tracking operation's count
        /// </summary>
        private readonly long[,] m_counters;

        /// <summary>
        /// Array for tracking operation's duration.
        /// </summary>
        private readonly long[,] m_durations;

        /// <summary>
        /// Creates a collection with the specified number of counters. Each counter in <c>[0, numberOfCounters - 1]</c>
        /// may be accessed with <see cref="AddToCounterInternal"/> or <see cref="GetCounterValueInternal"/>.
        /// </summary>
        internal CounterCollection(ushort numberOfCounters)
        {
            Contract.Requires(numberOfCounters > 0);

            // Round up the per-processor 'row' size to cache line size. Since m_counters is row-major,
            // this means that the processor rows are cache-aligned and so processors do not share cache lines.
            int valuesPerProcessor = ((int)numberOfCounters + (ValuesPerCacheLine - 1)) & ~(ValuesPerCacheLine - 1);
            m_counters = new long[AssumedLogicalProcessorCount, valuesPerProcessor];
            m_durations = new long[AssumedLogicalProcessorCount, valuesPerProcessor];
        }

        /// <summary>
        /// Adds to the counter with the given zero-based ID.
        /// </summary>
        internal void AddToCounterInternal(ushort counterId, long add)
        {
            AddToCounter(m_counters, counterId, add);
        }

        /// <summary>
        /// Adds to the counter with the given zero-based ID.
        /// </summary>
        internal void AddToStopwatchInternal(ushort counterId, long add)
        {
            AddToCounter(m_durations, counterId, add);
        }

        /// <summary>
        /// Adds to the counter with the given zero-based ID.
        /// </summary>
        private static void AddToCounter(long[,] counters, ushort counterId, long add)
        {
            if (add == 0)
            {
                return;
            }

            int processorId = 0;

#if FEATURE_CORECLR
            if (OperatingSystemHelper.IsUnixOS)
            {
                processorId = Thread.GetCurrentProcessorId() % AssumedLogicalProcessorCount;
            }
            else
#endif
            {
                processorId = GetCurrentProcessorNumber();
            }

            // GetCurrentProcessorNumber returns the relative ID within the 64-processor 'processor group'.
            Contract.Assume(processorId < AssumedLogicalProcessorCount);

            // Why Interlocked.Add? Maybe we moved to another processor since querying it above.
            // Or maybe there are actually more processors than representable in processorId
            // (multiple processor groups).
            long postAdd = Interlocked.Add(ref counters[processorId, counterId], add);
            if (add > 0 ? (postAdd < add + long.MinValue) : (postAdd > add + long.MaxValue))
            {
                throw new OverflowException("Overflow while incrementing a counter");
            }
        }

        /// <summary>
        /// Get the current value of a counter
        /// </summary>
        internal long GetCounterValueInternal(ushort counterId)
        {
            return GetCounterValue(m_counters, counterId);
        }

        /// <summary>
        /// Get the current value of a counter
        /// </summary>
        internal long GetStopwatchValueInternal(ushort counterId)
        {
            return GetCounterValue(m_durations, counterId);
        }

        private long GetCounterValue(long[,] counters, ushort counterId)
        {
            long sum = 0;
            for (int i = 0; i < AssumedLogicalProcessorCount; i++)
            {
                long rhs = Volatile.Read(ref counters[i, counterId]);
                sum = checked(sum + rhs);
            }

            return sum;
        }

        /// <summary>
        /// Converts stopwatch ticks to a TimeSpan
        /// </summary>
        public static TimeSpan StopwatchTicksToTimeSpan(long stopwatchTicks)
        {
            return new TimeSpan((long)((double)stopwatchTicks * s_tickFrequency));
        }

        /// <summary>
        /// Converts a TimeSpan to stopwarch ticks
        /// </summary>
        public static long TimeSpanToStopwatchTicks(TimeSpan timespan)
        {
            return (long)(timespan.Ticks / s_tickFrequency);
        }

        /// <summary>
        /// Stopwatch context of a counter. Adds to the counter when disposed.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public readonly struct Stopwatch : IDisposable
        {
            private readonly CounterCollection m_collection;
            private readonly ushort m_id;
            private readonly long m_startTimestamp;

            internal Stopwatch(CounterCollection collection, ushort id)
            {
                Contract.Requires(collection != null);

                m_collection = collection;
                m_id = id;
                m_startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            }

            /// <summary>
            /// Gets the total elapsed time measured by the current instance.
            /// </summary>
            public TimeSpan Elapsed
            {
                get
                {
                    long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - m_startTimestamp;
                    return StopwatchTicksToTimeSpan(elapsed);
                }
            }

            /// <inheritdoc />
            public void Dispose()
            {
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - m_startTimestamp;
                if (elapsed > 0)
                {
                    m_collection.AddToStopwatchInternal(m_id, elapsed);
                }

                m_collection.AddToCounterInternal(m_id, 1);
            }
        }

        /// <summary>
        /// See http://msdn.microsoft.com/en-us/library/windows/desktop/ms683181(v=vs.85).aspx
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = false, ExactSpelling = true)]
        private static extern int GetCurrentProcessorNumber();
    }

    /// <summary>
    /// Counter instance within <see cref="CounterCollection"/>.
    /// </summary>
    public readonly struct Counter
    {
        private readonly CounterCollection m_collection;
        private readonly ushort m_id;
        private readonly CounterType m_counterType;

        /// <nodoc />
        internal Counter(CounterCollection collection, ushort id, CounterType counterType, string name)
        {
            m_collection = collection;
            m_id = id;
            m_counterType = counterType;
            Name = name;
        }

        /// <summary>
        /// Increments a value of a current counter by 1.
        /// </summary>
        public void Increment()
        {
            m_collection.AddToCounterInternal(m_id, 1);
        }

        /// <summary>
        /// Decrements a value of a current counter by 1.
        /// </summary>
        public void Decrement()
        {
            m_collection.AddToCounterInternal(m_id, -1);
        }

        /// <summary>
        /// Increments a value of a current counter by <paramref name="value"/>.
        /// </summary>
        public void Add(long value)
        {
            m_collection.AddToCounterInternal(m_id, value);
        }

        /// <summary>
        /// Creates a stopwatch for a current counter, which will add an elapsed timespan to the counter when disposed.
        /// </summary>
        public CounterCollection.Stopwatch Start()
        {
            return new CounterCollection.Stopwatch(m_collection, m_id);
        }

        /// <summary>
        /// Returns a current value for a current counter.
        /// </summary>
        public long Value => m_collection.GetCounterValueInternal(m_id);

        /// <summary>
        /// Returns true if a current counter is a stopwatch counter.
        /// </summary>
        public bool IsStopwatch => m_counterType == CounterType.Stopwatch;

        /// <summary>
        /// Returns a current duration for a current counter.
        /// </summary>
        public TimeSpan Duration => CounterCollection.StopwatchTicksToTimeSpan(m_collection.GetStopwatchValueInternal(m_id));
        
        /// <summary>
        /// Returns the name for a counter.
        /// </summary>
        public string Name { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            string optionalDuration = IsStopwatch ? (" " + Duration.ToString(@"hh\:mm\:ss\.fff")) : string.Empty;
            return $"[{Value.ToString().PadLeft(8, ' ')}{optionalDuration}";
        }
    }

    /// <summary>
    /// <see cref="CounterCollection"/> with counters named according to an enum.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
    public sealed class CounterCollection<TEnum> : CounterCollection
        where TEnum : struct
    {
        private static readonly ulong s_counterIdOffset = EnumTraits<TEnum>.MinValue;
        private static readonly CounterType[] s_counterTypes;
        private static readonly string[] s_counterNames;

        static CounterCollection()
        {
            ulong min = EnumTraits<TEnum>.MinValue;
            ulong max = EnumTraits<TEnum>.MaxValue;
            Contract.Assume(max >= min);

            ushort numValues = checked((ushort)(max - min + 1));
            s_counterTypes = new CounterType[numValues];
            s_counterNames = new string[numValues];

            foreach (FieldInfo field in typeof(TEnum).GetFields())
            {
                if (field.IsSpecialName)
                {
                    continue;
                }

                Contract.Assume(field.FieldType == typeof(TEnum));

                var attribute = field.GetCustomAttribute(typeof(CounterTypeAttribute)) as CounterTypeAttribute;
                s_counterTypes[GetCounterIndex((TEnum)field.GetValue(null))] = attribute?.CounterType ?? CounterType.Numeric;
                s_counterNames[GetCounterIndex((TEnum)field.GetValue(null))] = attribute?.CounterName;
            }
        }

        /// <summary>
        /// Creates a collection with a counter for every value of <typeparamref name="TEnum"/>.
        /// Note that the enum should be dense, since this creates <c>MaxEnumValue - MinEnumValue + 1</c> counters.
        /// </summary>
        public CounterCollection()
            : base((ushort)s_counterTypes.Length)
        {
        }

        /// <summary>
        /// Returns a counter instance for a given <paramref name="counterId"/>.
        /// </summary>
        public Counter this[TEnum counterId]
        {
            get
            {
                ushort counterIndex = GetCounterIndex(counterId);
                return new Counter(this, counterIndex, s_counterTypes[counterIndex], s_counterNames[counterIndex]);
            }
        }

        /// <summary>
        /// Increments a counter with a given enum name.
        /// This call is valid only for counters that are not of type Stopwatch
        /// </summary>
        public void IncrementCounter(TEnum counterId)
        {
            AddToCounter(counterId, 1);
        }

        /// <summary>
        /// Decrements a counter with a given enum name.
        /// This call is valid only for counters that are not of type Stopwatch
        /// </summary>
        public void DecrementCounter(TEnum counterId)
        {
            AddToCounter(counterId, -1);
        }

        /// <summary>
        /// Adds to a counter with a given enum name.
        /// This call is valid only for counters that are not of type Stopwatch
        /// </summary>
        public void AddToCounter(TEnum counterId, long add)
        {
            ushort counterIndex = GetCounterIndex(counterId);
            AddToCounterInternal(counterIndex, add);
        }

        /// <summary>
        /// Adds to a counter with a given enum name.
        /// This call is valid only for counters that are of type Stopwatch
        /// </summary>
        public void AddToCounter(TEnum counterId, TimeSpan add)
        {
            Contract.Requires(IsStopwatch(counterId));
            ushort counterIndex = GetCounterIndex(counterId);
            AddToStopwatchInternal(counterIndex, TimeSpanToStopwatchTicks(add));
        }

        /// <summary>
        /// Get the counter value.
        /// This call is valid only for counters that are not of type Stopwatch
        /// </summary>
        public long GetCounterValue(TEnum counterId)
        {
            ushort counterIndex = GetCounterIndex(counterId);
            return GetCounterValueInternal(counterIndex);
        }

        /// <summary>
        /// Creates a stopwatch for a counter based on counter ID, which will add an elapsed timespan to the counter when disposed.
        /// This call is valid only for counters that are of type Stopwatch
        /// </summary>
        public Stopwatch StartStopwatch(TEnum counterId)
        {
            Contract.Requires(IsStopwatch(counterId));
            ushort counterIndex = GetCounterIndex(counterId);
            return new Stopwatch(this, counterIndex);
        }

        /// <summary>
        /// Retrieves the counter value interpreted as an elapsed time span.
        /// This call is valid only for counters that are of type Stopwatch
        /// </summary>
        public TimeSpan GetElapsedTime(TEnum counterId)
        {
            Contract.Requires(IsStopwatch(counterId));

            ushort counterIndex = GetCounterIndex(counterId);

            return StopwatchTicksToTimeSpan(GetStopwatchValueInternal(counterIndex));
        }

        /// <summary>
        /// Converts <see cref="CounterCollection"/> as statistics in the form of dictionary from statistic names to values.
        /// </summary>
        public Dictionary<string, long> AsStatistics(string namePrefix = null)
        {
            Dictionary<string, long> counters = new Dictionary<string, long>();
            foreach (TEnum counterName in EnumTraits<TEnum>.EnumerateValues())
            {
                bool isStopwatch = IsStopwatch(counterName);

                string nameString = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}{1}{2}",
                    !string.IsNullOrWhiteSpace(namePrefix) ? namePrefix + "." : string.Empty,
                    counterName,
                    isStopwatch ? "Ms" : string.Empty);
                long counterValue = isStopwatch
                    ? (long)GetElapsedTime(counterName).TotalMilliseconds
                    : GetCounterValue(counterName);
                counters.Add(nameString, counterValue);
            }

            return counters;
        }

        /// <summary>
        /// Check if a particular enum represents a stopwatch
        /// </summary>
        public static bool IsStopwatch(TEnum counterId)
        {
            ushort counterIndex = GetCounterIndex(counterId);
            return s_counterTypes[counterIndex] == CounterType.Stopwatch;
        }

        /// <summary>
        /// Gets a counter index given an enum name.
        /// </summary>
        private static ushort GetCounterIndex(TEnum counterId)
        {
            ulong counterIdValue = EnumTraits<TEnum>.ToInteger(counterId);
            Contract.Assume(counterIdValue >= s_counterIdOffset);
            ulong relativeCounterIdValue = counterIdValue - s_counterIdOffset;
            return checked((ushort)relativeCounterIdValue);
        }

        /// <summary>
        /// Appends <paramref name="other"/> into a current collection instance.
        /// </summary>
        public void Append(CounterCollection<TEnum> other)
        {
            foreach (var value in EnumTraits<TEnum>.EnumerateValues())
            {
                if (IsStopwatch(value))
                {
                    AddToCounter(value, other.GetElapsedTime(value));
                }

                AddToCounter(value, other.GetCounterValue(value));
            }
        }

        /// <summary>
        /// Creates a snapshot of the counter collection
        /// </summary>
        public CounterCollection<TEnum> Snapshot()
        {
            var result = new CounterCollection<TEnum>();
            result.Append(this);
            return result;
        }

        /// <summary>
        /// Adds two counters.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2225")]
        [SuppressMessage("Microsoft.Design", "CA1013")]
        public static CounterCollection<TEnum> operator +(CounterCollection<TEnum> x, CounterCollection<TEnum> y)
        {
            var result = new CounterCollection<TEnum>();

            foreach (var value in EnumTraits<TEnum>.EnumerateValues())
            {
                if (IsStopwatch(value))
                {
                    result.AddToCounter(value, x.GetElapsedTime(value) + y.GetElapsedTime(value));
                }

                result.AddToCounter(value, x.GetCounterValue(value) + y.GetCounterValue(value));
            }

            return result;
        }

        private string ToDebuggerDisplay()
        {
            var sb = new StringBuilder();
            foreach (var enumValue in EnumTraits<TEnum>.EnumerateValues())
            {
                var counter = this[enumValue];
                sb.AppendLine($"[{enumValue.ToString().PadLeft(50, ' ')}]: {counter.ToString()}");

            }

            return sb.ToString();
        }
    }
}
