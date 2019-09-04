// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Xldb.Proto;
using Google.Protobuf;

namespace BuildXL.Xldb
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
            bool openReadOnly = true,
            bool dropMismatchingColumns = false,
            bool onFailureDeleteExistingStoreAndRetry = false)
        {
            var accessor = KeyValueStoreAccessor.Open(storeDirectory,
               defaultColumnKeyTracked,
               new string[] { EventColumnFamilyName, PipColumnFamilyName, StaticGraphColumnFamilyName },
               additionalKeyTrackedColumns,
               failureHandler: null,
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
                Console.Error.WriteLine("Could not create an accessor for RocksDB. Accessor is null! " + accessor.Failure.DescribeIncludingInnerFailures());
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

            // We only store non-meta pips (and no HashSourceFile pips) into this database, so module, hash, value, and specfile are not included in the parser dictionary
            m_pipParserDictionary.Add(PipType.CopyFile, CopyFile.Parser);
            m_pipParserDictionary.Add(PipType.WriteFile, WriteFile.Parser);
            m_pipParserDictionary.Add(PipType.Process, ProcessPip.Parser);
            m_pipParserDictionary.Add(PipType.SealDirectory, SealDirectory.Parser);
            m_pipParserDictionary.Add(PipType.Ipc, IpcPip.Parser);
        }

        /// <summary>
        /// Gets all the events of a certain type from the DB
        /// </summary>
        /// <returns>List of events, empty if no such events exist</returns>
        private IEnumerable<IMessage> GetEventsByType(ExecutionEventId eventTypeID)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = eventTypeID,
            };

            return GetEventsByKey(eventKey);
        }

        /// <summary>
        /// Gets events from the DB based on the eventKey
        /// </summary>
        /// <returns>List of events, empty if no such events exist</returns>
        private IEnumerable<IMessage> GetEventsByKey(EventKey eventKey)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var storedEvents = new List<IMessage>();

            if (!m_eventParserDictionary.TryGetValue(eventKey.EventTypeID, out var parser))
            {
                Contract.Assert(false, "No parser found for EventTypeId passed in");
            }

            var maybeFound = Accessor.Use(database =>
            {
                foreach (var kvp in database.PrefixSearch(eventKey.ToByteArray(), EventColumnFamilyName))
                {
                    storedEvents.Add(parser.ParseFrom(kvp.Value));
                }
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            return storedEvents;
        }

        /// <summary>
        /// Gets a depdendency violated events by key
        /// </summary>
        public IEnumerable<DependencyViolationReportedEvent> GetDependencyViolatedEventByKey(uint violatorPipID, uint workerID = 0)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = ExecutionEventId.DependencyViolationReported,
                ViolatorPipID = violatorPipID,
                WorkerID = workerID
            };

            return GetEventsByKey(eventKey).Cast<DependencyViolationReportedEvent>();
        }

        /// <summary>
        /// Gets pip execution step performance events by key
        /// </summary>
        public IEnumerable<PipExecutionStepPerformanceReportedEvent> GetPipExecutionStepPerformanceEventByKey(uint pipID, PipExecutionStep pipExecutionStep = 0, uint workerID = 0)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = ExecutionEventId.PipExecutionStepPerformanceReported,
                WorkerID = workerID,
                PipId = pipID,
                PipExecutionStepPerformanceKey = pipExecutionStep
            };

            return GetEventsByKey(eventKey).Cast<PipExecutionStepPerformanceReportedEvent>();
        }

        /// <summary>
        /// Gets process fingerprint computation events by key
        /// </summary>
        public IEnumerable<ProcessFingerprintComputationEvent> GetProcessFingerprintComputationEventByKey(uint pipID, FingerprintComputationKind computationKind = 0, uint workerID = 0)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = ExecutionEventId.ProcessFingerprintComputation,
                WorkerID = workerID,
                PipId = pipID,
                ProcessFingerprintComputationKey = computationKind
            };

            return GetEventsByKey(eventKey).Cast<ProcessFingerprintComputationEvent>();
        }

        /// <summary>
        /// Gets directory membership hashed event by key
        /// </summary>
        public IEnumerable<DirectoryMembershipHashedEvent> GetDirectoryMembershipHashedEventByKey(uint pipID, string directoryPath = "", uint workerID = 0)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = ExecutionEventId.DirectoryMembershipHashed,
                WorkerID = workerID,
                PipId = pipID,
                DirectoryMembershipHashedKey = directoryPath
            };

            return GetEventsByKey(eventKey).Cast<DirectoryMembershipHashedEvent>();
        }

        /// <summary>
        /// Gets pip execution directory output event by key
        /// </summary>
        public IEnumerable<PipExecutionDirectoryOutputsEvent> GetPipExecutionDirectoryOutputEventByKey(uint pipID, string directoryPath = "", uint workerID = 0)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = ExecutionEventId.PipExecutionDirectoryOutputs,
                WorkerID = workerID,
                PipId = pipID,
                PipExecutionDirectoryOutputKey = directoryPath
            };

            return GetEventsByKey(eventKey).Cast<PipExecutionDirectoryOutputsEvent>();
        }

        /// <summary>
        /// Gets file artficat content decided event by key
        /// </summary>
        public IEnumerable<FileArtifactContentDecidedEvent> GetFileArtifactContentDecidedEventByKey(string directoryPath, int fileRewriteCount = 0, uint workerID = 0)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = ExecutionEventId.FileArtifactContentDecided,
                WorkerID = workerID,
                FileArtifactContentDecidedKey = directoryPath,
                FileRewriteCount = fileRewriteCount
            };

            return GetEventsByKey(eventKey).Cast<FileArtifactContentDecidedEvent>();
        }

        /// <summary>
        /// Gets pip execution performance events by key
        /// </summary>
        public IEnumerable<PipExecutionPerformanceEvent> GetPipExecutionPerformanceEventByKey(uint pipID, uint workerID = 0) => GetEventsByPipIdOnly(ExecutionEventId.PipExecutionPerformance, pipID, workerID).Cast<PipExecutionPerformanceEvent>();

        /// <summary>
        /// Gets process execution monitoring reported events by key
        /// </summary>
        public IEnumerable<ProcessExecutionMonitoringReportedEvent> GetProcessExecutionMonitoringReportedEventByKey(uint pipID, uint workerID = 0) => GetEventsByPipIdOnly(ExecutionEventId.ProcessExecutionMonitoringReported, pipID, workerID).Cast<ProcessExecutionMonitoringReportedEvent>();

        /// <summary>
        /// Gets pip cache miss events by key
        /// </summary>
        public IEnumerable<PipCacheMissEvent> GetPipCacheMissEventByKey(uint pipID, uint workerID = 0) => GetEventsByPipIdOnly(ExecutionEventId.PipCacheMiss, pipID, workerID).Cast<PipCacheMissEvent>();

        /// <summary>
        /// Get events that only use PipID as the key
        /// </summary>
        private IEnumerable<IMessage> GetEventsByPipIdOnly(ExecutionEventId eventTypeID, uint pipID, uint workerID = 0)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = eventTypeID,
                PipId = pipID,
                WorkerID = workerID
            };

            return GetEventsByKey(eventKey);
        }

        /// <summary>
        /// Returns the count and payload of events by the event type
        /// </summary>
        /// <returns>EventCountsByTypeValue if exists, null otherwise</returns>
        public EventCountByTypeValue GetCountByEvent(ExecutionEventId eventTypeID)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var eventCountKey = new EventCountByTypeKey
            {
                EventTypeID = eventTypeID,
            };

            var maybeFound = Accessor.Use(database =>
            {
                if (database.TryGetValue(eventCountKey.ToByteArray(), out var eventCountObj))
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
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

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
        public IEnumerable<DependencyViolationReportedEvent> GetDependencyViolationReportedEvents() => GetEventsByType(ExecutionEventId.DependencyViolationReported).Cast<DependencyViolationReportedEvent>();

        /// <summary>
        /// Gets all the Pip Execution Step Performance Reported Events
        /// </summary>
        public IEnumerable<PipExecutionStepPerformanceReportedEvent> GetPipExecutionStepPerformanceReportedEvents() => GetEventsByType(ExecutionEventId.PipExecutionStepPerformanceReported).Cast<PipExecutionStepPerformanceReportedEvent>();

        /// <summary>
        /// Gets all the Status Reported Events
        /// </summary>
        public IEnumerable<StatusReportedEvent> GetStatusReportedEvents() => GetEventsByType(ExecutionEventId.ResourceUsageReported).Cast<StatusReportedEvent>();

        /// <summary>
        /// Gets all the Pip Cache Miss Events
        /// </summary>
        public IEnumerable<PipCacheMissEvent> GetPipCacheMissEvents() => GetEventsByType(ExecutionEventId.PipCacheMiss).Cast<PipCacheMissEvent>();

        /// <summary>
        /// Gets all the BXL Invocation Events.
        /// </summary>
        public IEnumerable<BXLInvocationEvent> GetBXLInvocationEvents() => GetEventsByType(ExecutionEventId.BxlInvocation).Cast<BXLInvocationEvent>();

        /// <summary>
        /// Gets all the Pip Execution Directory Outputs Events
        /// </summary>
        public IEnumerable<PipExecutionDirectoryOutputsEvent> GetPipExecutionDirectoryOutputsEvents() => GetEventsByType(ExecutionEventId.PipExecutionDirectoryOutputs).Cast<PipExecutionDirectoryOutputsEvent>();

        /// <summary>
        /// Gets the pip stored based on the semistable hash
        /// </summary>
        /// <returns>Returns null if no such pip is found</returns>
        public IMessage GetPipBySemiStableHash(long semiStableHash, out PipType pipType)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            IMessage foundPip = null;

            var pipSemistableHashKey = new PipSemistableHashKey()
            {
                SemiStableHash = semiStableHash
            };

            PipType outPipType = 0;

            var maybeFound = Accessor.Use(database =>
            {
                if (database.TryGetValue(pipSemistableHashKey.ToByteArray(), out var pipValueSemistableHash, PipColumnFamilyName))
                {
                    foundPip = GetPipByPipId(PipIdValue.Parser.ParseFrom(pipValueSemistableHash).PipId, out outPipType);
                }
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            pipType = outPipType;
            return foundPip;
        }

        /// <summary>
        /// Gets the pip stored based on the pip id
        /// </summary>
        /// <returns>Returns null if no such pip is found</returns>
        public IMessage GetPipByPipId(uint pipId, out PipType pipType)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            IMessage foundPip = null;

            var pipIdKey = new PipIdKey()
            {
                PipId = pipId
            };

            PipType outPipType = 0;

            var maybeFound = Accessor.Use(database =>
            {
                foreach (var kvp in database.PrefixSearch(pipIdKey.ToByteArray(), PipColumnFamilyName))
                {
                    var pipKey = PipIdKey.Parser.ParseFrom(kvp.Key);
                    if (m_pipParserDictionary.TryGetValue(pipKey.PipType, out var parser))
                    {
                        foundPip = parser.ParseFrom(kvp.Value);
                        outPipType = pipKey.PipType;
                    }
                    else
                    {
                        Contract.Assert(false, "No parser found for PipId");
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

        /// <summary>
        /// Gets all pips of a certain type.
        /// </summary>
        /// <returns>Returns list of all pips of certain type, empty if no such pips exist.</returns>
        public IEnumerable<IMessage> GetAllPipsByType(PipType pipType)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var storedPips = new List<IMessage>();

            if (!m_pipParserDictionary.TryGetValue(pipType, out var parser))
            {
                Contract.Assert(false, "No parser found for PipId");
            }

            // Empty key will match all pips in the prefix search, and then we grab only the ones that match the type we want
            var pipIdKey = new PipIdKey();

            var maybeFound = Accessor.Use(database =>
            {
                foreach (var kvp in database.PrefixSearch(pipIdKey.ToByteArray(), PipColumnFamilyName))
                {
                    var pipKey = PipIdKey.Parser.ParseFrom(kvp.Key);
                    if (pipKey.PipType == pipType)
                    {
                        storedPips.Add(parser.ParseFrom(kvp.Value));
                    }
                }
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            return storedPips;
        }

        /// <summary>
        /// Gets all Process Pips
        /// </summary>
        public IEnumerable<ProcessPip> GetAllProcessPips() => GetAllPipsByType(PipType.Process).Cast<ProcessPip>();

        /// <summary>
        /// Gets all WriteFile Pips
        /// </summary>
        public IEnumerable<WriteFile> GetAllWriteFilePips() => GetAllPipsByType(PipType.WriteFile).Cast<WriteFile>();

        /// <summary>
        /// Gets all CopyFile Pips
        /// </summary>
        public IEnumerable<CopyFile> GetAllCopyFilePips() => GetAllPipsByType(PipType.CopyFile).Cast<CopyFile>();

        /// <summary>
        /// Gets all IPC Pips
        /// </summary>
        public IEnumerable<IpcPip> GetAllIPCPips() => GetAllPipsByType(PipType.Ipc).Cast<IpcPip>();

        /// <summary>
        /// Gets all Seal Directory Pips
        /// </summary>
        public IEnumerable<SealDirectory> GetAllSealDirectoryPips() => GetAllPipsByType(PipType.SealDirectory).Cast<SealDirectory>();

        /// <summary>
        /// Gets all scheduled pips (ie. all the non meta pips). Must return pair of PipType, IMessage so user knows how to
        /// cast the IMessage object
        /// </summary>
        public IEnumerable<(PipType, IMessage)> GetAllScheduledPips()
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var allPips = new List<(PipType, IMessage)>();

            // Empty key to prefix match all pips stored in DB
            var pipIdKey = new PipIdKey();

            var maybeFound = Accessor.Use(database =>
            {
                foreach (var kvp in database.PrefixSearch(pipIdKey.ToByteArray(), PipColumnFamilyName))
                {
                    var pipType = PipIdKey.Parser.ParseFrom(kvp.Key).PipType;
                    if (m_pipParserDictionary.TryGetValue(pipType, out var parser))
                    {
                        var xldbPip = parser.ParseFrom(kvp.Value);
                        allPips.Add((pipType, xldbPip));
                    }
                    else
                    {
                        Contract.Assert(false, "No parser found for PipId");
                    }
                }
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            return allPips;
        }

        /// <summary>
        /// Gets the pip graph meta data
        /// </summary>
        /// <returns>Metadata, null if no such value found</returns>
        public PipGraph GetPipGraphMetaData()
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var graphMetadata = new CachedGraphKey
            {
                PipGraph = true
            };

            var maybeFound = Accessor.Use(database =>
            {
                database.TryGetValue(graphMetadata.ToByteArray(), out var pipTableMetadata, StaticGraphColumnFamilyName);
                return PipGraph.Parser.ParseFrom(pipTableMetadata);
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            return maybeFound.Result;
        }

        /// <summary>
        /// Gets all the information about a certain path (which pips produce it, and which consume it)
        /// Though there should be one producer for each file artifact, since we do not store the rewrite count, 
        /// prefix search will match every pip that produced (and re-wrote) a file, which means it can be a list.
        /// </summary>
        public (IEnumerable<uint>, IEnumerable<uint>) GetProducerAndConsumersOfPath(string path, bool isDirectory)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            if (isDirectory)
            {
                return (GetProducersOfDirectory(path), GetConsumersOfDirectory(path));
            }

            return (GetProducersOfFile(path), GetConsumersOfFile(path));
        }

        /// <summary>
        /// Gets all producers of a particular file
        /// Though there should be one producer for each file artifact, since we do not store the rewrite count, 
        /// prefix search will match every pip that produced (and re-wrote) a file, which means it can be a list.
        /// </summary>
        public IEnumerable<uint> GetProducersOfFile(string path)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var fileProducerKey = new FileProducerConsumerKey()
            {
                Type = ProducerConsumerType.Producer,
                FileArtifact = path
            };

            return GetProducerConsumerOfFileByKey(fileProducerKey);
        }

        /// <summary>
        /// Gets all consumers of a particular file
        /// </summary>
        public IEnumerable<uint> GetConsumersOfFile(string path)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var fileConsumerKey = new FileProducerConsumerKey()
            {
                Type = ProducerConsumerType.Consumer,
                FileArtifact = path
            };

            return GetProducerConsumerOfFileByKey(fileConsumerKey);
        }

        /// <summary>
        /// Gets producers or consumers of a file based on the key passed in
        /// </summary>
        private IEnumerable<uint> GetProducerConsumerOfFileByKey(FileProducerConsumerKey key)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var fileProducersOrConsumers = new List<uint>();

            var maybeFound = Accessor.Use(database =>
            {
                foreach (var kvp in database.PrefixSearch(key.ToByteArray(), StaticGraphColumnFamilyName))
                {
                    if (key.Type == ProducerConsumerType.Producer)
                    {
                        var pipId = FileProducerValue.Parser.ParseFrom(kvp.Value).PipId;
                        fileProducersOrConsumers.Add(pipId);
                    }
                    else if (key.Type == ProducerConsumerType.Consumer)
                    {
                        fileProducersOrConsumers.AddRange(FileConsumerValue.Parser.ParseFrom(kvp.Value).PipIds);
                    }
                }
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            return fileProducersOrConsumers;
        }

        /// <summary>
        /// Gets all producers of a particular directory. There should be only one, but to make it 
        /// compatible with GetProducerAndConsumersOfPath, it also returns a list of producers.
        /// </summary>
        public IEnumerable<uint> GetProducersOfDirectory(string path)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var directoryProducerKey = new DirectoryProducerConsumerKey()
            {
                Type = ProducerConsumerType.Producer,
                DirectoryArtifact = path
            };

            return GetProducerConsumerOfDirectoryByKey(directoryProducerKey);
        }

        /// <summary>
        /// Gets all consumers of a particular directory
        /// </summary>
        public IEnumerable<uint> GetConsumersOfDirectory(string path)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var directoryConsumerKey = new DirectoryProducerConsumerKey()
            {
                Type = ProducerConsumerType.Consumer,
                DirectoryArtifact = path
            };

            return GetProducerConsumerOfDirectoryByKey(directoryConsumerKey);
        }

        /// <summary>
        /// Gets producers or consumers of a directory based on the key passed in
        /// </summary>
        private IEnumerable<uint> GetProducerConsumerOfDirectoryByKey(DirectoryProducerConsumerKey key)
        {
            Contract.Requires(Accessor != null, "XldbDataStore is not initialized");

            var directoryProducerOrConsumers = new List<uint>();

            var maybeFound = Accessor.Use(database =>
            {
                foreach (var kvp in database.PrefixSearch(key.ToByteArray(), StaticGraphColumnFamilyName))
                {
                    if (key.Type == ProducerConsumerType.Producer)
                    {
                        var pipId = FileProducerValue.Parser.ParseFrom(kvp.Value).PipId;
                        directoryProducerOrConsumers.Add(pipId);
                    }
                    else if (key.Type == ProducerConsumerType.Consumer)
                    {
                        directoryProducerOrConsumers.AddRange(FileConsumerValue.Parser.ParseFrom(kvp.Value).PipIds);
                    }
                }
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            return directoryProducerOrConsumers;
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
