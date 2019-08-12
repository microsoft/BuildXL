// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Text;
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
        private Dictionary<ExecutionEventId, MessageParser> m_eventParserDictionary = new Dictionary<ExecutionEventId, MessageParser>();
        public const string EventCountKey = "EventCount";

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

            m_eventParserDictionary.Add(ExecutionEventId.FileArtifactContentDecided, FileArtifactContentDecidedEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.WorkerList, WorkerListEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.PipExecutionPerformance, PipExecutionPerformanceEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.DirectoryMembershipHashed, DirectoryMembershipHashedEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.ProcessExecutionMonitoringReported, ProcessExecutionMonitoringReportedEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.ProcessFingerprintComputation, ProcessFingerprintComputationEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.ExtraEventDataReported, ExtraEventDataReported.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.DependencyViolationReported, DependencyViolationReportedEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.PipExecutionStepPerformanceReported, PipExecutionStepPerformanceReportedEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.PipCacheMiss, PipCacheMissEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.ResourceUsageReported, StatusReportedEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.BxlInvocation, BXLInvocationEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.PipExecutionDirectoryOutputs, PipExecutionDirectoryOutputsEvent.Parser);
        }

        /// <summary>
        /// Gets all the events of a certain type from the DB
        /// </summary>
        private IEnumerable<string> GetEventsByType(ExecutionEventId eventTypeID)
        {
            Contract.Assert(Accessor != null, "XldbStore must be initialized via OpenDatastore first");

            var storedEvents = new List<string>();
            var eventQuery = new EventTypeQuery
            {
                EventTypeID = eventTypeID,
            };

            var maybeFound = Accessor.Use(database =>
            {
                foreach (var kvp in database.PrefixSearch(eventQuery.ToByteArray()))
                {
                    if (m_eventParserDictionary.TryGetValue(eventTypeID, out var parser))
                    {
                        storedEvents.Add(parser.ParseFrom(kvp.Value).ToString());
                    }
                    else
                    {
                        // We will never reach here since this is a private method and we explicitly control which ExecutionEventIDs are passed in (ie. the public facing helper methods below)
                        _ = Contract.AssertFailure("Invalid Execution Event ID passed in. Exiting");
                    }
                }
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            return storedEvents;
        }

        /// <summary>
        /// Gets all the File Artifact Content Decided Events
        /// </summary>
        public IEnumerable<string> GetFileArtifactContentDecidedEvents() => GetEventsByType(ExecutionEventId.FileArtifactContentDecided);

        /// <summary>
        /// Gets all the Worker List Events
        /// </summary>
        public IEnumerable<string> GetWorkerListEvents() => GetEventsByType(ExecutionEventId.WorkerList);

        /// <summary>
        /// Gets all the Pip Execution Performance Events
        /// </summary>
        public IEnumerable<string> GetPipExecutionPerformanceEvents() => GetEventsByType(ExecutionEventId.PipExecutionPerformance);

        /// <summary>
        /// Gets all the Directory Membership Hashed Events
        /// </summary>
        public IEnumerable<string> GetDirectoryMembershipHashedEvents() => GetEventsByType(ExecutionEventId.DirectoryMembershipHashed);

        /// <summary>
        /// Gets all the Process Execution Monitoring Reported Events
        /// </summary>
        public IEnumerable<string> GetProcessExecutionMonitoringReportedEvents() => GetEventsByType(ExecutionEventId.ProcessExecutionMonitoringReported);

        /// <summary>
        /// Gets all the Process Execution Monitoring Reported Events
        /// </summary>
        public IEnumerable<string> GetProcessFingerprintComputationEvents() => GetEventsByType(ExecutionEventId.ProcessFingerprintComputation);

        /// <summary>
        /// Gets all the Extra Event Data Reported Events
        /// </summary>
        public IEnumerable<string> GetExtraEventDataReportedEvents() => GetEventsByType(ExecutionEventId.ExtraEventDataReported);

        /// <summary>
        /// Gets all the Dependency Violation Reported Events
        /// </summary>
        public IEnumerable<string> GetDependencyViolationReportedEvents() => GetEventsByType(ExecutionEventId.DependencyViolationReported);

        /// <summary>
        /// Gets all the Pip Execution Step Performance Reported Events
        /// </summary>
        public IEnumerable<string> GetPipExecutionStepPerformanceReportedEvents() => GetEventsByType(ExecutionEventId.PipExecutionStepPerformanceReported);

        /// <summary>
        /// Gets all the Status Reported Events
        /// </summary>
        public IEnumerable<string> GetStatusReportedEvents() => GetEventsByType(ExecutionEventId.ResourceUsageReported);

        /// <summary>
        /// Gets all the Pip Cache Miss Events
        /// </summary>
        public IEnumerable<string> GetPipCacheMissEvents() => GetEventsByType(ExecutionEventId.PipCacheMiss);

        /// <summary>
        /// Gets all the BXL Invocation Events
        /// </summary>
        public IEnumerable<string> GetBXLInvocationEvents() => GetEventsByType(ExecutionEventId.BxlInvocation);

        /// <summary>
        /// Gets all the Pip Execution Directory Outputs Events
        /// </summary>
        public IEnumerable<string> GetPipExecutionDirectoryOutputsEvents() => GetEventsByType(ExecutionEventId.PipExecutionDirectoryOutputs);

        /// <summary>
        /// Gets the total number of stored xlg events in the database, 0 if the accessor was unsuccesful.
        /// </summary>
        public uint GetEventCount()
        {
            var maybeFound = Accessor.Use(database =>
            {
                database.TryGetValue(Encoding.ASCII.GetBytes(EventCountKey), out var eventCountObj);
                return EventCount.Parser.ParseFrom(eventCountObj).Value;
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            return maybeFound.Result;
        }

        /// <summary>
        /// Closes the connection to the DB
        /// </summary>
        public void Dispose()
        {
            Accessor.Dispose();
        }
    }
}
