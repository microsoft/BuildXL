// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Operations performed during pip execution
    /// NOTE: This enum-like struct handles mapping from operations for <see cref="PipExecutorCounter"/>
    /// and <see cref="PipExecutionStep"/> along with pip-type specific variations of those operations
    /// </summary>
    public readonly struct OperationKind : IEquatable<OperationKind>
    {
        /// <summary>
        /// All operation kinds
        /// </summary>
        public static IReadOnlyList<OperationKind> AllOperations => Inner.AllOperations;

        /// <summary>
        /// Gets the invalid operation
        /// </summary>
        public static readonly OperationKind Invalid = default(OperationKind);

        /// <summary>
        /// Gets the pass-through operation which is not externally reported but can
        /// be used to change operation context
        /// </summary>
        public static OperationKind PassThrough => Inner.PassThroughOperationKind;

        /// <summary>
        /// Indicates whether the operation has a pip type specific variation.
        /// NOTE: The actual pip type specific variation will return false for this
        /// i.e.
        /// PipExecutionStep.ExecuteNonProcess returns TRUE
        /// PipExecutionStep.ExecuteNonProcess.HashSourceFile returns FALSE
        /// </summary>
        public bool HasPipTypeSpecialization => m_pipTypeCounterStartIndex >= 0;

        /// <summary>
        /// The index of the pip type specific variation in the <see cref="AllOperations"/> array
        /// </summary>
        private readonly short m_pipTypeCounterStartIndex;

        /// <summary>
        /// The integral value of the <see cref="OperationKind"/>
        /// </summary>
        public readonly ushort Value;

        /// <summary>
        /// The integral id for the counter in a cache lookup counter map
        /// </summary>
        internal readonly short CacheLookupCounterId;

        /// <summary>
        /// Gets whether the operation kind is valid
        /// </summary>
        public readonly bool IsValid;

        /// <summary>
        /// Gets the name of the operation
        /// </summary>
        public string Name => IsValid ? Inner.Names[Value] : "[Invalid]";

        /// <summary>
        /// Tracked cachelookup counter count
        /// </summary>
        public static int TrackedCacheLookupCounterCount => Inner.TrackedCacheLookupCounterKinds.Count;

        private OperationKind(ushort index, short? pipTypeCounterStartIndex = null, short cacheLookupCounterId = -1)
        {
            Value = index;
            IsValid = true;
            m_pipTypeCounterStartIndex = pipTypeCounterStartIndex ?? -1;
            CacheLookupCounterId = cacheLookupCounterId;
        }

        /// <inheritdoc />
        public bool Equals(OperationKind other)
        {
            return other.Value == Value;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <summary>
        /// Indicates whether two object instances are equal.
        /// </summary>
        public static bool operator ==(OperationKind left, OperationKind right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two objects instances are not equal.
        /// </summary>
        public static bool operator !=(OperationKind left, OperationKind right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Name;
        }

        /// <summary>
        /// Converts the <see cref="OperationKind"/> to its integral value
        /// </summary>
        public static implicit operator int(OperationKind operation)
        {
            return operation.Value;
        }

        /// <summary>
        /// Converts the <see cref="PipExecutorCounter"/> to corresponding <see cref="OperationKind"/>
        /// </summary>
        public static implicit operator OperationKind(PipExecutorCounter counter)
        {
            return Inner.PipExecutorCounterKinds[(int)counter];
        }

        /// <summary>
        /// Converts the <see cref="PipExecutionStep"/> to corresponding <see cref="OperationKind"/>
        /// </summary>
        public static implicit operator OperationKind(PipExecutionStep step)
        {
            return Inner.PipExecutionStepKinds[(int)step];
        }

        /// <summary>
        /// Converts the <see cref="OperationCounter"/> to corresponding <see cref="OperationKind"/>
        /// </summary>
        public static implicit operator OperationKind(OperationCounter operation)
        {
            return Inner.OperationCounterKinds[(int)operation];
        }

        /// <summary>
        /// Gets whether the <see cref="PipExecutorCounter"/> should expand pip type specific variations
        /// </summary>
        private static bool IsPipTypeSpecificCounter(PipExecutorCounter counter)
        {
            switch (counter)
            {
                case PipExecutorCounter.ExecutePipStepDuration:
                case PipExecutorCounter.ScheduleDependentDuration:
                case PipExecutorCounter.ScheduledByDependencyDuration:
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Get tracked cache operation kind
        /// </summary>
        public static OperationKind GetTrackedCacheOperationKind(int id)
        {
            return Inner.TrackedCacheLookupCounterKinds[id];
        }

        /// <summary>
        /// Gets whether the <see cref="PipExecutorCounter"/> should expand pip type specific variations
        /// </summary>
        private static bool IsTrackedCacheCounter(PipExecutorCounter counter)
        {
            switch (counter)
            {
                case PipExecutorCounter.ObservedInputProcessorPreProcessDuration:
                case PipExecutorCounter.ObservedInputProcessorPass1InitializeObservationInfosDuration:
                case PipExecutorCounter.ObservedInputProcessorTryQuerySealedInputContentDuration:
                case PipExecutorCounter.ObservedInputProcessorTryProbeForExistenceDuration:
                case PipExecutorCounter.ObservedInputProcessorComputePipFileSystemPaths:
                case PipExecutorCounter.ObservedInputProcessorReportUnexpectedAccess:
                case PipExecutorCounter.ObservedInputProcessorComputeSearchPathsAndFilterDuration:
                case PipExecutorCounter.ObservedInputProcessorTryQueryDirectoryFingerprintDuration:
                case PipExecutorCounter.ComputeWeakFingerprintDuration:
                case PipExecutorCounter.CacheQueryingWeakFingerprintDuration:
                case PipExecutorCounter.TryLoadPathSetFromContentCacheDuration:
                case PipExecutorCounter.CheckProcessRunnableFromCacheExecutionLogDuration:
                case PipExecutorCounter.CheckProcessRunnableFromCacheChapter3RetrieveAndParseMetadataDuration:
                case PipExecutorCounter.CheckProcessRunnableFromCacheChapter4CheckContentAvailabilityDuration:
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the pip type specialized operation. Internal use only. This should by called by the <see cref="OperationTracker"/>
        /// </summary>
        internal OperationKind GetPipTypeSpecialization(PipType pipType)
        {
            Contract.Requires(HasPipTypeSpecialization);
            return Inner.PipTypeSpecificOperations[m_pipTypeCounterStartIndex + (int)pipType];
        }

        /// <summary>
        /// Creates a new operation kind
        /// </summary>
        /// <param name="name">the name</param>
        /// <param name="hasPipTypeSpecialization">if the operation is different for distinct pip type kinds</param>
        /// <returns>the created operation kind</returns>
        internal static OperationKind Create(string name, bool hasPipTypeSpecialization = false)
        {
            return Inner.CreateOperationKind(name, hasPipTypeSpecialization);
        }

        #region Static Initialization

        private const string DurationSuffix = "Duration";

        /// <summary>
        /// Strange behavior in CLR does not allow these static fields to be declared directly in the
        /// enclosing class (throws a <see cref="TypeLoadException"/>)
        /// </summary>
        private static class Inner
        {
            public static readonly OperationKind PassThroughOperationKind;

            public static IReadOnlyList<OperationKind> AllOperations => s_allOperations;

            public static IReadOnlyList<OperationKind> PipExecutorCounterKinds => s_pipExecutorCounterKinds;

            public static IReadOnlyList<OperationKind> TrackedCacheLookupCounterKinds => s_trackedCacheCounterKinds;

            public static IReadOnlyList<OperationKind> OperationCounterKinds => s_operationCounterKinds;

            public static IReadOnlyList<OperationKind> PipExecutionStepKinds => s_pipExecutionStepKinds;

            public static IReadOnlyList<OperationKind> PipTypeSpecificOperations => s_pipTypeSpecificOperations;

            public static IReadOnlyList<string> Names => s_names;

            private static readonly List<OperationKind> s_allOperations = new List<OperationKind>();
            private static readonly List<OperationKind> s_trackedCacheCounterKinds = new List<OperationKind>();
            private static readonly List<OperationKind> s_pipExecutorCounterKinds = new List<OperationKind>();
            private static readonly List<OperationKind> s_operationCounterKinds = new List<OperationKind>();
            private static readonly List<OperationKind> s_pipExecutionStepKinds = new List<OperationKind>();
            private static readonly List<OperationKind> s_pipTypeSpecificOperations = new List<OperationKind>();
            private static readonly List<string> s_names = new List<string>();

            private static object s_addOperationLock = new object();

            private static readonly PipType[] s_pipTypes = EnumTraits<PipType>.EnumerateValues().ToArray();
            private static readonly string[] s_pipTypeSuffixes = s_pipTypes.Select(p => "." + p.ToString()).ToArray();

            /// <summary>
            /// Creates a new operation kind
            /// </summary>
            /// <param name="name">the name</param>
            /// <param name="hasPipTypeSpecialization">if the operation is different for distinct pip type kinds</param>
            /// <param name="trackedCacheCounterId">if the given operation is one of the tracked cache lookup counter</param>
            /// <returns>the created operation kind</returns>
            public static OperationKind CreateOperationKind(string name, bool hasPipTypeSpecialization, int trackedCacheCounterId = -1)
            {
                lock (s_addOperationLock)
                {
                    int index = s_allOperations.Count;
                    short? pipTypeCounterStartIndex = hasPipTypeSpecialization ?
                        (short?)s_pipTypeSpecificOperations.Count : null;
                    var operation = new OperationKind((ushort)index, pipTypeCounterStartIndex, (short)trackedCacheCounterId);
                    s_allOperations.Add(operation);
                    s_names.Add(name);

                    if (hasPipTypeSpecialization)
                    {
                        foreach (var pipTypeSuffix in s_pipTypeSuffixes)
                        {
                            s_pipTypeSpecificOperations.Add(CreateOperationKind(name + pipTypeSuffix, /* hasPipTypeSpecialization */ false, trackedCacheCounterId));
                        }
                    }

                    return operation;
                }
            }

            /// <summary>
            /// Static constructor used to initialize <see cref="OperationKind"/>s and the mappings
            /// from <see cref="PipExecutorCounter"/>s and <see cref="PipExecutionStep"/>
            /// </summary>
            [SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline")]
            static Inner()
            {
                PassThroughOperationKind = CreateOperationKind("[Pass-Through]", false);

                var prefix = "PipExecutorCounter.";
                foreach (var counter in EnumTraits<PipExecutorCounter>.EnumerateValues())
                {
                    int trackedCacheCounterId = IsTrackedCacheCounter(counter) ? s_trackedCacheCounterKinds.Count : -1;

                    if (!CounterCollection<PipExecutorCounter>.IsStopwatch(counter))
                    {
                        s_pipExecutorCounterKinds.Add(Invalid);
                        continue;
                    }

                    var counterName = counter.ToString();
                    if (counterName.EndsWith(DurationSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        counterName = counterName.Substring(0, counterName.Length - DurationSuffix.Length);
                    }

                    counterName = prefix + counterName;

                    var operationKind = CreateOperationKind(counterName, /* hasPipTypeSpecialization */ IsPipTypeSpecificCounter(counter), trackedCacheCounterId: trackedCacheCounterId);
                    s_pipExecutorCounterKinds.Add(operationKind);

                    if (trackedCacheCounterId != -1)
                    {
                        s_trackedCacheCounterKinds.Add(operationKind);
                    }
                }

                prefix = "PipExecutionStep.";
                foreach (var counter in EnumTraits<PipExecutionStep>.EnumerateValues())
                {
                    var counterName = counter.ToString();
                    counterName = prefix + counterName;
                    var operationKind = CreateOperationKind(counterName, /* hasPipTypeSpecialization */ true);
                    s_pipExecutionStepKinds.Add(operationKind);
                }

                prefix = "Operation.";
                foreach (var counter in EnumTraits<OperationCounter>.EnumerateValues())
                {
                    var counterName = counter.ToString();
                    counterName = prefix + counterName;
                    var operationKind = CreateOperationKind(counterName, /* hasPipTypeSpecialization */ false);
                    s_operationCounterKinds.Add(operationKind);
                }
            }
        }

        #endregion Static Initialization
    }
}
