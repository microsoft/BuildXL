// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Threading;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Tracks operations associated with pip execution
    ///
    /// At a high level, operations start from a root operation (via <see cref="OperationContext"/>
    /// returned by <see cref="StartOperation(OperationKind, PipId, PipType, LoggingContext, Action{OperationKind,TimeSpan})"/>). An operation may start be started from the context
    /// either as a nested operation or a new operation thread. Nested operations are awaited by the current
    /// <see cref="OperationThread.ActiveOperation"/> and become the new <see cref="OperationThread.ActiveOperation"/>
    /// for the duration of their scope. A new operation thread represents an operation
    /// started by the current <see cref="OperationThread.ActiveOperation"/> but which is not awaited by the
    /// current operation and becomes the active operation on the new <see cref="OperationThread"/>.
    /// </summary>
    public sealed class OperationTracker : IOperationTracker
    {
        private const int MaxTopOperations = 5;

        /// <summary>
        /// Enables tracing of operations
        /// </summary>
        private static bool s_enableDebugTracing = Environment.GetEnvironmentVariable("BuildXLTraceOperation") == "1";

        /// <summary>
        /// The minimum amount of time for an operation to run before reporting as a max operation
        /// </summary>
        private static readonly TimeSpan s_maxOpsThreshold = TimeSpan.FromMilliseconds(10);

        /// <summary>
        /// The currently active root operations for pips
        /// </summary>
        private readonly ConcurrentBigSet<RootOperation> m_activePipOperations
            = new ConcurrentBigSet<RootOperation>();

        /// <summary>
        /// The root operation pool
        /// </summary>
        private readonly ConcurrentQueue<RootOperation> m_rootOperationPool = new ConcurrentQueue<RootOperation>();

        /// <summary>
        /// All root operations
        /// </summary>
        private readonly ConcurrentQueue<RootOperation> m_allRootOperations = new ConcurrentQueue<RootOperation>();

        /// <summary>
        /// Hierarchical counters for operations
        /// </summary>
        private readonly OperationCounters m_counters;

        /// <summary>
        /// Set of counters with associated operations used when computing max operations
        /// </summary>
        private readonly HashSet<StackCounter> m_countersWithAssociatedOperations = new HashSet<StackCounter>();

        private readonly HashSet<Operation> m_uniqueAssociatedOperationsSet = new HashSet<Operation>();
        private readonly List<CapturedOperationInfo> m_uniqueAssociatedOperationsBuffer = new List<CapturedOperationInfo>();

        private readonly IOperationTrackerHost m_host;
        private readonly EtwOnlyTextLogger m_etwOnlyTextLogger;

        /// <summary>
        /// The time keeper
        /// </summary>
        private readonly Stopwatch m_stopwatch;

        /// <summary>
        /// The operation tracker for the total active time of the <see cref="OperationTracker"/>
        /// </summary>
        private readonly OperationContext m_totalActiveTimeOperationTracker;

        internal OperationContext DefaultOperation => m_totalActiveTimeOperationTracker;

        private readonly object m_counterFileLock = new object();
        private TimeSpan m_lastCounterFileWriteTime = TimeSpan.Zero;

        /// <summary>
        /// Class constructor
        /// </summary>
        public OperationTracker(LoggingContext loggingContext, IOperationTrackerHost host = null)
        {
            m_host = host;
            m_stopwatch = Stopwatch.StartNew();
            m_counters = new OperationCounters();
            m_totalActiveTimeOperationTracker = StartOperation(PipExecutorCounter.OperationTrackerActiveDuration, loggingContext);
            if (EtwOnlyTextLogger.TryGetDefaultGlobalLoggingContext(out var defaultGlobalLoggingContext))
            {
                m_etwOnlyTextLogger = new EtwOnlyTextLogger(defaultGlobalLoggingContext, "stats.prf.json");
            }
        }

        /// <summary>
        /// Starts an operation associated with a given pip
        /// </summary>
        /// <returns>an operation context for the started root operation</returns>
        public OperationContext StartOperation(OperationKind kind, PipId pipId, PipType pipType, LoggingContext loggingContext, Action<OperationKind, TimeSpan> onOperationCompleted = null)
        {
            var root = GetOrCreateRootOperation();
            m_activePipOperations.Add(root);
            root.Initialize(pipId, pipType, kind, onOperationCompleted);
            TraceDebugData(root);
            return new OperationContext(loggingContext, root);
        }

        /// <summary>
        /// Starts an operation
        /// </summary>
        public OperationContext StartOperation(OperationKind kind, LoggingContext loggingContext)
        {
            return StartOperation(kind, PipId.Invalid, PipType.Max, loggingContext);
        }

        /// <summary>
        /// Gets or creates a new root operation for the given pip
        /// </summary>
        private RootOperation GetOrCreateRootOperation()
        {
            RootOperation root;
            if (!m_rootOperationPool.TryDequeue(out root))
            {
                root = new RootOperation(this);
                m_allRootOperations.Enqueue(root);
            }

            return root;
        }

        internal Counter TryGetAggregateCounter(OperationKind kind) => m_counters.TryGetAggregateCounter(kind);

        /// <summary>
        /// Stops the operation tracker, logs counters, and writes performance stats file
        /// </summary>
        public void Stop(
            PipExecutionContext context,
            ILoggingConfiguration loggingConfiguration,
            CounterCollection<PipExecutorCounter> pipExecutorCounters,
            IEnumerable<OperationKind> statisticOperations)
        {
            m_totalActiveTimeOperationTracker.Dispose();

            var pipTypes = EnumTraits<PipType>.EnumerateValues().ToArray();
            foreach (PipExecutorCounter pipExecutorCounter in EnumTraits<PipExecutorCounter>.EnumerateValues())
            {
                OperationKind operation = pipExecutorCounter;
                if (!operation.IsValid)
                {
                    continue;
                }

                if (operation.HasPipTypeSpecialization)
                {
                    // Use values aggregated from pip type specializations where applicable
                    foreach (var pipType in pipTypes)
                    {
                        var pipTypeOperation = operation.GetPipTypeSpecialization(pipType);
                        var operationCounter = m_counters.TryGetAggregateCounter(pipTypeOperation);
                        if (operationCounter != null)
                        {
                            pipExecutorCounters.AddToCounter(pipExecutorCounter, operationCounter.Duration);
                        }
                    }
                }
                else
                {
                    var operationCounter = m_counters.TryGetAggregateCounter(operation);
                    if (operationCounter != null)
                    {
                        pipExecutorCounters.AddToCounter(pipExecutorCounter, operationCounter.Duration);
                    }
                }
            }

            Dictionary<string, long> statistics = new Dictionary<string, long>();
            foreach (var operation in statisticOperations)
            {
                var operationCounter = m_counters.TryGetAggregateCounter(operation);
                if (operationCounter != null)
                {
                    statistics[operation.Name + ".DurationMs"] = (long)operationCounter.Duration.TotalMilliseconds;
                    statistics[operation.Name + ".Occurrences"] = operationCounter.Occurrences;
                }
            }

            BuildXL.Tracing.Logger.Log.BulkStatistic(m_totalActiveTimeOperationTracker, statistics);

            WriteCountersFile(context, loggingConfiguration);
        }

        internal void WriteCountersFile(PipExecutionContext context, ILoggingConfiguration loggingConfiguration, TimeSpan? refreshInterval = null)
        {
            bool includeOutstanding = refreshInterval != null;
            lock (m_counterFileLock)
            {
                if (refreshInterval != null)
                {
                    if (m_stopwatch.Elapsed - m_lastCounterFileWriteTime < refreshInterval.Value)
                    {
                        return;
                    }
                }

                m_lastCounterFileWriteTime = m_stopwatch.Elapsed;

                var performanceStatsJsonPath = loggingConfiguration.StatsLog.ToString(context.PathTable) + "prf.json";

                try
                {
                    using (var writer = new StreamWriter(performanceStatsJsonPath))
                    {
                        StringBuilder builder = new StringBuilder();

                        using (m_counters.ReadCounters())
                        {
                            m_counters.ResetWidths();

                            builder.Append("{ 'operationStacks': [");
                            m_counters.RootCounters.Sort((c1, c2) => -c1.Duration.CompareTo(c2.Duration));
                            PrintCounterList(builder, m_counters.RootCounters);
                            builder.AppendLine();

                            builder.AppendLine("],");

                            builder.AppendLine();
                            builder.AppendLine();
                            builder.AppendLine();

                            builder.Append("'operationStacksWithTopOps': [");

                            PrintCounterList(builder, m_counters.RootCounters, includeAssociatedOperations: true);
                            builder.AppendLine();

                            if (includeOutstanding)
                            {
                                RecomputeOutstandingCounters();
                                m_counters.ResetWidths();
                                m_counters.ComputeWidths(outstandingCounters: true);

                                builder.AppendLine("],");

                                builder.AppendLine();
                                builder.AppendLine();
                                builder.AppendLine();

                                builder.Append("'outstandingStacks': [");
                                m_counters.RootOutstandingCounters.Sort((c1, c2) => -c1.Duration.CompareTo(c2.Duration));
                                PrintCounterList(builder, m_counters.RootOutstandingCounters, includeAssociatedOperations: false);
                                builder.AppendLine("],");

                                builder.AppendLine();
                                builder.AppendLine();
                                builder.AppendLine();

                                builder.Append("'outstandingStacksWithTopOps': [");
                                m_counters.RootOutstandingCounters.Sort((c1, c2) => -c1.Duration.CompareTo(c2.Duration));
                                PrintCounterList(builder, m_counters.RootOutstandingCounters, includeAssociatedOperations: true);
                                builder.AppendLine();
                            }

                            builder.Append("] }");

                            // Replace single quotes with double quotes
                            builder.Replace('\'', '"');
                        }

                        var content = builder.ToString();

                        if (m_etwOnlyTextLogger != null)
                        {
                            using (var reader = new StringReader(content))
                            {
                                string line;
                                while ((line = reader.ReadLine()) != null)
                                {
                                    m_etwOnlyTextLogger.TextLogEtwOnly((int)EventId.StatsPerformanceLog,
                                         refreshInterval == null ? "Performance" : "IncrementalPerformance", line);
                                }
                            }
                        }

                        writer.Write(content);
                    }
                }
                catch (IOException)
                {
                    // IOExceptions during periodic refresh should be ignored. Only
                    // the IOExceptions observed during final write should be propagated out
                    if (refreshInterval == null)
                    {
                        throw;
                    }
                }
            }
        }

        private void RecomputeOutstandingCounters()
        {
            m_countersWithAssociatedOperations.Clear();

            foreach (var outstandingCounter in m_counters.AllOutstandingCounters)
            {
                outstandingCounter.Reset();
            }

            foreach (var rootOperation in m_allRootOperations)
            {
                if (!rootOperation.IsComplete)
                {
                    rootOperation.Counter.OutstandingCounter.Add(rootOperation.Duration);

                    foreach (var operation in rootOperation.CreatedOperations)
                    {
                        if (!operation.IsComplete)
                        {
                            var info = operation.Capture();
                            if (info.HasValue)
                            {
                                var outstandingCounter = operation.Counter.OutstandingCounter;
                                outstandingCounter.Add(info.Value.Duration);

                                if (info.Value.Artifact.IsValid || info.Value.PipId.IsValid)
                                {
                                    outstandingCounter.AssociatedOperations.Add(info.Value);
                                    m_countersWithAssociatedOperations.Add(outstandingCounter);

                                    if (info.Value.Duration > s_maxOpsThreshold)
                                    {
                                        operation.Counter.AssociatedOperations.Add(info.Value);
                                        m_countersWithAssociatedOperations.Add(operation.Counter);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            foreach (var counter in m_countersWithAssociatedOperations)
            {
                // Clear the local buffers for ensuring reported associations are unique.
                // Duplicates can arise if the same operation gets captured twice so we need to remove
                // the duplicates after the sort (removing the lower duration duplicates).
                m_uniqueAssociatedOperationsBuffer.Clear();
                m_uniqueAssociatedOperationsSet.Clear();

                // Only keep the top operations
                counter.SortAssociatedOperations();

                // Add only unique operations to the buffers
                foreach (var associatedOperation in counter.AssociatedOperations)
                {
                    if (m_uniqueAssociatedOperationsSet.Add(associatedOperation.Operation))
                    {
                        m_uniqueAssociatedOperationsBuffer.Add(associatedOperation);
                    }

                    if (m_uniqueAssociatedOperationsBuffer.Count == MaxTopOperations)
                    {
                        break;
                    }
                }

                // Add the contents of the buffer to the associated operations for the counter
                counter.AssociatedOperations.Clear();
                counter.AssociatedOperations.AddRange(m_uniqueAssociatedOperationsBuffer);
            }

            foreach (var outstandingCounter in m_counters.AllOutstandingCounters)
            {
                outstandingCounter.ReattachActive();
            }
        }

        private int PrintCounterList(StringBuilder builder, IReadOnlyList<Counter> counterList, int depth = 0, bool includeAssociatedOperations = false)
        {
            int index = 0;
            foreach (var counter in counterList)
            {
                if (index != 0)
                {
                    builder.Append(",");
                }

                if (counter.Kind == OperationKind.PassThrough)
                {
                    index += PrintCounterList(builder, counter.Children, depth, includeAssociatedOperations);
                }
                else
                {
                    PrintCounter(builder, counter, depth, includeAssociatedOperations);
                    index++;
                }
            }

            return index;
        }

        private void PrintCounter(StringBuilder builder, Counter counter, int depth, bool includeAssociatedOperations)
        {
            builder.AppendLine();

            string name = counter.Name;
            int indentWidth = depth * OperationCounters.SingleIndentWidth;
            string namePadding = string.Empty.PadLeft(m_counters.NameWidth - (name.Length + indentWidth));
            string occ = OperationCounters.GetNumberDisplayString(counter.Occurrences).PadLeft(m_counters.OccurrenceWidth);
            string duration = OperationCounters.GetDurationDisplayString(counter.Duration).PadLeft(m_counters.DurationWidth);

            builder.Append(' ', indentWidth);
            builder.Append(I($"{{ 'id':'{name}'{namePadding}, 'duration':'{duration}', 'occurrences':'{occ}', 'displayAvgDuration':'{TimeSpan.FromTicks(counter.Duration.Ticks / Math.Max(1, counter.Occurrences))}'"));

            if (counter.Children.Count != 0)
            {
                counter.Children.Sort((c1, c2) => -c1.Duration.CompareTo(c2.Duration));
                builder.Append(", 'c':[");
                PrintCounterList(builder, counter.Children, depth + 1, includeAssociatedOperations);
                builder.Append("]");
            }
            else if (includeAssociatedOperations)
            {
                StackCounter stackCounter = counter as StackCounter;
                if (stackCounter != null && stackCounter.AssociatedOperations.Count != 0 && m_host != null)
                {
                    stackCounter.SortAssociatedOperations();

                    builder.Append(", 'topOps':[");
                    for (int i = 0; i < Math.Min(MaxTopOperations, stackCounter.AssociatedOperations.Count); i++)
                    {
                        var operation = stackCounter.AssociatedOperations[i];
                        string operationDuration = OperationCounters.GetDurationDisplayString(operation.Duration).PadLeft(m_counters.DurationWidth);
                        if (i != 0)
                        {
                            builder.AppendLine(",");
                        }
                        else
                        {
                            builder.AppendLine();
                        }

                        PrintIndented(builder, depth + 1, "{", newline: false);
                        builder.Append(I($"'duration': '{operationDuration}', "));
                        if (operation.PipId.IsValid)
                        {
                            builder.Append(I($"'pip': '{m_host.GetDescription(operation.PipId) ?? string.Empty}', "));
                        }

                        if (operation.Artifact.IsValid)
                        {
                            builder.Append(I($"'artifact': '{m_host.GetDescription(operation.Artifact) ?? string.Empty}', "));
                        }

                        builder.Append(I($"'details': '{operation.Operation?.Details ?? string.Empty}' }}"));
                    }

                    builder.Append("]");
                }
            }

            builder.Append("}");
        }

        internal struct CapturedOperationInfo
        {
            public PipId PipId;
            public FileOrDirectoryArtifact Artifact;
            public TimeSpan Duration;
            public StackCounter Counter;
            public Operation Operation;
        }

        private static void PrintIndented(StringBuilder builder, int indent, string s, bool newline = false)
        {
            builder.Append(' ', indent * OperationCounters.SingleIndentWidth);
            builder.Append(s);
            if (newline)
            {
                builder.AppendLine();
            }
        }

        /// <summary>
        /// Represents the scope of an operation with start time
        /// </summary>
        internal abstract class Operation
        {
            /// <summary>
            /// The root pip operation
            /// </summary>
            public readonly RootOperation Root;

            /// <summary>
            /// The current operation thread
            /// </summary>
            public OperationThread Thread { get; private set; }

            /// <summary>
            /// The operation kind
            /// </summary>
            public OperationKind Kind;

            /// <summary>
            /// Details about the operation
            /// </summary>
            public string Details;

            /// <summary>
            /// The start time of the operation
            /// </summary>
            public TimeSpan Start;

            /// <summary>
            /// The associated file or directory artifact of the operation
            /// </summary>
            public FileOrDirectoryArtifact FileOrDirectory;

            /// <summary>
            /// The number of children still outstanding
            /// </summary>
            private int m_activeSelfAndChildrenCount;

            public int ActiveSelfAndChildrenCount => Volatile.Read(ref m_activeSelfAndChildrenCount);

            /// <summary>
            /// Indicates whether the operation is complete
            /// </summary>
            public bool IsComplete => ActiveSelfAndChildrenCount == 0;

            /// <summary>
            /// The parent operation which started the operation
            /// </summary>
            public Operation Parent;

            /// <summary>
            /// The counter for the operation
            /// </summary>
            public StackCounter Counter;

            /// <summary>
            /// Indicates whether the current operation is the root operation
            /// </summary>
            public bool IsRoot => Root == this;

            /// <summary>
            /// Indicates whether operation is an operation thread which tracks its own <see cref="OperationThread.ActiveOperation"/>
            /// </summary>
            public bool IsThread { get; protected set; }

            private int m_version;

            /// <summary>
            /// Computes the duration of the operation
            /// </summary>
            public TimeSpan Duration => Root.Tracker.m_stopwatch.Elapsed - Start;

            public OpType OpType { get; set; }

            public int InstanceId { get; set; }

            /// <nodoc />
            protected Operation(RootOperation root)
            {
                Root = root ?? (RootOperation)this;
            }

            public void InitDebugData(OpType opType)
            {
                OpType = opType;
                InstanceId = Interlocked.Increment(ref s_nextInstanceId);
            }

            /// <summary>
            /// Attempts to release an operation (i.e. return it to the root operation pool
            /// if it is complete and has no more children
            /// </summary>
            public void TryRelease()
            {
                TraceDebugData(this);

                var pendingChildCount = Interlocked.Decrement(ref m_activeSelfAndChildrenCount);
                Assert(this, pendingChildCount >= 0, "count cannot be less than zero");
                if (pendingChildCount == 0)
                {
                    Parent?.TryRelease();
                    Return();
                }
            }

            public void SetThread(OperationThread thread)
            {
                Thread = thread;

                TraceDebugData(this);
            }

            /// <summary>
            /// Initializes the common operation
            /// </summary>
            protected void InitializeCore(OperationKind kind, in FileOrDirectoryArtifact artifact = default(FileOrDirectoryArtifact), string details = null)
            {
                Contract.Requires(kind.IsValid);
                Contract.Requires(Counter != null, "Must initialize counter");

                Assert(this, ActiveSelfAndChildrenCount == 0, "Cannot initialize operation with children");

                Kind = kind;
                Start = Root.Tracker.m_stopwatch.Elapsed;
                FileOrDirectory = artifact;

                TraceDebugData(this, "InitializeCoreBefore");

                // Initial value of 1 indicating the incomplete operation
                Volatile.Write(ref m_activeSelfAndChildrenCount, 1);

                TraceDebugData(this);
            }

            /// <summary>
            /// Initializes the operation's state for a parented operation
            /// </summary>
            public void Initialize(Operation parent, OperationKind kind, in FileOrDirectoryArtifact artifact, string details)
            {
                Contract.Requires(!IsRoot);

                Interlocked.Increment(ref m_version);
                TraceDebugData(this);
                if (parent.IsComplete)
                {
                    Assert(this, false, I($"Operation ({kind.Name}) started for completed parent ({parent.Kind}, active self and children: {parent.ActiveSelfAndChildrenCount})"));
                }

                Counter = Root.Tracker.m_counters.GetCounter(kind, parent: parent.Counter);
                if (!IsThread)
                {
                    Parent = parent;
                    TraceDebugData(this, "InitializeWithParent");
                    int selfAndChildCount = Interlocked.Increment(ref parent.m_activeSelfAndChildrenCount);
                    Assert(this, selfAndChildCount > 1, "Attempted to attach to complete parent");
                }

                InitializeCore(kind, artifact, details);
            }

            /// <summary>
            /// Reports time for an external nested operation
            /// </summary>
            public void ReportExternalOperation(OperationKind kind, TimeSpan duration, int occurrences = 1)
            {
                var nestedCounter = Root.Tracker.m_counters.GetCounter(kind, parent: Counter);
                nestedCounter.Add(duration, occurrences: occurrences);
            }

            /// <summary>
            /// Completes the operation
            /// </summary>
            public void Complete()
            {
                Interlocked.Increment(ref m_version);
                TraceDebugData(this);
                // Assert done in the if condition because getting the child name and constructring this string is expensive.
                if (ActiveSelfAndChildrenCount != 1)
                {
                    Assert(this, false, I($"Must have no pending children Kind:{Kind.Name}, Child Kind:{Root.GetChildOperationName(this)} "));
                }

                Assert(this, !IsComplete, "Operations can only be completed once");

                Counter.Add(Duration);

                Assert(this, Thread.ActiveOperation == this, "Must be the active operation");
                if (!IsThread)
                {
                    var parent = Parent;
                    Assert(this, !parent.IsComplete, "Parent cannot complete before child operations");
                    Thread.ActiveOperation = parent;
                }

                Assert(this, ActiveSelfAndChildrenCount == 1, "Must have no pending children 2");
                TryRelease();
            }

            public CapturedOperationInfo? Capture()
            {
                var version = m_version;
                if (!IsComplete)
                {
                    var info = new CapturedOperationInfo()
                    {
                        Counter = Counter,
                        Artifact = FileOrDirectory,
                        PipId = Root.PipId,
                        Duration = Duration,
                        Operation = this,
                    };

                    if (Volatile.Read(ref m_version) == version)
                    {
                        return info;
                    }
                }

                return null;
            }

            public override string ToString()
            {
                return string.Join(", ", GetDescriptionParts());
            }

            private IEnumerable<string> GetDescriptionParts()
            {
                yield return Kind.Name;

                yield return I($"Duration: '{(long)Duration.TotalSeconds}s'");

                var host = Root.Tracker.m_host;

                if (host != null)
                {
                    if (Root.PipId.IsValid)
                    {
                        yield return I($"Pip: '{host.GetDescription(Root.PipId)}'");
                    }

                    if (FileOrDirectory.IsValid)
                    {
                        yield return I($"Artifact: '{host.GetDescription(FileOrDirectory)}'");
                    }

                    if (Details != null)
                    {
                        yield return I($"Details: '{Details}'");
                    }
                }
            }

            /// <summary>
            /// Returns the operation to the pool
            /// </summary>
            public abstract void Return();
        }

        /// <summary>
        /// Nested operation which becomes the active operation of
        /// the current thread while in scope
        /// </summary>
        internal sealed class NestedOperation : Operation
        {
            /// <nodoc />
            public NestedOperation(RootOperation root)
                : base(root)
            {
                InitDebugData(OpType.Nested);
            }

            /// <inheritdoc />
            public override void Return()
            {
                TraceDebugData(this);

                Root.ReturnOperation(this);
            }
        }

        /// <summary>
        /// Represents a new operation thread which is not awaited by the parent operation and
        /// tracks its own <see cref="ActiveOperation"/>.
        /// </summary>
        internal class OperationThread : Operation
        {
            /// <summary>
            /// The active operation of the thread
            /// </summary>
            public Operation ActiveOperation;

            /// <nodoc />
            public OperationThread(RootOperation root)
                : base(root)
            {
                InitDebugData(OpType.Thread);
                ActiveOperation = this;
                IsThread = true;
                SetThread(this);
            }

            /// <summary>
            /// Starts a new operation thread parented by the <see cref="ActiveOperation"/>
            /// </summary>
            public Operation StartThread(OperationKind kind, in FileOrDirectoryArtifact artifact, string details)
            {
                TraceDebugData(this);
                kind = SpecializeKind(kind);
                OperationThread thread = Root.CreateUnitializedOperation(newThread: true).Thread;
                thread.Initialize(ActiveOperation, kind, artifact, details);
                thread.ActiveOperation = thread;
                return thread;
            }

            /// <summary>
            /// Starts a new nested operation parented by the <see cref="ActiveOperation"/>
            /// </summary>
            public Operation StartNestedOperation(OperationKind kind, in FileOrDirectoryArtifact artifact, string details)
            {
                TraceDebugData(this);
                kind = SpecializeKind(kind);
                Operation result = Root.CreateUnitializedOperation(newThread: false);
                result.SetThread(this);
                result.Initialize(ActiveOperation, kind, artifact, details);
                ActiveOperation = result;
                return result;
            }

            /// <summary>
            /// Gets the specialized operation kind for the pip type of the root operation
            /// </summary>
            protected OperationKind SpecializeKind(OperationKind kind)
            {
                return kind.HasPipTypeSpecialization ?
                    kind.GetPipTypeSpecialization(Root.PipType) :
                    kind;
            }

            /// <inheritdoc />
            public override void Return()
            {
                TraceDebugData(this);

                Root.ReturnThread(this);
            }
        }

        /// <summary>
        /// A root operation for a particular pip. This is also an operation thread which tracks its own <see cref="OperationThread.ActiveOperation"/>
        /// </summary>
        internal sealed class RootOperation : OperationThread
        {
            /// <summary>
            /// The operation tracker
            /// </summary>
            public readonly OperationTracker Tracker;

            /// <summary>
            /// The pip id of the root operation
            /// </summary>
            public PipId PipId;

            /// <summary>
            /// The pip type of the pip
            /// </summary>
            public PipType PipType;

            /// <summary>
            /// Callback for operation completions
            /// </summary>
            public Action<OperationKind, TimeSpan> OnOperationCompleted { get; set; }

            // Operation thread pool
            private readonly Stack<OperationThread> m_operationThreadPool = new Stack<OperationThread>();

            // Nested operation pool
            private readonly Stack<NestedOperation> m_operationPool = new Stack<NestedOperation>();

            // All the created operations (this is used for debugging purposes only)
            public readonly ConcurrentQueue<Operation> CreatedOperations = new ConcurrentQueue<Operation>();

            // Factory for operation threads
            private static readonly Func<RootOperation, OperationThread> s_threadFactory = root => new OperationThread(root);

            // Factory for nested operations
            private static readonly Func<RootOperation, NestedOperation> s_nestedFactory = root => new NestedOperation(root);

            /// <summary>
            /// Creates a new root operation
            /// </summary>
            public RootOperation(OperationTracker tracker)
                : base(root: null)
            {
                InitDebugData(OpType.Root);
                Tracker = tracker;
            }

            /// <summary>
            /// Initializes the root operation's state
            /// </summary>
            public void Initialize(PipId pipId, PipType pipType, OperationKind kind, Action<OperationKind, TimeSpan> onOperationCompleted)
            {
                kind = SpecializeKind(kind);
                PipId = pipId;
                PipType = pipType;
                Thread.ActiveOperation = this;
                OnOperationCompleted = onOperationCompleted;
                Counter = Tracker.m_counters.GetCounter(kind, parent: null);
                InitializeCore(kind, default(FileOrDirectoryArtifact));
            }

            /// <summary>
            /// Creates a new uninitialized operation
            /// </summary>
            public Operation CreateUnitializedOperation(bool newThread = false)
            {
                if (newThread)
                {
                    return CreateUnitializedOperation(m_operationThreadPool, s_threadFactory);
                }
                else
                {
                    return CreateUnitializedOperation(m_operationPool, s_nestedFactory);
                }
            }

            private T CreateUnitializedOperation<T>(
                Stack<T> pool,
                Func<RootOperation, T> factory)
                where T : Operation
            {
                lock (pool)
                {
                    if (pool.Count == 0)
                    {
                        var operation = factory(this);
                        TraceDebugData(operation);

                        CreatedOperations.Enqueue(operation);

                        return operation;
                    }
                    else
                    {
                        return pool.Pop();
                    }
                }
            }

            /// <inheritdoc />
            public override void Return()
            {
                TraceDebugData(this);

                Tracker.m_activePipOperations.Remove(this);
                Tracker.m_rootOperationPool.Enqueue(this);
            }

            /// <summary>
            /// Returns a nested operation to the pool of nested operations
            /// </summary>
            public void ReturnOperation(NestedOperation operation)
            {
                TraceDebugData(this);

                Assert(operation, Thread.ActiveOperation != operation, "Returned operations must not remain active");

                OnOperationCompleted?.Invoke(operation.Kind, operation.Duration);

                lock (m_operationPool)
                {
                    m_operationPool.Push(operation);
                }
            }

            /// <summary>
            /// Returns an operation thread to the pool of operation threads
            /// </summary>
            public void ReturnThread(OperationThread thread)
            {
                TraceDebugData(this);

                Assert(thread, Thread.ActiveOperation != thread, "Returned operations must not remain active");

                lock (m_operationThreadPool)
                {
                    m_operationThreadPool.Push(thread);
                }
            }

            internal string GetChildOperationName(Operation operation)
            {
                foreach (var child in CreatedOperations)
                {
                    if (child.Parent == operation)
                    {
                        return I($"{child.Kind.Name} -> {GetChildOperationName(child)}");
                    }
                }

                return "[Unknown]";
            }
        }

        internal sealed class OperationDebugData
        {
            public OpType OpType;
            public int InstanceId;
            public int RootInstanceId;
            public OperationKind Kind;
            public OperationKind ParentKind;
            public string CallerName;
            public PipId PipId;
            public bool IsComplete;
            public int ActiveSelfAndChildCount;
            public int ParentActiveSelfAndChildCount;
            public int ThreadActiveOperationInstanceId;
            public int ThreadInstanceId;
            public int ParentInstanceId;

            public override string ToString()
            {
                return I($@"{{
  IID = {InstanceId},
  RIID = {RootInstanceId},
  PIID = {ParentInstanceId},
  OT = {OpType},
  K = {Kind},
  PK = {ParentKind},
  CN = {CallerName},
  PID = {PipId.Value},
  IC = {IsComplete},
  ASCC = {ActiveSelfAndChildCount},
  P_ASCC = {ParentActiveSelfAndChildCount},
  TIID = {ThreadInstanceId},
  TAOIID = {ThreadActiveOperationInstanceId},
}}");
            }
        }

        private static void Assert(Operation operation, bool condition, string message = null)
        {
            if (condition)
            {
                return;
            }

            lock (s_debugDatas)
            {
                if (s_enableDebugTracing)
                {
                    TraceDebugData(operation);
                    s_enableDebugTracing = false;

                    var builder = new StringBuilder();
                    using (StreamWriter writer = new StreamWriter(@"E:\shared\trace.txt"))
                    {
                        writer.WriteLine(I($"InstanceId: {operation.InstanceId} raised assertion. {message}"));

                        var datas = s_debugDatas.Reverse().ToArray();
                        foreach (var item in datas)
                        {
                            writer.WriteLine(item.ToString());
                            builder.AppendLine(item.ToString());

                            if (builder.Length > 200000)
                            {
                                Events.Log.ErrorEvent(builder.ToString());
                                builder.Clear();
                            }
                        }
                    }

                    Events.Log.ErrorEvent(builder.ToString());
                    builder.Clear();

                    message = message ?? string.Empty;
                    if (!condition)
                    {
                        Contract.Assert(false, I($"InstanceId: {operation.InstanceId} raised assertion. {message}"));
                    }
                }
                else
                {
                    Contract.Assert(condition, message);
                }
            }
        }

        private static void TraceDebugData(Operation operation, [CallerMemberName] string callerName = null)
        {
            if (!s_enableDebugTracing)
            {
                return;
            }

            if (Interlocked.Increment(ref s_debugDataCount) > 20000)
            {
                OperationDebugData ignored;
                s_debugDatas.TryDequeue(out ignored);
            }

            s_debugDatas.Enqueue(new OperationDebugData()
            {
                InstanceId = operation.InstanceId,
                RootInstanceId = operation.Root?.InstanceId ?? -999,
                OpType = operation.OpType,
                Kind = operation.Kind,
                ParentInstanceId = operation.Parent?.InstanceId ?? -999,
                ParentActiveSelfAndChildCount = operation.Parent?.ActiveSelfAndChildrenCount ?? -999,
                ParentKind = operation.Parent?.Kind ?? OperationKind.Invalid,
                CallerName = callerName,
                PipId = operation.Root?.PipId ?? PipId.Invalid,
                IsComplete = operation.IsComplete,
                ActiveSelfAndChildCount = operation.ActiveSelfAndChildrenCount,
                ThreadInstanceId = operation.Thread?.InstanceId ?? -999,
                ThreadActiveOperationInstanceId = operation.Thread?.ActiveOperation?.InstanceId ?? -999,
            });
        }

        internal enum OpType
        {
            Root,
            Thread,
            Nested,
        }

        private static readonly ConcurrentQueue<OperationDebugData> s_debugDatas = new ConcurrentQueue<OperationDebugData>();

        private static int s_nextInstanceId = 0;

        private static int s_debugDataCount = 0;

        /// <summary>
        /// Hierarchical (stack) counter for operations with pointer to parent and aggregate counter
        /// </summary>
        internal sealed class StackCounter : Counter
        {
            /// <summary>
            /// The parent counter
            /// </summary>
            public readonly Counter Parent;

            /// <summary>
            /// The aggregate counter for all operations of the <see cref="Counter.Kind"/>
            /// </summary>
            public readonly Counter AggregateCounter;

            /// <summary>
            /// The counter for all outstanding operations of the <see cref="Counter.Kind"/>
            /// </summary>
            public readonly StackCounter OutstandingCounter;

            /// <summary>
            /// Gets associated operations for the counter
            /// </summary>
            public readonly List<CapturedOperationInfo> AssociatedOperations = new List<CapturedOperationInfo>(0);

            /// <summary>
            /// Creates a new hierachical counter
            /// </summary>
            public StackCounter(OperationKind kind, Counter parent, Counter aggregateCounter, StackCounter outstandingCounter = null)
                : base(kind)
            {
                Parent = parent;
                AggregateCounter = aggregateCounter;
                OutstandingCounter = outstandingCounter;
                AggregateCounter.Children.Add(this);
                parent?.Children.Add(this);
                Depth = (parent?.Depth ?? -1) + 1;
            }

            /// <summary>
            /// Adds the duration of the operation to the counter
            /// </summary>
            public void Add(TimeSpan duration, int occurrences = 1)
            {
                Interlocked.Add(ref Occurrences, occurrences);
                Interlocked.Add(ref DurationTicks, duration.Ticks);

                Interlocked.Increment(ref AggregateCounter.Occurrences);
                Interlocked.Add(ref AggregateCounter.DurationTicks, duration.Ticks);
            }

            public void SortAssociatedOperations()
            {
                AssociatedOperations.Sort((o1, o2) => -o1.Duration.CompareTo(o2.Duration));
            }

            public override void Reset()
            {
                base.Reset();
                Children.Clear();
                AssociatedOperations.Clear();
            }

            public override void ReattachActive()
            {
                base.ReattachActive();
                if (Occurrences != 0)
                {
                    Parent?.Children.Add(this);
                }
            }
        }

        /// <summary>
        /// An operation counter
        /// </summary>
        internal class Counter
        {
            /// <summary>
            /// The name of the operation
            /// </summary>
            public string Name => Kind.Name;

            /// <summary>
            /// The operation kind
            /// </summary>
            public readonly OperationKind Kind;

            /// <summary>
            /// The child counters
            /// </summary>
            public readonly List<StackCounter> Children = new List<StackCounter>();

            /// <summary>
            /// The nested depth of the coutner
            /// </summary>
            public int Depth;

            /// <summary>
            /// Creates a new operation counter
            /// </summary>
            public Counter(OperationKind kind)
            {
                Kind = kind;
            }

            /// <summary>
            /// The total duration of operations for the counter
            /// </summary>
            public TimeSpan Duration => TimeSpan.FromTicks(DurationTicks);

            /// <summary>
            /// The underlying ticks for <see cref="Duration"/>
            /// </summary>
            public long DurationTicks;

            /// <summary>
            /// The number of occurrences of operations for the counter
            /// </summary>
            public long Occurrences;

            /// <summary>
            /// Resets the duration and occurrences for an outstanding counter
            /// </summary>
            public virtual void Reset()
            {
                Occurrences = 0;
                DurationTicks = 0;
            }

            /// <summary>
            /// Reattaches an outstanding counter to the parent if it have occurrences
            /// </summary>
            public virtual void ReattachActive()
            {
            }

            public override string ToString()
            {
                return I($"{Name} (Duration = {Duration}, Occurrencies: {Occurrences})");
            }
        }

        /// <summary>
        /// Hierachical counters for operations
        /// </summary>
        private sealed class OperationCounters
        {
            /// <summary>
            /// Number format used for durations
            /// </summary>
            private static readonly NumberFormatInfo s_numberFormat = new NumberFormatInfo { NumberDecimalDigits = 0, NumberDecimalSeparator = "," };

            private int m_nameWidth;
            private int m_occurenceWidth;
            private int m_durationWidth;

            /// <summary>
            /// The width of a single indent
            /// </summary>
            public const int SingleIndentWidth = 2;

            /// <summary>
            /// Width of name column
            /// </summary>
            public int NameWidth
            {
                get
                {
                    ComputeWidths();
                    return m_nameWidth;
                }
            }

            /// <summary>
            /// Width of duration column
            /// </summary>
            public int DurationWidth
            {
                get
                {
                    ComputeWidths();
                    return m_durationWidth;
                }
            }

            /// <summary>
            /// Width of occurrence column
            /// </summary>
            public int OccurrenceWidth
            {
                get
                {
                    ComputeWidths();
                    return m_occurenceWidth;
                }
            }

            private Counter[] m_aggregateCounters;
            private Counter[] m_aggregateOutstandingCounters;

            /// <summary>
            /// Lock for accessing the counter map
            /// </summary>
            private readonly ReadWriteLock m_counterMapLock = ReadWriteLock.Create();

            /// <summary>
            /// The root counters (i.e. counters with no parent counter)
            /// </summary>
            public readonly List<StackCounter> RootCounters = new List<StackCounter>();

            /// <summary>
            /// All counters (including nested counters)
            /// </summary>
            public readonly List<StackCounter> AllCounters = new List<StackCounter>();

            /// <summary>
            /// The root counters (i.e. counters with no parent counter) for outstanding operations
            /// </summary>
            public readonly List<StackCounter> RootOutstandingCounters = new List<StackCounter>();

            /// <summary>
            /// All counters (including nested counters) for outstanding operations
            /// </summary>
            public readonly List<Counter> AllOutstandingCounters = new List<Counter>();

            /// <summary>
            /// The map of parent counter and kind to nested counter
            /// </summary>
            private readonly Dictionary<(StackCounter, OperationKind), StackCounter> m_counterMap =
                new Dictionary<(StackCounter, OperationKind), StackCounter>();

            /// <summary>
            /// Creates the operation counter
            /// </summary>
            public OperationCounters()
            {
                m_aggregateCounters = new Counter[OperationKind.AllOperations.Count * 2];
                m_aggregateOutstandingCounters = new Counter[OperationKind.AllOperations.Count * 2];
            }

            /// <summary>
            /// Gets the counter for the operation for the kind and parent counter
            /// </summary>
            public StackCounter GetCounter(OperationKind kind, StackCounter parent)
            {
                StackCounter counter;
                var key = (parent, kind);
                using (m_counterMapLock.AcquireReadLock())
                {
                    if (m_counterMap.TryGetValue(key, out counter))
                    {
                        return counter;
                    }
                }

                // Slow path involving write lock
                using (m_counterMapLock.AcquireWriteLock())
                {
                    if (!m_counterMap.TryGetValue(key, out counter))
                    {
                        if (m_aggregateCounters.Length < OperationKind.AllOperations.Count)
                        {
                            var allOperationsCount = OperationKind.AllOperations.Count;
                            Array.Resize(ref m_aggregateCounters, allOperationsCount * 2);
                            Array.Resize(ref m_aggregateOutstandingCounters, allOperationsCount * 2);
                        }

                        var aggregateCounter = m_aggregateCounters[kind];
                        if (aggregateCounter == null)
                        {
                            aggregateCounter = new Counter(kind);
                            m_aggregateCounters[kind] = aggregateCounter;
                        }

                        var aggregateOutstandingCounter = m_aggregateOutstandingCounters[kind];
                        if (aggregateOutstandingCounter == null)
                        {
                            aggregateOutstandingCounter = new Counter(kind);
                            m_aggregateOutstandingCounters[kind] = aggregateOutstandingCounter;
                            AllOutstandingCounters.Add(aggregateOutstandingCounter);
                        }

                        var outstandingCounter = new StackCounter(kind, parent: parent?.OutstandingCounter, aggregateCounter: aggregateOutstandingCounter);
                        counter = new StackCounter(kind, parent: parent, aggregateCounter: aggregateCounter, outstandingCounter: outstandingCounter);
                        if (parent == null)
                        {
                            RootCounters.Add(counter);
                            RootOutstandingCounters.Add(outstandingCounter);
                        }

                        AllCounters.Add(counter);
                        AllOutstandingCounters.Add(outstandingCounter);
                        m_counterMap[key] = counter;
                    }
                }

                return counter;
            }

            /// <summary>
            /// Gets the aggregate counter for the operation kind or null if none exists
            /// </summary>
            public Counter TryGetAggregateCounter(OperationKind kind)
            {
                return m_aggregateCounters[kind];
            }

            public void ResetWidths()
            {
                m_nameWidth = 0;
            }

            public ReadLock ReadCounters()
            {
                return m_counterMapLock.AcquireReadLock();
            }

            /// <summary>
            /// Computes the widths of columns in the output counter file
            /// </summary>
            public void ComputeWidths(bool outstandingCounters = false)
            {
                if (m_nameWidth == 0)
                {
                    int maxNameLength = 0;

                    // Max depth starts out at 1 to handle case for printing aggregate operations
                    // which is not recorded in the counter depth
                    int maxDepth = 1;
                    long maxOccurrence = 0;
                    long maxDuration = 0;
                    var counters = outstandingCounters ? (IEnumerable<Counter>)AllOutstandingCounters : AllCounters;
                    foreach (var counter in counters)
                    {
                        maxNameLength = Math.Max(maxNameLength, counter.Name.Length);
                        maxDepth = Math.Max(maxDepth, counter.Depth);
                        maxOccurrence = Math.Max(maxOccurrence, counter.Occurrences);
                        maxDuration = Math.Max(maxDuration, counter.DurationTicks);
                    }

                    m_nameWidth = maxNameLength + (maxDepth * SingleIndentWidth);
                    m_occurenceWidth = GetNumberDisplayString(maxOccurrence).Length;

                    m_durationWidth = GetDurationDisplayString(TimeSpan.FromTicks(maxDuration)).Length;
                }
            }

            /// <summary>
            /// Gets the display string for a number
            /// </summary>
            public static string GetNumberDisplayString(long number)
            {
                return number.ToString("N", s_numberFormat);
            }

            /// <summary>
            /// Gets the display string for a duration
            /// </summary>
            public static string GetDurationDisplayString(TimeSpan duration)
            {
                return ((long)duration.TotalMilliseconds).ToString("N", s_numberFormat);
            }
        }
    }
}
