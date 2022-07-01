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
    internal sealed partial class WorkerNotificationManager
    {
        /// <summary>
        /// Event listener for pip execution results which will be forwarded to the orchestrator
        /// </summary>
        private sealed class PipResultListener
        {
            private readonly WorkerNotificationManager m_notificationManager;
            private readonly IPipResultSerializer m_resultSerializer;

            /// <summary>
            /// Ready to send pip results are queued here and are consumed by <see cref="WorkerNotificationManager.SendNotifications(CancellationToken)" />
            /// </summary>
            internal readonly BlockingCollection<ExtendedPipCompletionData> ReadyToSendResultList = new BlockingCollection<ExtendedPipCompletionData>();

            private DistributionService DistributionService => m_notificationManager.DistributionService;

            public void ReportResult(ExtendedPipCompletionData pipCompletion)
            {
                try
                {
                    using (DistributionService.Counters.StartStopwatch(DistributionCounter.WorkerServiceResultSerializationDuration))
                    {
                        m_resultSerializer.SerializeExecutionResult(pipCompletion);
                        DistributionService.Counters.AddToCounter(pipCompletion.PipType == PipType.Process ? DistributionCounter.ProcessExecutionResultSize : DistributionCounter.IpcExecutionResultSize, pipCompletion.SerializedData.ResultBlob.Count);
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

            public PipResultListener(WorkerNotificationManager notificationManager, IPipResultSerializer serializer)
            {
                m_notificationManager = notificationManager;
                m_resultSerializer = serializer;
            }

            internal void Cancel()
            {
                ReadyToSendResultList.CompleteAdding();
            }
        }
    }
}
