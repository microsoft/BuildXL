// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler.Diagnostics
{
    /// <summary>
    /// Performs anomaly detection based on sizes of ObservedInputs
    /// </summary>
    public class ObservedInputAnomalyAnalyzer : ExecutionAnalyzerBase
    {
        private readonly ConcurrentBigMap<PipId, ObservedInputCounts> m_observedInputCounts = new ConcurrentBigMap<PipId, ObservedInputCounts>();

        /// <nodoc />
        public ObservedInputAnomalyAnalyzer(PipGraph pipGraph)
            : base(pipGraph)
        {
        }

        /// <inheritdoc />
        public override bool CanHandleEvent(ExecutionEventId eventId, long timestamp, int eventPayloadSize)
        {
            return
                eventId == ExecutionEventId.ProcessFingerprintComputation &&
                eventId == ExecutionEventId.PipExecutionStepPerformanceReported;
        }

        /// <inheritdoc />
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            if (data.Kind == FingerprintComputationKind.CacheCheck)
            {
                ObservedInputCounts? cacheMaxCounts = null;
                if (data.StrongFingerprintComputations != null)
                {
                    foreach (var strongFingerprintComputation in data.StrongFingerprintComputations)
                    {
                        if (strongFingerprintComputation.Succeeded)
                        {
                            var computationCounts = GetObservedInputCount(strongFingerprintComputation.ObservedInputs);
                            cacheMaxCounts = cacheMaxCounts?.Max(computationCounts) ?? computationCounts;
                        }
                    }

                    if (cacheMaxCounts.HasValue)
                    {
                        m_observedInputCounts.TryAdd(data.PipId, cacheMaxCounts.Value);
                    }
                }
            }
            else
            {
                Contract.Assert(data.Kind == FingerprintComputationKind.Execution);

                if (((data.StrongFingerprintComputations?.Count ?? 0) != 0) && data.StrongFingerprintComputations[0].Succeeded)
                {
                    ObservedInputCounts cacheMaxCounts;
                    if (m_observedInputCounts.TryRemove(data.PipId, out cacheMaxCounts))
                    {
                        var executionCounts = GetObservedInputCount(data.StrongFingerprintComputations[0].ObservedInputs);
                        ObservedInputCounts.LogForLowObservedInputs(
                            Events.StaticContext,
                            GetDescription(GetPip(data.PipId)),
                            executionCounts: executionCounts,
                            cacheMaxCounts: cacheMaxCounts);
                    }
                }
            }
        }

        /// <inheritdoc />
        public override void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
        {
            // Remove the counts after the pip completes
            if (data.Step == PipExecutionStep.HandleResult && PipGraph.PipTable.GetPipType(data.PipId) == PipType.Process)
            {
                m_observedInputCounts.RemoveKey(data.PipId);
            }
        }

        /// <summary>
        /// Get the observed input counts
        /// </summary>
        public static ObservedInputCounts GetObservedInputCount(ReadOnlyArray<ObservedInput> observedInputs)
        {
            ObservedInputCounts count = default(ObservedInputCounts);
            foreach (var item in observedInputs)
            {
                switch (item.Type)
                {
                    case ObservedInputType.AbsentPathProbe:
                        count.AbsentPathProbeCount++;
                        break;
                    case ObservedInputType.DirectoryEnumeration:
                        count.DirectoryEnumerationCount++;
                        break;
                    case ObservedInputType.ExistingDirectoryProbe:
                        count.ExistingDirectoryProbeCount++;
                        break;
                    case ObservedInputType.FileContentRead:
                        count.FileContentReadCount++;
                        break;
                    case ObservedInputType.ExistingFileProbe:
                        count.ExistingFileProbeCount++;
                        break;
                    default:
                        throw new NotImplementedException("Unrecognized Observed Input Type");
                }
            }

            return count;
        }

        /// <inheritdoc />
        public override int Analyze()
        {
            // Do nothing
            return 0;
        }
    }
}
