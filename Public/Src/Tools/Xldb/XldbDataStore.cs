// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Xldb.Proto;
using Google.Protobuf;

namespace BuildXL.Xldb
{
    public sealed class XldbDataStore : IDisposable, IXldbDataStore
    {
        /// <summary>
        /// Rocks DB Accessor for XLG++ data
        /// </summary>
        private readonly KeyValueStoreAccessor m_accessor;

        private Dictionary<ExecutionEventId, MessageParser> m_eventParserDictionary = new Dictionary<ExecutionEventId, MessageParser>();
        private Dictionary<PipType, MessageParser> m_pipParserDictionary = new Dictionary<PipType, MessageParser>();
        private const uint s_workerIDDefaultValue = uint.MaxValue;
        private const int s_fileRewriteCountDefaultValue = -1;

        public const string EventColumnFamilyName = "Event";
        public const string PipColumnFamilyName = "Pip";
        public const string StaticGraphColumnFamilyName = "StaticGraph";

        /// <summary>
        /// Version file name. Contains a single integer that represents the XldbVersion (see below)
        /// </summary>
        public const string XldbVersionFileName = "xldbversion.txt";

        /// <summary>
        /// The Xldb datastore can read any Xldb that has a verion that is equal to this number or before.
        /// Only bump this version when there are major changes to the underlying db instance
        /// (i.e. ProtoBuf objects being changed, indexes being created/changed, etc).
        /// </summary>
        public const int XldbVersion = 1;

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
            if (File.Exists(Path.Combine(storeDirectory, XldbVersionFileName)))
            {
                using TextReader reader = File.OpenText(Path.Combine(storeDirectory, XldbVersionFileName));
                var version = int.Parse(reader.ReadLine());

                if (version > XldbVersion)
                {
                    throw new Exception($"The Xldb version you are trying to access is newer than your Xldb Datastore version. There may be breaking changes in this new version and so the accessor cannot be created. Exiting now ...");
                }
            }
            else
            {
                throw new Exception($"Xldb version file not found in storeDirectory. Cannot open the accessor with this version file and exiting now ...");
            }

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
                m_accessor = accessor.Result;
            }
            else
            {
                m_accessor = null;
                throw new Exception($"Could not create an accessor for RocksDB. Accessor is null! Error is {accessor.Failure.DescribeIncludingInnerFailures()}");
            }

            m_eventParserDictionary.Add(ExecutionEventId.FileArtifactContentDecided, FileArtifactContentDecidedEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.WorkerList, WorkerListEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.PipExecutionPerformance, PipExecutionPerformanceEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.DirectoryMembershipHashed, DirectoryMembershipHashedEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.ProcessExecutionMonitoringReported, ProcessExecutionMonitoringReportedEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.ProcessFingerprintComputation, ProcessFingerprintComputationEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.BuildSessionConfiguration, BuildSessionConfigurationEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.DependencyViolationReported, DependencyViolationReportedEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.PipExecutionStepPerformanceReported, PipExecutionStepPerformanceReportedEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.PipCacheMiss, PipCacheMissEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.ResourceUsageReported, StatusReportedEvent.Parser);
            m_eventParserDictionary.Add(ExecutionEventId.BxlInvocation, BxlInvocationEvent.Parser);
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
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = eventTypeID,
                WorkerID = s_workerIDDefaultValue,
                FileRewriteCount = s_fileRewriteCountDefaultValue
            };

            return GetEventsByKey(eventKey);
        }

        /// <summary>
        /// Gets events from the DB based on the eventKey. 
        /// </summary>
        /// <remarks>
        /// Since 0 isn't serialized by Protobuf, a PrefixSearch for RewriteCount = 0 would match everything. 
        /// Thus to avoid that, we set it to -1 to "match everything", else we look for specific rewrite counts.
        /// Similarly, a PrefixSearch for WorkerID = 0 would match everything. 
        /// Thus to avoid that, we set it to uint.MaxValue to "match everything", else we look for specific rewrite counts.
        /// </remarks>
        /// <returns>List of events, empty if no such events exist</returns>
        private IEnumerable<IMessage> GetEventsByKey(EventKey eventKey)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var storedEvents = new List<IMessage>();

            if (!m_eventParserDictionary.TryGetValue(eventKey.EventTypeID, out var parser))
            {
                Contract.Assert(false, "No parser found for EventTypeId passed in");
            }

            var matchAllRewriteCounts = false;
            var matchAllWorkerIDs = false;

            if (eventKey.FileRewriteCount == s_fileRewriteCountDefaultValue)
            {
                matchAllRewriteCounts = true;
                // Set it to 0 to match everything
                eventKey.FileRewriteCount = 0;
            }

            if (eventKey.WorkerID == s_workerIDDefaultValue)
            {
                matchAllWorkerIDs = true;
                // Set it to 0 to match everything
                eventKey.WorkerID = 0;
            }

            var maybeFound = m_accessor.Use(database =>
            {
                foreach (var kvp in database.PrefixSearch(eventKey.ToByteArray(), EventColumnFamilyName))
                {
                    var kvpKey = EventKey.Parser.ParseFrom(kvp.Key);
                    // MatchAllWorker IDs and MatchAllRewriteCounts are true, so just add everything
                    if (matchAllWorkerIDs && matchAllRewriteCounts)
                    {
                        storedEvents.Add(parser.ParseFrom(kvp.Value));
                    }
                    // Else if matching all WorkerIDs, check for specific RewriteCounts OR
                    // if matching all RewriteCounts, check for specific worker ID
                    else if ((matchAllWorkerIDs && kvpKey.FileRewriteCount == eventKey.FileRewriteCount) ||
                             (matchAllRewriteCounts && kvpKey.WorkerID == eventKey.WorkerID))
                    {
                        storedEvents.Add(parser.ParseFrom(kvp.Value));
                    }
                    // Else both worker ID and RewriteCounts are unique so the prefix search matches the right one
                    else
                    {
                        storedEvents.Add(parser.ParseFrom(kvp.Value));
                    }
                }
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            return storedEvents;
        }

        /// <inheritdoc />
        public IEnumerable<DependencyViolationReportedEvent> GetDependencyViolationEventByKey(uint violatorPipID, uint? workerID = null)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = ExecutionEventId.DependencyViolationReported,
                ViolatorPipID = violatorPipID,
                FileRewriteCount = s_fileRewriteCountDefaultValue,
                WorkerID = workerID ?? s_workerIDDefaultValue
            };

            return GetEventsByKey(eventKey).Cast<DependencyViolationReportedEvent>();
        }

        /// <inheritdoc />
        public IEnumerable<PipExecutionStepPerformanceReportedEvent> GetPipExecutionStepPerformanceEventByKey(uint pipID, PipExecutionStep pipExecutionStep = 0, uint? workerID = null)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = ExecutionEventId.PipExecutionStepPerformanceReported,
                WorkerID = workerID ?? s_workerIDDefaultValue,
                FileRewriteCount = s_fileRewriteCountDefaultValue,
                PipId = pipID,
                PipExecutionStepPerformanceKey = pipExecutionStep
            };

            return GetEventsByKey(eventKey).Cast<PipExecutionStepPerformanceReportedEvent>();
        }

        /// <inheritdoc />
        public IEnumerable<ProcessFingerprintComputationEvent> GetProcessFingerprintComputationEventByKey(uint pipID, FingerprintComputationKind computationKind = 0, uint? workerID = null)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = ExecutionEventId.ProcessFingerprintComputation,
                WorkerID = workerID ?? s_workerIDDefaultValue,
                FileRewriteCount = s_fileRewriteCountDefaultValue,
                PipId = pipID,
                ProcessFingerprintComputationKey = computationKind
            };

            return GetEventsByKey(eventKey).Cast<ProcessFingerprintComputationEvent>();
        }

        /// <inheritdoc />
        public IEnumerable<DirectoryMembershipHashedEvent> GetDirectoryMembershipHashedEventByKey(uint pipID, string directoryPath = "", uint? workerID = null)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = ExecutionEventId.DirectoryMembershipHashed,
                WorkerID = workerID ?? s_workerIDDefaultValue,
                FileRewriteCount = s_fileRewriteCountDefaultValue,
                PipId = pipID,
                DirectoryMembershipHashedKey = directoryPath
            };

            return GetEventsByKey(eventKey).Cast<DirectoryMembershipHashedEvent>();
        }

        /// <inheritdoc />
        public IEnumerable<PipExecutionDirectoryOutputsEvent> GetPipExecutionDirectoryOutputEventByKey(uint pipID, string directoryPath = "", uint? workerID = null)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = ExecutionEventId.PipExecutionDirectoryOutputs,
                WorkerID = workerID ?? s_workerIDDefaultValue,
                FileRewriteCount = s_fileRewriteCountDefaultValue,
                PipId = pipID,
                PipExecutionDirectoryOutputKey = directoryPath
            };

            return GetEventsByKey(eventKey).Cast<PipExecutionDirectoryOutputsEvent>();
        }

        /// <inheritdoc />
        public IEnumerable<FileArtifactContentDecidedEvent> GetFileArtifactContentDecidedEventByKey(string directoryPath, int? fileRewriteCount = null, uint? workerID = null)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = ExecutionEventId.FileArtifactContentDecided,
                WorkerID = workerID ?? s_workerIDDefaultValue,
                FileArtifactContentDecidedKey = directoryPath,
                FileRewriteCount = fileRewriteCount ?? s_fileRewriteCountDefaultValue
            };

            return GetEventsByKey(eventKey).Cast<FileArtifactContentDecidedEvent>();
        }

        /// <inheritdoc />
        public IEnumerable<PipExecutionPerformanceEvent> GetPipExecutionPerformanceEventByKey(uint pipID, uint? workerID = null) => GetEventsByPipIdOnly(ExecutionEventId.PipExecutionPerformance, pipID, workerID).Cast<PipExecutionPerformanceEvent>();

        /// <inheritdoc />
        public IEnumerable<ProcessExecutionMonitoringReportedEvent> GetProcessExecutionMonitoringReportedEventByKey(uint pipID, uint? workerID = null) => GetEventsByPipIdOnly(ExecutionEventId.ProcessExecutionMonitoringReported, pipID, workerID).Cast<ProcessExecutionMonitoringReportedEvent>();

        /// <inheritdoc />
        public IEnumerable<PipCacheMissEvent> GetPipCacheMissEventByKey(uint pipID, uint? workerID = null) => GetEventsByPipIdOnly(ExecutionEventId.PipCacheMiss, pipID, workerID).Cast<PipCacheMissEvent>();

        /// <summary>
        /// Get events that only use PipID as the key
        /// </summary>
        private IEnumerable<IMessage> GetEventsByPipIdOnly(ExecutionEventId eventTypeID, uint pipID, uint? workerID = null)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var eventKey = new EventKey
            {
                EventTypeID = eventTypeID,
                PipId = pipID,
                WorkerID = workerID ?? s_workerIDDefaultValue,
                FileRewriteCount = s_fileRewriteCountDefaultValue
            };

            return GetEventsByKey(eventKey);
        }

        /// <inheritdoc />
        public DBStorageStatsValue GetDBStatsInfoByStorageType(DBStoredTypes storageType)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var storageStatsKey = new DBStorageStatsKey
            {
                StorageType = storageType,
            };

            var maybeFound = m_accessor.Use(database =>
            {
                if (database.TryGetValue(storageStatsKey.ToByteArray(), out var storageStatValue))
                {
                    return DBStorageStatsValue.Parser.ParseFrom(storageStatValue);
                }
                return null;
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            return maybeFound.Result;
        }

        /// <inheritdoc />
        public IEnumerable<FileArtifactContentDecidedEvent> GetFileArtifactContentDecidedEvents() => GetEventsByType(ExecutionEventId.FileArtifactContentDecided).Cast<FileArtifactContentDecidedEvent>();

        /// <inheritdoc />
        public IEnumerable<WorkerListEvent> GetWorkerListEvents() => GetEventsByType(ExecutionEventId.WorkerList).Cast<WorkerListEvent>();

        /// <inheritdoc />
        public IEnumerable<PipExecutionPerformanceEvent> GetPipExecutionPerformanceEvents() => GetEventsByType(ExecutionEventId.PipExecutionPerformance).Cast<PipExecutionPerformanceEvent>();

        /// <inheritdoc />
        public IEnumerable<DirectoryMembershipHashedEvent> GetDirectoryMembershipHashedEvents() => GetEventsByType(ExecutionEventId.DirectoryMembershipHashed).Cast<DirectoryMembershipHashedEvent>();

        /// <inheritdoc />
        public IEnumerable<ProcessExecutionMonitoringReportedEvent> GetProcessExecutionMonitoringReportedEvents() => GetEventsByType(ExecutionEventId.ProcessExecutionMonitoringReported).Cast<ProcessExecutionMonitoringReportedEvent>();

        /// <inheritdoc />
        public IEnumerable<ProcessFingerprintComputationEvent> GetProcessFingerprintComputationEvents() => GetEventsByType(ExecutionEventId.ProcessFingerprintComputation).Cast<ProcessFingerprintComputationEvent>();

        /// <inheritdoc />
        public IEnumerable<BuildSessionConfigurationEvent> GetBuildSessionConfigurationEvents() => GetEventsByType(ExecutionEventId.BuildSessionConfiguration).Cast<BuildSessionConfigurationEvent>();

        /// <inheritdoc />
        public IEnumerable<DependencyViolationReportedEvent> GetDependencyViolationReportedEvents() => GetEventsByType(ExecutionEventId.DependencyViolationReported).Cast<DependencyViolationReportedEvent>();

        /// <inheritdoc />
        public IEnumerable<PipExecutionStepPerformanceReportedEvent> GetPipExecutionStepPerformanceReportedEvents() => GetEventsByType(ExecutionEventId.PipExecutionStepPerformanceReported).Cast<PipExecutionStepPerformanceReportedEvent>();

        /// <inheritdoc />
        public IEnumerable<StatusReportedEvent> GetStatusReportedEvents() => GetEventsByType(ExecutionEventId.ResourceUsageReported).Cast<StatusReportedEvent>();

        /// <inheritdoc />
        public IEnumerable<PipCacheMissEvent> GetPipCacheMissEvents() => GetEventsByType(ExecutionEventId.PipCacheMiss).Cast<PipCacheMissEvent>();

        /// <inheritdoc />
        public IEnumerable<BxlInvocationEvent> GetBxlInvocationEvents() => GetEventsByType(ExecutionEventId.BxlInvocation).Cast<BxlInvocationEvent>();

        /// <inheritdoc />
        public IEnumerable<PipExecutionDirectoryOutputsEvent> GetPipExecutionDirectoryOutputsEvents() => GetEventsByType(ExecutionEventId.PipExecutionDirectoryOutputs).Cast<PipExecutionDirectoryOutputsEvent>();

        /// <inheritdoc />
        public IMessage GetPipBySemiStableHash(long semiStableHash, out PipType pipType)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            IMessage foundPip = null;

            var pipSemistableHashKey = new PipSemistableHashKey()
            {
                SemiStableHash = semiStableHash
            };

            PipType outPipType = 0;

            var maybeFound = m_accessor.Use(database =>
            {
                if (database.TryGetValue(pipSemistableHashKey.ToByteArray(), out var pipValueSemistableHash))
                {
                    foundPip = GetPipByPipId(PipIdKey.Parser.ParseFrom(pipValueSemistableHash).PipId, out outPipType);
                }
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            pipType = outPipType;
            return foundPip;
        }

        /// <inheritdoc />
        public IMessage GetPipByPipId(uint pipId, out PipType pipType)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            IMessage foundPip = null;

            var pipIdKey = new PipIdKey()
            {
                PipId = pipId
            };

            PipType outPipType = 0;

            var maybeFound = m_accessor.Use(database =>
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

        /// <inheritdoc />
        public IEnumerable<IMessage> GetAllPipsByType(PipType pipType)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var storedPips = new List<IMessage>();

            if (!m_pipParserDictionary.TryGetValue(pipType, out var parser))
            {
                Contract.Assert(false, "No parser found for PipId");
            }

            // Empty key will match all pips in the prefix search, and then we grab only the ones that match the type we want
            var pipIdKey = new PipIdKey();

            var maybeFound = m_accessor.Use(database =>
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

        /// <inheritdoc />
        public IEnumerable<ProcessPip> GetAllProcessPips() => GetAllPipsByType(PipType.Process).Cast<ProcessPip>();

        /// <inheritdoc />
        public IEnumerable<WriteFile> GetAllWriteFilePips() => GetAllPipsByType(PipType.WriteFile).Cast<WriteFile>();

        /// <inheritdoc />
        public IEnumerable<CopyFile> GetAllCopyFilePips() => GetAllPipsByType(PipType.CopyFile).Cast<CopyFile>();

        /// <inheritdoc />
        public IEnumerable<IpcPip> GetAllIpcPips() => GetAllPipsByType(PipType.Ipc).Cast<IpcPip>();

        /// <inheritdoc />
        public IEnumerable<SealDirectory> GetAllSealDirectoryPips() => GetAllPipsByType(PipType.SealDirectory).Cast<SealDirectory>();

        /// <inheritdoc />
        public PipGraph GetPipGraphMetaData()
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var graphMetadata = new GraphMetadataKey
            {
                Type = GraphMetaData.PipGraph
            };

            var maybeFound = m_accessor.Use(database =>
            {
                database.TryGetValue(graphMetadata.ToByteArray(), out var pipGraphMetaData, StaticGraphColumnFamilyName);
                return PipGraph.Parser.ParseFrom(pipGraphMetaData);
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            return maybeFound.Result;
        }

        public MountPathExpander GetMountPathExpander()
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var graphMetadata = new GraphMetadataKey
            {
                Type = GraphMetaData.MountPathExpander
            };

            var maybeFound = m_accessor.Use(database =>
            {
                database.TryGetValue(graphMetadata.ToByteArray(), out var mountPathExpanderData, StaticGraphColumnFamilyName);
                return MountPathExpander.Parser.ParseFrom(mountPathExpanderData);
            });

            if (!maybeFound.Succeeded)
            {
                maybeFound.Failure.Throw();
            }

            return maybeFound.Result;
        }

        /// <inheritdoc />
        public (IEnumerable<uint>, IEnumerable<uint>) GetProducerAndConsumersOfPath(string path, bool isDirectory)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            if (isDirectory)
            {
                return (GetProducersOfDirectory(path), GetConsumersOfDirectory(path));
            }

            return (GetProducersOfFile(path), GetConsumersOfFile(path));
        }

        /// <inheritdoc />
        public IEnumerable<uint> GetProducersOfFile(string path)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var fileProducerKey = new FileProducerConsumerKey()
            {
                Type = ProducerConsumerType.Producer,
                FilePath = path
            };

            return GetProducerConsumerOfFileByKey(fileProducerKey);
        }

        /// <inheritdoc />
        public IEnumerable<uint> GetConsumersOfFile(string path)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var fileConsumerKey = new FileProducerConsumerKey()
            {
                Type = ProducerConsumerType.Consumer,
                FilePath = path
            };

            return GetProducerConsumerOfFileByKey(fileConsumerKey);
        }

        /// <summary>
        /// Gets producers or consumers of a file based on the key passed in
        /// </summary>
        private IEnumerable<uint> GetProducerConsumerOfFileByKey(FileProducerConsumerKey key)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var fileProducersOrConsumers = new List<uint>();

            var maybeFound = m_accessor.Use(database =>
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

        /// <inheritdoc />
        public IEnumerable<uint> GetProducersOfDirectory(string path)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var directoryProducerKey = new DirectoryProducerConsumerKey()
            {
                Type = ProducerConsumerType.Producer,
                DirectoryPath = path
            };

            return GetProducerConsumerOfDirectoryByKey(directoryProducerKey);
        }

        /// <inheritdoc />
        public IEnumerable<uint> GetConsumersOfDirectory(string path)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var directoryConsumerKey = new DirectoryProducerConsumerKey()
            {
                Type = ProducerConsumerType.Consumer,
                DirectoryPath = path
            };

            return GetProducerConsumerOfDirectoryByKey(directoryConsumerKey);
        }

        /// <summary>
        /// Gets producers or consumers of a directory based on the key passed in
        /// </summary>
        private IEnumerable<uint> GetProducerConsumerOfDirectoryByKey(DirectoryProducerConsumerKey key)
        {
            Contract.Requires(m_accessor != null, "XldbDataStore is not initialized");

            var directoryProducerOrConsumers = new List<uint>();

            var maybeFound = m_accessor.Use(database =>
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
            m_accessor.Dispose();
        }
    }
}
