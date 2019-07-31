// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Execution.Analyzer.Xldb;
using BuildXL.Utilities;
using Google.Protobuf;

namespace BuildXL.Analyzers.Core.XLGPlusPlus
{
    public sealed class XldbDataStore : IDisposable
    {
        /// <summary>
        /// Rocks DB Accessor for XLG++ data
        /// </summary>
        private KeyValueStoreAccessor Accessor { get; set; }

        /// <summary>
        /// Open the datastore and populate the KeyValueStoreAccessor for the XLG++ DB
        /// </summary>
        public XldbDataStore(string storeDirectory,
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
        public IEnumerable<string> GetEventsByType(ExecutionEventId eventTypeID)
        {
            Contract.Assert(Accessor != null, "XldbStore must be initialized via OpenDatastore first");

            var storedEvents = new List<string>();
            var eventQuery = new EventTypeQuery
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
                            case ExecutionEventId.FileArtifactContentDecided:
                                storedEvents.Add(FileArtifactContentDecidedEvent.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId.WorkerList:
                                storedEvents.Add(WorkerListEvent.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId.PipExecutionPerformance:
                                storedEvents.Add(PipExecutionPerformanceEvent.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId.DirectoryMembershipHashed:
                                storedEvents.Add(DirectoryMembershipHashedEvent.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId.ProcessExecutionMonitoringReported:
                                storedEvents.Add(ProcessExecutionMonitoringReportedEvent.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId.ProcessFingerprintComputation:
                                storedEvents.Add(ProcessFingerprintComputationEvent.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId.ExtraEventDataReported:
                                storedEvents.Add(ExtraEventDataReported.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId.DependencyViolationReported:
                                storedEvents.Add(DependencyViolationReportedEvent.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId.PipExecutionStepPerformanceReported:
                                storedEvents.Add(PipExecutionStepPerformanceReportedEvent.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId.PipCacheMiss:
                                storedEvents.Add(PipCacheMissEvent.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId.ResourceUsageReported:
                                storedEvents.Add(StatusReportedEvent.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId.DominoInvocation:
                                storedEvents.Add(BXLInvocationEvent.Parser.ParseFrom(kvp.Value).ToString());
                                break;
                            case ExecutionEventId.PipExecutionDirectoryOutputs:
                                storedEvents.Add(PipExecutionDirectoryOutputsEvent.Parser.ParseFrom(kvp.Value).ToString());
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
        public IEnumerable<string> GetFileArtifactContentDecidedEvents() => GetEventsByType(ExecutionEventId.FileArtifactContentDecided);

        /// <summary>
        /// Gets all the Worker List Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetWorkerListEvents() => GetEventsByType(ExecutionEventId.WorkerList);

        /// <summary>
        /// Gets all the Pip Execution Performance Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetPipExecutionPerformanceEvents() => GetEventsByType(ExecutionEventId.PipExecutionPerformance);

        /// <summary>
        /// Gets all the Directory Membership Hashed Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetDirectoryMembershipHashedEvents() => GetEventsByType(ExecutionEventId.DirectoryMembershipHashed);

        /// <summary>
        /// Gets all the Process Execution Monitoring Reported Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetProcessExecutionMonitoringReportedEvents() => GetEventsByType(ExecutionEventId.ProcessExecutionMonitoringReported);

        /// <summary>
        /// Gets all the Extra Event Data Reported Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetExtraEventDataReportedEvents() => GetEventsByType(ExecutionEventId.ExtraEventDataReported);

        /// <summary>
        /// Gets all the Dependency Violation Reported Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetDependencyViolationReportedEvents() => GetEventsByType(ExecutionEventId.DependencyViolationReported);

        /// <summary>
        /// Gets all the Pip Execution Step Performance Reported Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetPipExecutionStepPerformanceReportedEvents() => GetEventsByType(ExecutionEventId.PipExecutionStepPerformanceReported);

        /// <summary>
        /// Gets all the Status Reported Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetStatusReportedEvents() => GetEventsByType(ExecutionEventId.ResourceUsageReported);

        /// <summary>
        /// Gets all the Pip Cache Miss Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetPipCacheMissEvents() => GetEventsByType(ExecutionEventId.PipCacheMiss);

        /// <summary>
        /// Gets all the BXL Invocation Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetBXLInvocationEvents() => GetEventsByType(ExecutionEventId.DominoInvocation);

        /// <summary>
        /// Gets all the Pip Execution Directory Outputs Events
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetPipExecutionDirectoryOutputsEvents() => GetEventsByType(ExecutionEventId.PipExecutionDirectoryOutputs);


        /// <summary>
        /// Closes the connection to the DB
        /// </summary>
        public void Dispose()
        {
            Accessor.Dispose();
        }

        /// <summary>
        /// Method to test if the appropriate things have been stored in DB.
        /// NOTE: For internal testing/Debugging only!
        /// </summary>
        /// <returns>Stored data in string format</returns>
        public string GetStoredData()
        {
            Contract.Assert(Accessor != null, "XldbStore must be initialized via OpenDatastore first");

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
