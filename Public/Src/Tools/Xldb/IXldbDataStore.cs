using System.Collections.Generic;
using BuildXL.Xldb.Proto;
using Google.Protobuf;

namespace BuildXL.Xldb
{
    /// <summary>
    /// Interface for Xldb API.
    /// </summary>
    public interface IXldbDataStore
    {
        /// Pip Related Endpoints
        
        /// <summary>
        /// Gets all CopyFile Pips
        /// </summary>
        IEnumerable<CopyFile> GetAllCopyFilePips();

        /// <summary>
        /// Gets all IPC Pips
        /// </summary>
        IEnumerable<IpcPip> GetAllIPCPips();

        /// <summary>
        /// Gets all pips of a certain type.
        /// </summary>
        /// <returns>Returns list of all pips of certain type, empty if no such pips exist.</returns>
        IEnumerable<IMessage> GetAllPipsByType(PipType pipType);

        /// <summary>
        /// Gets all Process Pips
        /// </summary>
        IEnumerable<ProcessPip> GetAllProcessPips();

        /// <summary>
        /// Gets all Seal Directory Pips
        /// </summary>
        IEnumerable<SealDirectory> GetAllSealDirectoryPips();

        /// <summary>
        /// Gets all WriteFile Pips
        /// </summary>
        IEnumerable<WriteFile> GetAllWriteFilePips();

        /// <summary>
        /// Gets the pip stored based on the pip id
        /// </summary>
        /// <returns>Returns null if no such pip is found</returns>
        IMessage GetPipByPipId(uint pipId, out PipType pipType);

        /// <summary>
        /// Gets the pip stored based on the semistable hash
        /// </summary>
        /// <returns>Returns null if no such pip is found</returns>
        IMessage GetPipBySemiStableHash(long semiStableHash, out PipType pipType);


        /// Event Related Endpoints

        /// <summary>
        /// Gets all the Bxl Invocation Events.
        /// </summary>
        IEnumerable<BxlInvocationEvent> GetBxlInvocationEvents();

        /// <summary>
        /// Gets a depdendency violated events by key
        /// </summary>
        /// <param name="workerID">If workerId = uint.MaxValue, it will match all worker IDs. Else it will match a specific worker ID</param>"
        IEnumerable<DependencyViolationReportedEvent> GetDependencyViolationEventByKey(uint violatorPipID, uint workerID = uint.MaxValue);

        /// <summary>
        /// Gets all the Dependency Violation Reported Events
        /// </summary>
        IEnumerable<DependencyViolationReportedEvent> GetDependencyViolationReportedEvents();

        /// <summary>
        /// Gets directory membership hashed event by key
        /// </summary>
        /// <param name="workerID">If workerId = uint.MaxValue, it will match all worker IDs. Else it will match a specific worker ID</param>"
        IEnumerable<DirectoryMembershipHashedEvent> GetDirectoryMembershipHashedEventByKey(uint pipID, string directoryPath = "", uint workerID = uint.MaxValue);

        /// <summary>
        /// Gets all the Directory Membership Hashed Events
        /// </summary>
        IEnumerable<DirectoryMembershipHashedEvent> GetDirectoryMembershipHashedEvents();

        /// <summary>
        /// Gets all the Execution Log Salts Data Events
        /// </summary>
        IEnumerable<ExecutionLogSaltsDataEvent> GetExecutionLogSaltsDataEvents();

        /// <summary>
        /// Gets file artficat content decided event by key. 
        /// </summary>
        /// <param name="fileRewriteCount">If fileRewriteCount = -1, it will match all rewrite counts. Else it will match a specific rewrite count.</param>
        /// <param name="workerID">If workerId = uint.MaxValue, it will match all worker IDs. Else it will match a specific worker ID</param>"
        IEnumerable<FileArtifactContentDecidedEvent> GetFileArtifactContentDecidedEventByKey(string directoryPath, int fileRewriteCount = -1, uint workerID = uint.MaxValue);

        /// <summary>
        /// Gets all the File Artifact Content Decided Events
        /// </summary>
        IEnumerable<FileArtifactContentDecidedEvent> GetFileArtifactContentDecidedEvents();

        /// <summary>
        /// Gets pip cache miss events by key
        /// </summary>
        /// <param name="workerID">If workerId = uint.MaxValue, it will match all worker IDs. Else it will match a specific worker ID</param>"
        IEnumerable<PipCacheMissEvent> GetPipCacheMissEventByKey(uint pipID, uint workerID = uint.MaxValue);

        /// <summary>
        /// Gets all the Pip Cache Miss Events
        /// </summary>
        IEnumerable<PipCacheMissEvent> GetPipCacheMissEvents();

        /// <summary>
        /// Gets pip execution directory output event by key
        /// </summary>
        /// <param name="workerID">If workerId = uint.MaxValue, it will match all worker IDs. Else it will match a specific worker ID</param>"
        IEnumerable<PipExecutionDirectoryOutputsEvent> GetPipExecutionDirectoryOutputEventByKey(uint pipID, string directoryPath = "", uint workerID = uint.MaxValue);

        /// <summary>
        /// Gets all the Pip Execution Directory Outputs Events
        /// </summary>
        /// <param name="workerID">If workerId = uint.MaxValue, it will match all worker IDs. Else it will match a specific worker ID</param>"
        IEnumerable<PipExecutionDirectoryOutputsEvent> GetPipExecutionDirectoryOutputsEvents();

        /// <summary>
        /// Gets pip execution performance events by key
        /// </summary>
        /// <param name="workerID">If workerId = uint.MaxValue, it will match all worker IDs. Else it will match a specific worker ID</param>"
        IEnumerable<PipExecutionPerformanceEvent> GetPipExecutionPerformanceEventByKey(uint pipID, uint workerID = uint.MaxValue);

        /// <summary>
        /// Gets all the Pip Execution Performance Events
        /// </summary>
        IEnumerable<PipExecutionPerformanceEvent> GetPipExecutionPerformanceEvents();

        /// <summary>
        /// Gets pip execution step performance events by key.
        /// If pipExecutionStep is not passed in, will match and return all steps for matching pipID
        /// </summary>
        /// <param name="workerID">If workerId = uint.MaxValue, it will match all worker IDs. Else it will match a specific worker ID</param>"
        IEnumerable<PipExecutionStepPerformanceReportedEvent> GetPipExecutionStepPerformanceEventByKey(uint pipID, PipExecutionStep pipExecutionStep = PipExecutionStep.Unspecified, uint workerID = uint.MaxValue);

        /// <summary>
        /// Gets all the Pip Execution Step Performance Reported Events
        /// </summary>
        IEnumerable<PipExecutionStepPerformanceReportedEvent> GetPipExecutionStepPerformanceReportedEvents();

        /// <summary>
        /// Gets process execution monitoring reported events by key
        /// </summary>
        /// <param name="workerID">If workerId = uint.MaxValue, it will match all worker IDs. Else it will match a specific worker ID</param>"
        IEnumerable<ProcessExecutionMonitoringReportedEvent> GetProcessExecutionMonitoringReportedEventByKey(uint pipID, uint workerID = uint.MaxValue);

        /// <summary>
        /// Gets all the Process Execution Monitoring Reported Events
        /// </summary>
        IEnumerable<ProcessExecutionMonitoringReportedEvent> GetProcessExecutionMonitoringReportedEvents();

        /// <summary>
        /// Gets process fingerprint computation events by key.
        /// If computationKind is not passed in, will match and return all all computation kinds for a matching pipID
        /// </summary>
        /// <param name="workerID">If workerId = uint.MaxValue, it will match all worker IDs. Else it will match a specific worker ID</param>"
        IEnumerable<ProcessFingerprintComputationEvent> GetProcessFingerprintComputationEventByKey(uint pipID, FingerprintComputationKind computationKind = FingerprintComputationKind.Unspecified, uint workerID = uint.MaxValue);

        /// <summary>
        /// Gets all the Process Execution Monitoring Reported Events
        /// </summary>
        IEnumerable<ProcessFingerprintComputationEvent> GetProcessFingerprintComputationEvents();

        /// <summary>
        /// Gets all the Status Reported Events
        /// </summary>
        IEnumerable<StatusReportedEvent> GetStatusReportedEvents();

        /// <summary>
        /// Gets all the Worker List Events
        /// </summary>
        IEnumerable<WorkerListEvent> GetWorkerListEvents();

        // Graph metadata and "interesting indexing" Endpoints

        /// <summary>
        /// Gets all consumers of a particular directory
        /// The Path must be a full path without any wildcards.
        /// </summary>
        IEnumerable<uint> GetConsumersOfDirectory(string path);

        /// <summary>
        /// Gets all consumers of a particular file
        /// The Path must be a full path without any wildcards.
        /// </summary>
        IEnumerable<uint> GetConsumersOfFile(string path);

        /// <summary>
        /// Gets all the information about a certain path (which pips produce it, and which consume it).
        /// The Path must be a full path without any wildcards.
        /// Though there should be one producer for each file artifact, since we do not store the rewrite count, 
        /// prefix search will match every pip that produced (and re-wrote) a file, which means it can be a list.
        /// </summary>
        (IEnumerable<uint>, IEnumerable<uint>) GetProducerAndConsumersOfPath(string path, bool isDirectory);

        /// <summary>
        /// Gets all producers of a particular directory.
        /// The Path must be a full path without any wildcards.
        /// There should be only one, but to make it 
        /// compatible with GetProducerAndConsumersOfPath, it also returns a list of producers.
        /// </summary>
        IEnumerable<uint> GetProducersOfDirectory(string path);

        /// <summary>
        /// Gets all producers of a particular file
        /// The Path must be a full path without any wildcards.
        /// Though there should be one producer for each file artifact, since we do not store the rewrite count, 
        /// prefix search will match every pip that produced (and re-wrote) a file, which means it can be a list.
        /// </summary>
        IEnumerable<uint> GetProducersOfFile(string path);

        /// <summary>
        /// Gets the pip graph meta data
        /// </summary>
        /// <returns>Metadata, null if no such value found</returns>
        PipGraph GetPipGraphMetaData();

        /// <summary>
        /// Gets the mount path expander information from the DB so the consumer knows the roots and mounts used in the build
        /// </summary>
        MountPathExpander GetMountPathExpander();

        /// <summary>
        /// Returns the count and payload of items stored in the DB
        /// </summary>
        /// <returns>DBStorageStatsValue if exists, null otherwise</returns>
        DBStorageStatsValue GetDBStatsInfoByStorageType(DBStoredTypes storageType);
    }
}