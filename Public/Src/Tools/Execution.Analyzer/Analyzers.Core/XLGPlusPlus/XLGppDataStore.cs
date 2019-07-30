// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Execution.Analyzer;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using Google.Protobuf;

namespace BuildXL.Analyzers.Core.XLGPlusPlus
{
    public class XLGppDataStore: IDisposable
    {
        /// <summary>
        /// Rocks DB Accessor for XLG++ data
        /// </summary>
        private KeyValueStoreAccessor Accessor { get; set; }

        /// <summary>
        /// Open the datastore and populate the KeyValueStoreAccessor for the XLG++ DB
        /// </summary>
        public XLGppDataStore(string storeDirectory,
            bool defaultColumnKeyTracked = false,
            IEnumerable<string> additionalColumns = null,
            IEnumerable<string> additionalKeyTrackedColumns = null,
            Action<Failure> failureHandler = null,
            bool openReadOnly = false,
            bool dropMismatchingColumns = false,
            bool onFailureDeleteExistingStoreAndRetry = false)
        {
            var accessor = KeyValueStoreAccessor.Open(storeDirectory,
               defaultColumnKeyTracked,
               additionalColumns,
               additionalKeyTrackedColumns,
               failureHandler,
               openReadOnly,
               dropMismatchingColumns,
               onFailureDeleteExistingStoreAndRetry);

            if (accessor.Succeeded)
            {
                Accessor = accessor.Result;
            }
            else
            {
                Accessor = null;
                Console.Error.WriteLine("Could not create an accessor for RocksDB. Accessor is null");
            }
        }

        /// <summary>
        /// Gets all the events of a certain type from the DB
        /// </summary>
        /// <returns>List of event objects recovered from DB </returns>
        public IEnumerable<string> GetEventsByType_V0(ExecutionEventId_XLGpp eventTypeID)
        {
            Contract.Assert(Accessor != null, "XLGppStore must be initialized via OpenDatastore first");

            var storedEvents = new List<string>();
            var eventQuery = new EventTypeQuery_XLGpp
            {
                EventTypeID = eventTypeID,
            };
            Analysis.IgnoreResult(
                Accessor.Use(database =>
                {
                    foreach (var kvp in database.PrefixSearch(eventQuery.ToByteArray()))
                    {
                        switch (eventTypeID)
                        {
                            case ExecutionEventId_XLGpp.FileArtifactContentDecided:
                                storedEvents.Add(FileArtifactContentDecidedEvent_XLGpp.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId_XLGpp.WorkerList:
                                storedEvents.Add(WorkerListEvent_XLGpp.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId_XLGpp.PipExecutionPerformance:
                                storedEvents.Add(PipExecutionPerformanceEvent_XLGpp.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId_XLGpp.DirectoryMembershipHashed:
                                storedEvents.Add(DirectoryMembershipHashedEvent_XLGpp.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId_XLGpp.ProcessExecutionMonitoringReported:
                                storedEvents.Add(ProcessExecutionMonitoringReportedEvent_XLGpp.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId_XLGpp.ProcessFingerprintComputation:
                                storedEvents.Add(ProcessFingerprintComputationEvent_XLGpp.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId_XLGpp.ExtraEventDataReported:
                                storedEvents.Add(ExtraEventDataReported_XLGpp.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId_XLGpp.DependencyViolationReported:
                                storedEvents.Add(DependencyViolationReportedEvent_XLGpp.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId_XLGpp.PipExecutionStepPerformanceReported:
                                storedEvents.Add(PipExecutionStepPerformanceReportedEvent_XLGpp.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId_XLGpp.PipCacheMiss:
                                storedEvents.Add(PipCacheMissEvent_XLGpp.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId_XLGpp.ResourceUsageReported:
                                storedEvents.Add(StatusReportedEvent_XLGpp.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId_XLGpp.DominoInvocation:
                                storedEvents.Add(BXLInvocationEvent_XLGpp.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId_XLGpp.PipExecutionDirectoryOutputs:
                                storedEvents.Add(PipExecutionDirectoryOutputsEvent_XLGpp.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            default:
                                break;
                        }
                    }
                })
            );
            
            return storedEvents;
        }

        /// <summary>
        /// Gets all the File Artifact Content Decided Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetFileArtifactContentDecidedEvents_V0() => GetEventsByType_V0(ExecutionEventId_XLGpp.FileArtifactContentDecided);

        /// <summary>
        /// Gets all the Worker List Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetWorkerListEvents_V0() => GetEventsByType_V0(ExecutionEventId_XLGpp.WorkerList);

        /// <summary>
        /// Gets all the Pip Execution Performance Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetPipExecutionPerformanceEvents_V0() => GetEventsByType_V0(ExecutionEventId_XLGpp.PipExecutionPerformance);

        /// <summary>
        /// Gets all the Directory Membership Hashed Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetDirectoryMembershipHashedEvents_V0() => GetEventsByType_V0(ExecutionEventId_XLGpp.DirectoryMembershipHashed);

        /// <summary>
        /// Gets all the Process Execution Monitoring Reported Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetProcessExecutionMonitoringReportedEvents_V0() => GetEventsByType_V0(ExecutionEventId_XLGpp.ProcessExecutionMonitoringReported);

        /// <summary>
        /// Gets all the Extra Event Data Reported Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetExtraEventDataReportedEvents_V0() => GetEventsByType_V0(ExecutionEventId_XLGpp.ExtraEventDataReported);

        /// <summary>
        /// Gets all the Dependency Violation Reported Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetDependencyViolationReportedEvents_V0() => GetEventsByType_V0(ExecutionEventId_XLGpp.DependencyViolationReported);

        /// <summary>
        /// Gets all the Pip Execution Step Performance Reported Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetPipExecutionStepPerformanceReportedEvents_V0() => GetEventsByType_V0(ExecutionEventId_XLGpp.PipExecutionStepPerformanceReported);

        /// <summary>
        /// Gets all the Status Reported Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetStatusReportedEvents_V0() => GetEventsByType_V0(ExecutionEventId_XLGpp.ResourceUsageReported);

        /// <summary>
        /// Gets all the Pip Cache Miss Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetPipCacheMissEvents_V0() => GetEventsByType_V0(ExecutionEventId_XLGpp.PipCacheMiss);

        /// <summary>
        /// Gets all the BXL Invocation Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetBXLInvocationEvents_V0() => GetEventsByType_V0(ExecutionEventId_XLGpp.DominoInvocation);

        /// <summary>
        /// Gets all the Pip Execution Directory Outputs Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetPipExecutionDirectoryOutputsEvents_V0() => GetEventsByType_V0(ExecutionEventId_XLGpp.PipExecutionDirectoryOutputs);


        /// <summary>
        /// Closes the connection to the DB
        /// </summary>
        public void Dispose()
        {
            Contract.Assert(Accessor != null, "XLGppStore must be initialized via OpenDatastore first");

            Accessor.Dispose();
        }

        /// <summary>
        /// Method to test if the appropriate things have been stored in DB.
        /// NOTE: For internal testing/Debugging only!
        /// </summary>
        /// <returns>Stored data in string format</returns>
        public string GetStoredData_V0()
        {
            Contract.Assert(Accessor != null, "XLGppStore must be initialized via OpenDatastore first");

            string value = null;
            Analysis.IgnoreResult(
                Accessor.Use(database =>
                {
                    database.TryGetValue("", out value);
                    foreach (var kvp in database.PrefixSearch(""))
                    {
                        Console.WriteLine("The key is {0}, and the value is {1}", kvp.Key, kvp.Value);
                    }
                })
            );
            return value;
        }
    }
}
