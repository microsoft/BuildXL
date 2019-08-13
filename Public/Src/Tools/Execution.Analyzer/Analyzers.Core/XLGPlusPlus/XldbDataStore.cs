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
using System.Linq;
using BuildXL.Pips.Operations;
using System.Reflection;
using PipType = BuildXL.Execution.Analyzer.Xldb.PipType;
using System.Collections;

namespace BuildXL.Analyzers.Core.XLGPlusPlus
{
    public sealed class XldbDataStore : IDisposable
    {
        /// <summary>
        /// Rocks DB Accessor for XLG++ data
        /// </summary>
        private KeyValueStoreAccessor Accessor { get; set; }
        private Dictionary<ExecutionEventId, MessageParser> m_eventParserDictionary = new Dictionary<ExecutionEventId, MessageParser>();
        private Dictionary<PipType, MessageParser> m_pipParserDictionary = new Dictionary<PipType, MessageParser>();
        public const string EventCountKey = "EventCount";

        public const string EventColumnFamilyName = "Event";
        public const string PipColumnFamilyName = "Pip";
        public const string StaticGraphColumnFamilyName = "StaticGraph";

        /// <summary>
        /// Open the datastore and populate the KeyValueStoreAccessor for the XLG++ DB
        /// </summary>
        public XldbDataStore(string storeDirectory,
            bool defaultColumnKeyTracked = false,
            IEnumerable<string> additionalKeyTrackedColumns = null,
            Action<Failure> failureHandler = null,
            bool openReadOnly = true,
            bool dropMismatchingColumns = false,
            bool onFailureDeleteExistingStoreAndRetry = false)
        {
            var accessor = KeyValueStoreAccessor.Open(storeDirectory,
               defaultColumnKeyTracked,
               new string[]  { EventColumnFamilyName, PipColumnFamilyName, StaticGraphColumnFamilyName },
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

            m_pipParserDictionary.Add(PipType.CopyFile, Execution.Analyzer.Xldb.CopyFile.Parser);
            m_pipParserDictionary.Add(PipType.WriteFile, Execution.Analyzer.Xldb.WriteFile.Parser);
            m_pipParserDictionary.Add(PipType.Process, ProcessPip.Parser);
            m_pipParserDictionary.Add(PipType.Module, Execution.Analyzer.Xldb.ModulePip.Parser);

        }

        /// <summary>
        /// Gets all the events of a certain type from the DB
        /// </summary>
        /// <returns>List of events, empty if no such event exists</returns>
        private IEnumerable<IMessage> GetEventsByType(ExecutionEventId eventTypeID)
        {
            Contract.Assert(Accessor != null, "XldbStore must be initialized via OpenDatastore first");

            var storedEvents = new List<IMessage>();
            var eventQuery = new EventTypeQuery
            {
                EventTypeID = eventTypeID,
            };

            var maybeFound = Accessor.Use(database =>
            {
                foreach (var kvp in database.PrefixSearch(eventQuery.ToByteArray(), EventColumnFamilyName))
                {
                    if (m_eventParserDictionary.TryGetValue(eventTypeID, out var parser))
                    {
                        storedEvents.Add(parser.ParseFrom(kvp.Value));
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

        public IMessage GetEventByKey(ExecutionEventId eventTypeID, uint pipID)
        {
            Contract.Assert(Accessor != null, "XldbStore must be initialized via OpenDatastore first");
            
            var eventQuery = new EventTypeQuery
            {
                EventTypeID = eventTypeID,
                PipId = pipID
            };

            var maybeFound = Accessor.Use(database =>
            {
                foreach (var kvp in database.PrefixSearch(eventQuery.ToByteArray(), EventColumnFamilyName))
                {
                    if (m_eventParserDictionary.TryGetValue(eventTypeID, out var parser))
                    {
                        return parser.ParseFrom(kvp.Value);
                    }
                }
                return null;
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            return maybeFound.Result;
        }

        /// <summary>
        /// Returns the count and payload of events by the event type
        /// </summary>
        /// <returns>EventCountsByTypeValue if exists, null otherwise</returns>
        public EventCountByTypeValue GetCountByEvent(ExecutionEventId eventTypeID)
        {
            Contract.Assert(Accessor != null, "XldbStore must be initialized via OpenDatastore first");

            var eventCountQuery = new EventCountByTypeKey
            {
                EventTypeID = eventTypeID,
            };

            var maybeFound = Accessor.Use(database =>
            {
                if (database.TryGetValue(eventCountQuery.ToByteArray(), out var eventCountObj))
                {
                    return EventCountByTypeValue.Parser.ParseFrom(eventCountObj);
                }
                return null;
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            return maybeFound.Result;
        }

        /// <summary>
        /// Gets the total number of stored xlg events in the database, 0 if the accessor was unsuccesful.
        /// </summary>
        public uint GetTotalEventCount()
        {
            Contract.Assert(Accessor != null, "XldbStore must be initialized via OpenDatastore first");

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
        /// Gets all the File Artifact Content Decided Events
        /// </summary>
        public IEnumerable<FileArtifactContentDecidedEvent> GetFileArtifactContentDecidedEvents() => GetEventsByType(ExecutionEventId.FileArtifactContentDecided).Cast<FileArtifactContentDecidedEvent>();

        /// <summary>
        /// Gets all the Worker List Events
        /// </summary>
        public IEnumerable<WorkerListEvent> GetWorkerListEvents() => GetEventsByType(ExecutionEventId.WorkerList).Cast<WorkerListEvent>();

        /// <summary>
        /// Gets all the Pip Execution Performance Events
        /// </summary>
        public IEnumerable<PipExecutionPerformanceEvent> GetPipExecutionPerformanceEvents() => GetEventsByType(ExecutionEventId.PipExecutionPerformance).Cast<PipExecutionPerformanceEvent>();

        /// <summary>
        /// Gets all the Directory Membership Hashed Events
        /// </summary>
        public IEnumerable<DirectoryMembershipHashedEvent> GetDirectoryMembershipHashedEvents() => GetEventsByType(ExecutionEventId.DirectoryMembershipHashed).Cast<DirectoryMembershipHashedEvent>();

        /// <summary>
        /// Gets all the Process Execution Monitoring Reported Events
        /// </summary>
        public IEnumerable<ProcessExecutionMonitoringReportedEvent> GetProcessExecutionMonitoringReportedEvents() => GetEventsByType(ExecutionEventId.ProcessExecutionMonitoringReported).Cast<ProcessExecutionMonitoringReportedEvent>();

        /// <summary>
        /// Gets all the Process Execution Monitoring Reported Events
        /// </summary>
        public IEnumerable<ProcessFingerprintComputationEvent> GetProcessFingerprintComputationEvents() => GetEventsByType(ExecutionEventId.ProcessFingerprintComputation).Cast<ProcessFingerprintComputationEvent>();

        /// <summary>
        /// Gets all the Extra Event Data Reported Events
        /// </summary>
        public IEnumerable<ExtraEventDataReported> GetExtraEventDataReportedEvents() => GetEventsByType(ExecutionEventId.ExtraEventDataReported).Cast<ExtraEventDataReported>();

        /// <summary>
        /// Gets all the Dependency Violation Reported Events
        /// </summary>
        public IEnumerable<DependencyViolationReportedEvent> GetDependencyViolationReportedEvents() => GetEventsByType(ExecutionEventId.DependencyViolationReported).Cast< DependencyViolationReportedEvent>();

        /// <summary>
        /// Gets all the Pip Execution Step Performance Reported Events
        /// </summary>
        public IEnumerable<PipExecutionStepPerformanceReportedEvent> GetPipExecutionStepPerformanceReportedEvents() => GetEventsByType(ExecutionEventId.PipExecutionStepPerformanceReported).Cast< PipExecutionStepPerformanceReportedEvent>();

        /// <summary>
        /// Gets all the Status Reported Events
        /// </summary>
        public IEnumerable<StatusReportedEvent> GetStatusReportedEvents() => GetEventsByType(ExecutionEventId.ResourceUsageReported).Cast< StatusReportedEvent>();

        /// <summary>
        /// Gets all the Pip Cache Miss Events
        /// </summary>
        public IEnumerable<PipCacheMissEvent> GetPipCacheMissEvents() => GetEventsByType(ExecutionEventId.PipCacheMiss).Cast< PipCacheMissEvent>();

        /// <summary>
        /// Gets all the BXL Invocation Events
        /// </summary>
        public IEnumerable<BXLInvocationEvent> GetBXLInvocationEvents() => GetEventsByType(ExecutionEventId.BxlInvocation).Cast< BXLInvocationEvent>();

        /// <summary>
        /// Gets all the Pip Execution Directory Outputs Events
        /// </summary>
        public IEnumerable<PipExecutionDirectoryOutputsEvent> GetPipExecutionDirectoryOutputsEvents() => GetEventsByType(ExecutionEventId.PipExecutionDirectoryOutputs).Cast< PipExecutionDirectoryOutputsEvent>();

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IMessage GetPipBySemiStableHash (long semiStableHash, out PipType pipType)
        {
            Contract.Assert(Accessor != null, "XldbStore must be initialized via OpenDatastore first");

            IMessage foundPip = null;

            var pipQuery = new PipQuery()
            {
                SemiStableHash = semiStableHash
            };

            var outPipType = (PipType) 0;

            var maybeFound = Accessor.Use(database =>
            {
                foreach (var kvp in database.PrefixSearch(pipQuery.ToByteArray(), PipColumnFamilyName))
                {
                    var pipKey = PipQuery.Parser.ParseFrom(kvp.Key);
                    if (m_pipParserDictionary.TryGetValue(pipKey.PipType, out var parser))
                    {
                        foundPip = parser.ParseFrom(kvp.Value);
                        outPipType = pipKey.PipType;
                    }
                }
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            pipType = outPipType;
            return foundPip;
        }

        ///// <summary>
        ///// Get all pips of a certain pip type
        ///// TODO: This may not actually work :( 
        ///// </summary>
        ///// <returns>List of pips of a certain type. If no such pips are found, returns empty list</returns>
        //public IEnumerable<IMessage> GetPipsOfType(PipType pipType)
        //{
            //Contract.Assert(Accessor != null, "XldbStore must be initialized via OpenDatastore first");
        //    var storedPips = new List<IMessage>();
        //    var pipQuery = new PipQuery()
        //    {
        //        PipType = pipType
        //    };

        //    var maybeFound = Accessor.Use(database =>
        //    {
        //        foreach (var kvp in database.PrefixSearch(pipQuery.ToByteArray(), PipColumnFamilyName))
        //        {
        //            var pipKey = PipQuery.Parser.ParseFrom(kvp.Key);
        //            if (m_pipParserDictionary.TryGetValue(pipKey.PipType, out var parser))
        //            {
        //                storedPips.Add(parser.ParseFrom(kvp.Value));
        //            }
        //            else
        //            {
        //                // We will never reach here since this is a private method and we explicitly control which ExecutionEventIDs are passed in (ie. the public facing helper methods below)
        //                _ = Contract.AssertFailure("Invalid Pip Type passed in. Exiting");
        //            }
        //        }
        //    });

        //    if (!maybeFound.Succeeded)
        //    {
        //        maybeFound.Failure.Throw();
        //    }

        //    return storedPips;
        //}

        /// <summary>
        /// Closes the connection to the DB
        /// </summary>
        public void Dispose()
        {
            Accessor.Dispose();
        }
    }
}
