// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Utilities;
using System.Collections.Concurrent;
using BuildXL.Pips;
using BuildXL.Scheduler.Distribution;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using BuildXL.Scheduler;
using BuildXL.Pips.Operations;

namespace BuildXL.Engine.Distribution
{
    public sealed partial class WorkerNotificationManager
    {
        /// <summary>
        /// Event listener for pip execution results which will be forwarded to the orchestrator
        /// </summary>
        private sealed class PipResultListener
        {
            #region Writer Pool

            private readonly ObjectPool<BuildXLWriter> m_writerPool = new ObjectPool<BuildXLWriter>(CreateWriter, (Action<BuildXLWriter>)CleanupWriter);

            private static void CleanupWriter(BuildXLWriter writer)
            {
                writer.BaseStream.SetLength(0);
            }

            [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Disposal is not needed for memory stream")]
            private static BuildXLWriter CreateWriter()
            {
                return new BuildXLWriter(
                    debug: false,
                    stream: new MemoryStream(),
                    leaveOpen: false,
                    logStats: false);
            }

            #endregion Writer Pool

            private readonly WorkerNotificationManager m_notificationManager;
            private readonly ExecutionResultSerializer m_resultSerializer;
            private readonly PipTable m_pipTable;
            private readonly IPipExecutionEnvironment m_environment;

            /// <summary>
            /// Ready to send pip results are queued here and are consumed by <see cref="WorkerNotificationManager.SendNotifications(CancellationToken)" />
            /// </summary>
            internal readonly BlockingCollection<ExtendedPipCompletionData> ReadyToSendResultList = new BlockingCollection<ExtendedPipCompletionData>();

            private DistributionServices DistributionServices => m_notificationManager.DistributionServices;

            public void ReportResult(ExtendedPipCompletionData pipCompletion)
            {
                try
                {
                    using (DistributionServices.Counters.StartStopwatch(DistributionCounter.WorkerServiceResultSerializationDuration))
                    {
                        SerializeExecutionResult(pipCompletion);
                    }
                    ReadyToSendResultList.Add(pipCompletion);
                }
                catch (InvalidOperationException)
                {
                    // ReadyToSendResultList is already marked as completed (due to cancellation or early exit)
                    // No need to report the other results as the build already failed or the orchestrator doesn't
                    // care about the results (this is the case with early release).
                }
            }

            public PipResultListener(WorkerNotificationManager notificationManager, EngineSchedule schedule, IPipExecutionEnvironment environment)
            {
                m_notificationManager = notificationManager;
                m_resultSerializer = new ExecutionResultSerializer(maxSerializableAbsolutePathIndex: schedule.MaxSerializedAbsolutePath, executionContext: schedule.Scheduler.Context);
                m_pipTable = schedule.PipTable;
                m_environment = environment;
            }

            internal void Cancel()
            {
                ReadyToSendResultList.CompleteAdding();
            }

            private void SerializeExecutionResult(ExtendedPipCompletionData completionData)
            {
                using (var pooledWriter = m_writerPool.GetInstance())
                {
                    var writer = pooledWriter.Instance;
                    PipId pipId = completionData.PipId;

                    m_resultSerializer.Serialize(writer, completionData.ExecutionResult, completionData.PreservePathSetCasing);

                    // TODO: ToArray is expensive here. Think about alternatives.
                    var dataByte = ((MemoryStream)writer.BaseStream).ToArray();
                    completionData.SerializedData.ResultBlob = new ArraySegment<byte>(dataByte);
                    m_notificationManager.WorkerService.ReportingPipToMaster(completionData);
                    m_environment.Counters.AddToCounter(m_pipTable.GetPipType(pipId) == PipType.Process ? PipExecutorCounter.ProcessExecutionResultSize : PipExecutorCounter.IpcExecutionResultSize, dataByte.Length);
                }
            }
        }
    }
}
