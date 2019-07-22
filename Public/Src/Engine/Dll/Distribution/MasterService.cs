// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Distribution.OpenBond;
using BuildXL.Engine.Tracing;
using BuildXL.Pips;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Distribution;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// A pip executor which can distributed work to remote workers
    /// </summary>
    public sealed class MasterService : IDistributionService
    {
        internal IPipExecutionEnvironment Environment
        {
            get
            {
                Contract.Assert(m_environment != null, "Distribution must be enabled to access pip graph");
                return m_environment;
            }
        }

        internal ExecutionResultSerializer ResultSerializer
        {
            get
            {
                Contract.Assert(m_resultSerializer != null);
                return m_resultSerializer;
            }
        }

        private readonly RemoteWorker[] m_remoteWorkers;
        private readonly LoggingContext m_loggingContext;

        private IPipExecutionEnvironment m_environment;
        private ExecutionResultSerializer m_resultSerializer;
        private PipGraphCacheDescriptor m_cachedGraphDescriptor;
        private readonly ushort m_buildServicePort;
        private readonly bool m_isGrpcEnabled;

        private readonly IServer m_masterServer;

        internal readonly DistributionServices DistributionServices;

        /// <summary>
        /// Class constructor
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "RemoteWorker disposes the workerClient")]
        public MasterService(IDistributionConfiguration config, LoggingContext loggingContext, string buildId)
        {
            Contract.Requires(config != null && config.BuildRole == DistributedBuildRoles.Master);
            Contract.Ensures(m_remoteWorkers != null);

            m_isGrpcEnabled = config.IsGrpcEnabled;

            // Create all remote workers
            m_buildServicePort = config.BuildServicePort;
            m_remoteWorkers = new RemoteWorker[config.BuildWorkers.Count];

            m_loggingContext = loggingContext;
            DistributionServices = new DistributionServices(buildId);

            for (int i = 0; i < m_remoteWorkers.Length; i++)
            {
                var configWorker = config.BuildWorkers[i];
                var workerId = i + 1; // 0 represents the local worker.
                var serviceLocation = new ServiceLocation { IpAddress = configWorker.IpAddress, Port = configWorker.BuildServicePort };
                m_remoteWorkers[i] = new RemoteWorker(m_isGrpcEnabled, loggingContext, (uint)workerId, this, serviceLocation);
            }

            if (m_isGrpcEnabled)
            {
                m_masterServer = new Grpc.GrpcMasterServer(loggingContext, this, buildId);
            }
            else
            {
#if !DISABLE_FEATURE_BOND_RPC
                m_masterServer = new InternalBond.BondMasterServer(loggingContext, this);
#endif
            }
        }

        /// <summary>
        /// The port on which the master is listening.
        /// </summary>
        public int Port => m_buildServicePort;

        /// <summary>
        /// The descriptor for the cached graph
        /// </summary>
        public PipGraphCacheDescriptor CachedGraphDescriptor
        {
            get
            {
                return m_cachedGraphDescriptor;
            }

            set
            {
                Contract.Requires(value != null);
                m_cachedGraphDescriptor = value;
            }
        }

        /// <summary>
        /// Content hash of symlink file.
        /// </summary>
        public ContentHash SymlinkFileContentHash { get; set; } = WellKnownContentHashes.AbsentFile;

        /// <summary>
        /// Prepares the master for pips execution
        /// </summary>
        public void EnableDistribution(EngineSchedule schedule)
        {
            Contract.Requires(schedule != null);

            m_environment = schedule.Scheduler;

            schedule.Scheduler.EnableDistribution(m_remoteWorkers);
            m_resultSerializer = new ExecutionResultSerializer(schedule.MaxSerializedAbsolutePath, m_environment.Context);
        }

        /// <summary>
        /// Completes the attachment of a worker.
        /// </summary>
        public void AttachCompleted(AttachCompletionInfo attachCompletionInfo)
        {
            var worker = GetWorkerById(attachCompletionInfo.WorkerId);
            worker.AttachCompletedAsync(attachCompletionInfo);
        }

        /// <summary>
        /// Handler for the 'work completion' notification from worker.
        /// </summary>
        [SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid", Justification = "This is eventhandler so fire&forget is understandable")]
        public async void ReceivedWorkerNotificationAsync(WorkerNotificationArgs notification)
        {
            var worker = GetWorkerById(notification.WorkerId);

            if (notification.ExecutionLogData != null && notification.ExecutionLogData.Count != 0)
            {
                // The channel is unblocked and ACK is sent after we put the execution blob to the queue in 'LogExecutionBlobAsync' method.
                await worker.LogExecutionBlobAsync(notification);
            }

            // Return immediately to unblock the channel so that worker can receive the ACK for the sent message
            await Task.Yield();

            foreach (var forwardedEvent in notification.ForwardedEvents)
            {
                EventLevel eventLevel = (EventLevel)forwardedEvent.Level;
                switch (eventLevel)
                {
                    case EventLevel.Error:
                        Logger.Log.DistributionWorkerForwardedError(
                            m_loggingContext,
                            new WorkerForwardedEvent()
                            {
                                Text = forwardedEvent.Text,
                                WorkerName = worker.Name,
                                EventId = forwardedEvent.EventId,
                                EventName = forwardedEvent.EventName,
                                EventKeywords = forwardedEvent.EventKeywords,
                            });
                        break;
                    case EventLevel.Warning:
                        Logger.Log.DistributionWorkerForwardedWarning(
                            m_loggingContext,
                            new WorkerForwardedEvent()
                            {
                                Text = forwardedEvent.Text,
                                WorkerName = worker.Name,
                                EventId = forwardedEvent.EventId,
                                EventName = forwardedEvent.EventName,
                                EventKeywords = forwardedEvent.EventKeywords,
                            });
                        break;
                    default:
                        break;
                }
            }

            foreach (PipCompletionData completedPip in notification.CompletedPips)
            {
                worker.NotifyPipCompletion(completedPip);
            }
        }

        private RemoteWorker GetWorkerById(uint id)
        {
            // Because 0 represents the local worker node, we need to substract 1 from the id.
            // We only store the remote workers in this class.
            return m_remoteWorkers[id - 1];
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            DistributionServices.LogStatistics(m_loggingContext);

            m_masterServer.Dispose();

            if (m_remoteWorkers != null)
            {
                foreach (Worker worker in m_remoteWorkers)
                {
                    worker.Dispose();
                }
            }
        }

        bool IDistributionService.Initialize()
        {
            // Start listening to the port if we have remote workers
            if (m_remoteWorkers.Length > 0)
            {
                try
                {
                    m_masterServer.Start(m_buildServicePort);
                }
                catch (Exception ex)
                {
                    Logger.Log.DistributionServiceInitializationError(m_loggingContext, DistributedBuildRole.Master.ToString(), m_buildServicePort, ExceptionUtilities.GetLogEventMessage(ex));
                    return false;
                }
            }

            return true;
        }
    }
}
