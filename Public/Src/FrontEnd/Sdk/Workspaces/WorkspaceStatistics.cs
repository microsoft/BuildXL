// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Workspaces
{
    /// <summary>
    /// Interface for capturing statistics for different phases of workspace computation.
    /// </summary>
    public interface IWorkspaceStatistics
    {
        /// <summary>Counter for parsing individual specs.</summary>
        Counter SpecParsing { get; }

        /// <summary>Counter for binding individual specs.</summary>
        Counter SpecBinding { get; }

        /// <summary>Counter for type checking individual specs.</summary>
        Counter SpecTypeChecking { get; }

        /// <summary>Counter for computing binding fingerprint for individual specs.</summary>
        Counter SpecComputeFingerprint { get; }

        /// <summary>Counter for converting individual specs.</summary>
        Counter SpecConversion { get; }

        /// <summary>Counter for evaluating individual specs.</summary>
        Counter SpecEvaluation { get; }

        /// <summary>Counter for end-to-end parsing (<see cref="Counter.Start"/> should be called exactly once on it).</summary>
        Counter EndToEndParsing { get; }

        /// <summary>Counter for end-to-end binding (<see cref="Counter.Start"/> should be called at most once on it, when binding is done separately from parsing).</summary>
        Counter EndToEndBinding { get; }

        /// <summary>Counter for end-to-end type checking (<see cref="Counter.Start"/> should be called exactly once on it).</summary>
        Counter EndToEndTypeChecking { get; }

        /// <summary>
        /// If front-end incrementality is enabled, this property stores how long it took to save front-end snapshot.
        /// </summary>
        TimeSpan? FrontEndSnapshotSavingDuration { get; set; }

        /// <summary>
        /// If front-end incrementality is enabled, this property stores how long it took to load front-end snapshot
        /// </summary>
        TimeSpan? FrontEndSnapshotLoadingDuration { get; set; }

        /// <summary>
        /// Counter for the number of specs whose public facade was successfully retrieved and used
        /// </summary>
        Counter PublicFacadeHits { get; }

        /// <summary>
        /// Counter for the number of specs whose serialized AST was successfully retrieved and used
        /// </summary>
        Counter SerializedAstHits { get; }

        /// <summary>
        /// Count for the number of public facades that were attempted to generate and failed (because of visibility reasons)
        /// </summary>
        Counter PublicFacadeGenerationFailures { get; }

        /// <summary>
        /// Counter for the number of public facades that were saved for a future build to consume
        /// </summary>
        Counter PublicFacadeSaves { get; }

        /// <summary>
        /// Counter for the number of serialized AST that were saved for a future build to consume
        /// </summary>
        Counter AstSerializationSaves { get; }

        /// <summary>
        /// Aggregate size of the serialized ast.
        /// </summary>
        CounterValue AstSerializationBlobSize { get; }

        /// <summary>
        /// Aggregate size of the serialized public facades.
        /// </summary>
        CounterValue PublicFacadeSerializationBlobSize { get; }

        /// <summary>
        /// Total number of AST nodes across all parsed source files.
        /// </summary>
        WeightedCounter SourceFileNodes { get; }

        /// <summary>
        /// Total number of identifiers across all parsed source files.
        /// </summary>
        WeightedCounter SourceFileIdentifiers { get; }

        /// <summary>
        /// Total number of lines across all source files.
        /// </summary>
        WeightedCounter SourceFileLines { get; }

        /// <summary>
        /// Total number of lines across all source files.
        /// </summary>
        WeightedCounter SourceFileChars { get; }

        /// <summary>
        /// Total number of symbols across all parsed source files.
        /// </summary>
        WeightedCounter SourceFileSymbols { get; }
    }

    /// <summary>
    /// Counter that captures a value.
    /// </summary>
    public sealed class CounterValue
    {
        private long m_value;

        /// <summary>
        /// Current value of the counter.
        /// </summary>
        public long Value => m_value;

        /// <summary>
        /// Adds a given value to this counter in an atomic fashion.
        /// </summary>
        public void AddAtomic(long increment)
        {
            Interlocked.Add(ref m_value, increment);
        }
    }

    /// <summary>
    /// Used for counting invocations (of a particular operation) and aggregating their individual weights.
    /// </summary>
    public class WeightedCounter
    {
        private const int TopListMaxLength = 10;
        internal readonly ConcurrentBoundedSortedCollection<long, string> SortedList = new ConcurrentBoundedSortedCollection<long, string>(TopListMaxLength);
        private int m_count;
        private long m_aggregateWeight;

        /// <summary>
        /// Returns the number of times <see cref="Increment(long, string)"/> has been called on this counter.
        /// </summary>
        public int Count => Volatile.Read(ref m_count);

        /// <summary>
        /// Returns the sum of all weights passed to all previous invocations of <see cref="Increment(long, string)"/> on this counter.
        /// </summary>
        public long AggregateWeight => Volatile.Read(ref m_aggregateWeight);

        /// <summary>
        /// Increments <see cref="Count"/>, adds <paramref name="weight"/> to <see cref="AggregateWeight"/>,
        /// and (optionally) associates the weight with <paramref name="path"/>.
        /// </summary>
        public int Increment(long weight, string path = null)
        {
            var result = Interlocked.Increment(ref m_count);
            Interlocked.Add(ref m_aggregateWeight, weight);

            if (!string.IsNullOrEmpty(path))
            {
                SortedList.TryAdd(weight, path);
            }

            return result;
        }

        /// <summary>
        /// Adds a <paramref name="value"/> to the <see cref="Count"/> property.
        /// </summary>
        public int Add(int value)
        {
            return Interlocked.Add(ref m_count, value);
        }

        /// <summary>
        /// Increments the <see cref="Count"/>.
        /// </summary>
        public int Increment() => Increment(weight: 0);

        /// <summary>
        /// Returns a user-friendly description of the most heavy-weight elements.
        /// </summary>
        public string RenderMostHeavyWeight() => CollapseDictionaryTimeLogs(SortedList, (weight) => I($"{weight}"));

        /// <summary>
        /// Renders all alements found in <paramref name="sortedList"/>.
        /// Elements are sorted by weight (in descending order).
        /// Each element is rendered on a separate line using the following format string: $"[{weight}] {path}.
        /// The 'weigth' fragment is padded so that all 'path's are aligned on the left.
        /// </summary>
        protected static string CollapseDictionaryTimeLogs(ConcurrentBoundedSortedCollection<long, string> sortedList, Func<long, string> weightRenderer)
        {
            var maxRenderedWeightLength = sortedList.Any()
                ? sortedList.Max(kvp => weightRenderer(kvp.Key).Length)
                : 0;

            StringBuilder stringBuilder = new StringBuilder();
            foreach (KeyValuePair<long, string> pair in sortedList.Reverse())
            {
                var renderedWeight = weightRenderer(pair.Key);
                var paddedDurationStr = renderedWeight.PadLeft(maxRenderedWeightLength);
                stringBuilder.Append(I($"{Environment.NewLine}  [{paddedDurationStr}] {pair.Value}"));
            }

            return stringBuilder.ToString();
        }
    }

    /// <summary>
    /// Used for counting invocations (of a particular operation) and aggregating their individual durations.
    /// </summary>
    public class Counter : WeightedCounter
    {
        /// <summary>
        /// Returns aggregate duration.
        /// </summary>
        public TimeSpan AggregateDuration => TimeSpan.FromTicks(AggregateWeight);

        /// <summary>
        /// Increments <see cref="WeightedCounter.Count"/> and adds <paramref name="elapsed"/> to <see cref="AggregateDuration"/>.
        /// </summary>
        public void Increment(TimeSpan elapsed, string path = null) => Increment(elapsed.Ticks, path);

        /// <summary>
        /// Returns a disposable object which when disposed increments the <see cref="WeightedCounter.Count"/>
        /// of this counter and adds elapsed time to its <see cref="AggregateDuration"/>.
        /// </summary>
        public virtual Stopwatch Start(string path = null)
        {
            return new Stopwatch(this, path);
        }

        /// <summary>
        /// Returns a string of the most time-consuming elements.
        /// </summary>
        public string RenderSlowest => CollapseDictionaryTimeLogs(SortedList, RenderTicks);

        private static string RenderTicks(long ticks) => I($"{(long)TimeSpan.FromTicks(ticks).TotalMilliseconds}ms");

        /// <summary>
        /// Disposable struct for measuring <see cref="AggregateDuration"/> of a given Counter.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals", Justification = "Not comparable")]
        public struct Stopwatch : IDisposable
        {
            private static readonly ObjectPool<System.Diagnostics.Stopwatch> s_stopWatchPool = new ObjectPool<System.Diagnostics.Stopwatch>(
                () => new System.Diagnostics.Stopwatch(),
                sw => { sw.Reset(); return sw; });

            private readonly PooledObjectWrapper<System.Diagnostics.Stopwatch> m_stopwatch;
            private readonly Counter m_counter;
            private readonly string m_path;

            internal Stopwatch(Counter counter, string path = null)
            {
                m_stopwatch = s_stopWatchPool.GetInstance();
                m_stopwatch.Instance.Start();
                m_counter = counter;
                m_path = path;
            }

            /// <summary>
            /// Increments the corresponding counter (<seealso cref="Increment"/>).
            /// </summary>
            public void Dispose()
            {
                TimeSpan elapsed = m_stopwatch.Instance.Elapsed;
                m_counter.Increment(elapsed, m_path);

                m_stopwatch.Dispose();
            }

            /// <nodoc />
            public TimeSpan Elapsed => m_stopwatch.Instance.Elapsed;
        }
    }

    /// <summary>
    /// Certain counters are expected to be <code>0</code>.  Hence, the <see cref="Start"/> method captures the current
    /// invocation trace, which helps create an actionable error message and debug why the counter was incremented.
    /// </summary>
    public sealed class CounterWithRootCause : Counter
    {
        /// <summary>
        /// The first invocation stack trace that caused this counter to be incremented.
        /// </summary>
        public StackTrace FileReloadStackTrace { get; private set; }

        /// <summary>
        /// In addition to the base class implementation saves the current <see cref="StackTrace"/>
        /// the first time this method gets called.
        /// </summary>
        public override Stopwatch Start(string path = null)
        {
            if (FileReloadStackTrace == null)
            {
                FileReloadStackTrace = new StackTrace();
            }

            return base.Start(path);
        }
    }

    /// <summary>
    /// Captures statistic information about different stages of the DScript frontend pipeline.
    /// </summary>
    public class WorkspaceStatistics : IWorkspaceStatistics
    {
        /// <inheritdoc/>
        public Counter SpecParsing { get; } = new Counter();

        /// <inheritdoc/>
        public Counter SpecBinding { get; } = new Counter();

        /// <inheritdoc/>
        public Counter SpecTypeChecking { get; } = new Counter();

        /// <inheritdoc/>
        public Counter SpecComputeFingerprint { get; } = new Counter();

        /// <inheritdoc/>
        public Counter SpecConversion { get; } = new Counter();

        /// <inheritdoc/>
        public Counter SpecEvaluation { get; } = new Counter();

        /// <inheritdoc/>
        public Counter EndToEndParsing { get; } = new Counter();

        /// <inheritdoc/>
        public Counter EndToEndBinding { get; } = new Counter();

        /// <inheritdoc/>
        public Counter EndToEndTypeChecking { get; } = new Counter();

        /// <inheritdoc/>
        public TimeSpan? FrontEndSnapshotSavingDuration { get; set; }

        /// <inheritdoc />
        public TimeSpan? FrontEndSnapshotLoadingDuration { get; set; }

        /// <inheritdoc />
        public Counter PublicFacadeHits { get; } = new Counter();

        /// <inheritdoc />
        public Counter SerializedAstHits { get; } = new Counter();

        /// <inheritdoc/>
        public Counter PublicFacadeGenerationFailures { get; } = new Counter();

        /// <inheritdoc/>
        public Counter PublicFacadeSaves { get; } = new Counter();

        /// <inheritdoc/>
        public Counter AstSerializationSaves { get; } = new Counter();

        /// <inheritdoc/>
        public CounterValue AstSerializationBlobSize { get; } = new CounterValue();

        /// <inheritdoc/>
        public CounterValue PublicFacadeSerializationBlobSize { get; } = new CounterValue();

        /// <inheritdoc/>
        public WeightedCounter SourceFileSymbols { get; } = new WeightedCounter();

        /// <inheritdoc/>
        public WeightedCounter SourceFileNodes { get; } = new WeightedCounter();

        /// <inheritdoc/>
        public WeightedCounter SourceFileIdentifiers { get; } = new WeightedCounter();

        /// <inheritdoc/>
        public WeightedCounter SourceFileLines { get; } = new WeightedCounter();

        /// <inheritdoc/>
        public WeightedCounter SourceFileChars { get; } = new WeightedCounter();
    }
}
