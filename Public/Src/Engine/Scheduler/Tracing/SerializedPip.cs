// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BuildXL.Scheduler.Tracing
{
#pragma warning disable 1591 //Missing XML comment for publicly visible type or member
    /// <summary>
    /// Container class for all the other serialization section classes
    /// </summary>
    public class SerializedPip
    {
        [JsonPropertyName("Pip Metadata")]
        public PipMetaData PipMetaData { get; set; }
        [JsonPropertyName("CopyFile Pip Details")]
        public CopyFileSpecificDetails CopyFileSpecificDetails { get; set; }
        [JsonPropertyName("Process Pip Details")]
        public ProcessSpecificDetails ProcessSpecificDetails { get; set; }
        [JsonPropertyName("Ipc Pip Details")]
        public IpcSpecificDetails IpcSpecificDetails { get; set; }
        [JsonPropertyName("Value Pip Details")]
        public ValueSpecificDetails ValueSpecificDetails { get; set; }
        [JsonPropertyName("SpecFile Pip Details")]
        public SpecFileSpecificDetails SpecFileSpecificDetails { get; set; }
        [JsonPropertyName("Module Pip Details")]
        public ModuleSpecificDetails ModuleSpecificDetails { get; set; }
        [JsonPropertyName("HashSourceFile Pip Details")]
        public HashSourceFileSpecificDetails HashSourceFileSpecificDetails { get; set; }
        [JsonPropertyName("SealDirectory Pip Details")]
        public SealDirectorySpecificDetails SealDirectorySpecificDetails { get; set; }
        [JsonPropertyName("WriteFile Pip Details")]
        public WriteFileSpecificDetails WriteFileSpecificDetails { get; set; }
        public List<ReportedProcessData> ReportedProcesses { get; set; }
        public List<ReportedFileAccessData> ReportedFileAccesses { get; set; }
    }

    /// <nodoc/>
    public class PipMetaData
    {
        public string PipId { get; set; }
        public string SemiStableHash { get; set; }
        public string PipType { get; set; }
        public List<string> Tags { get; set; }

        // Provenance
        public string Qualifier { get; set; }
        public string Usage { get; set; }
        public string Spec { get; set; }
        public string Location { get; set; }
        public string Thunk { get; set; }
        public string ModuleId { get; set; }
    }

    #region CopyFileSpecificDetails
    /// <nodoc/>
    public class CopyFileSpecificDetails
    {
        public string Source { get; set; }
        public string Destination { get; set; }
    }
    #endregion CopyFileSpecificDetails

    #region ProcessSpecificDetails
    /// <nodoc/>
    public class ProcessSpecificDetails
    {
        [JsonPropertyName("Process Invocation Details")]
        public ProcessInvocationDetails ProcessInvocationDetails { get; set; }
        [JsonPropertyName("Process Input/Output Handling")]
        public ProcessIoHandling ProcessIoHandling { get; set; }
        [JsonPropertyName("Process Directories")]
        public ProcessDirectories ProcessDirectories { get; set; }
        [JsonPropertyName("Process Advanced Options")]
        public ProcessAdvancedOptions ProcessAdvancedOptions { get; set; }
        [JsonPropertyName("Process Inputs/Outputs")]
        public ProcessInputOutput ProcessInputOutput { get; set; }
        [JsonPropertyName("Service Details")]
        public ServiceDetails ServiceDetails { get; set; }
    }

    /// <nodoc/>
    public class ProcessInvocationDetails
    {
        public string Executable { get; set; }
        [JsonPropertyName("Tool Description")]
        public string ToolDescription { get; set; }
        public string Arguments { get; set; }
        [JsonPropertyName("Response File Path")]
        public string ResponseFilePath { get; set; }
        [JsonPropertyName("Response File Contents")]
        public string ReponseFileContents { get; set; }
        [JsonPropertyName("Environment Variables")]
        public List<SerializedEnvironmentVariable> EnvironmentVariables { get; set; }
    }

    /// <nodoc/>
    public struct SerializedEnvironmentVariable
    {
        public string Variable { get; set; }
        public string Value { get; set; }

        public SerializedEnvironmentVariable(string variable, string value)
        {
            Variable = variable;
            Value = value;
        }
    }

    /// <nodoc/>
    public class ProcessIoHandling
    {
        [JsonPropertyName("Standard Input File Path")]
        public string StdInFile { get; set; }
        [JsonPropertyName("Standard Input Data")]
        public string StdInFileData { get; set; }
        [JsonPropertyName("Standard Output")]
        public string StdOut { get; set; }
        [JsonPropertyName("Standard Error")]
        public string StdErr { get; set; }
        [JsonPropertyName("Standard Directory")]
        public string StdDirectory { get; set; }
        [JsonPropertyName("Warning Regex")]
        public string WarningRegex { get; set; }
        [JsonPropertyName("Error Regex")]
        public string ErrorRegex { get; set; }
    }

    /// <nodoc/>
    public class ProcessDirectories
    {
        [JsonPropertyName("Working Directory")]
        public string WorkingDirectory { get; set; }
        [JsonPropertyName("Unique Output Directory")]
        public string UniqueOutputDirectory { get; set; }
        [JsonPropertyName("Temp Directory")]
        public string TempDirectory { get; set; }
        [JsonPropertyName("Additional Temp Directories")]
        public List<string> AdditionalTempDirectories { get; set; }
    }

    /// <nodoc/>
    public class ProcessAdvancedOptions
    {
        [JsonPropertyName("Warning Timeout (ms)")]
        public long? WarningTimeout { get; set; }
        [JsonPropertyName("Error Timeout (ms)")]
        public long? ErrorTimeout { get; set; }
        [JsonPropertyName("Success Codes")]
        public List<int> SuccessCodes { get; set; }
        public List<string> Semaphores { get; set; }
        public int PreserveOutputTrustLevel { get; set; }
        [JsonPropertyName("Preserve Outputs Allowlist")]
        public List<string> PreserveOutputsAllowlist { get; set; }
        [JsonPropertyName("Process Options")]
        public string ProcessOptions { get; set; }
        [JsonPropertyName("Retry Exit Codes")]
        public List<int> RetryExitCodes { get; set; }
    }

    /// <nodoc/>
    public class ProcessInputOutput
    {
        [JsonPropertyName("File Dependencies")]
        public List<string> FileDependencies { get; set; }
        [JsonPropertyName("Directory Dependencies")]
        public List<string> DirectoryDependencies { get; set; }
        [JsonPropertyName("Pip Dependencies")]
        public List<string> PipDependencies { get; set; }
        [JsonPropertyName("File Outputs")]
        public List<string> FileOutputs { get; set; }
        [JsonPropertyName("Directory Outputs")]
        public List<string> DirectoryOutputs { get; set; }
        [JsonPropertyName("Untracked Paths")]
        public List<string> UntrackedPaths { get; set; }
        [JsonPropertyName("Untracked Scopes")]
        public List<string> UntrackedScopes { get; set; }
    }

    /// <nodoc/>
    public class ServiceDetails
    {
        public bool IsService { get; set; }
        public string ShutdownProcessPipId { get; set; }
        public List<string> ServicePipDependencies { get; set; }
        public bool IsStartOrShutdownKind { get; set; }
    }
    #endregion ProcessSpecificDetails

    #region IpcSpecificDetails
    /// <nodoc/>
    public class IpcSpecificDetails
    {
        public string IpcMonikerId { get; set; }
        public string MessageBody { get; set; }
        public string OutputFile { get; set; }
        public List<string> ServicePipDependencies { get; set; }
        public List<string> FileDependencies { get; set; }
        public List<string> DirectoryDependencies { get; set; }
        public List<string> LazilyMaterializedFileDependencies { get; set; }
        public List<string> LazilyMaterializedDirectoryDependencies { get; set; }
        public bool IsServiceFinalization { get; set; }
        public bool MustRunOnOrchestrator { get; set; }
    }
    #endregion IpcSpecificDetails


    #region ValueSpecificDetails
    /// <nodoc/>
    public class ValueSpecificDetails
    {
        public string Symbol { get; set; }
        public string Qualifier { get; set; }
        public string SpecFile { get; set; }
        public string Location { get; set; }
    }
    #endregion ValueSpecificDetails

    #region SpecFileSpecificDetails
    /// <nodoc/>
    public class SpecFileSpecificDetails
    {
        public string SpecFile { get; set; }
        public string DefinitionFile { get; set; }
        public string Definition { get; set; }
        public int Module { get; set; }
    }
    #endregion SpecFileSpecificDetails

    #region ModuleSpecificDetails
    /// <nodoc/>
    public class ModuleSpecificDetails
    {
        public string Identity { get; set; }
        public string DestinationFile { get; set; }
        public string Definition { get; set; }
    }
    #endregion ModuleSpecificDetails

    #region HashSourceFileSpecificDetails
    /// <nodoc/>
    public class HashSourceFileSpecificDetails
    {
        public string Artifact { get; set; }
    }
    #endregion HashSourceFileSpecificDetails

    #region SealDirectorySpecificDetails
    /// <nodoc/>
    public class SealDirectorySpecificDetails
    {
        public string Kind { get; set; }
        public bool Scrub { get; set; }
        public string DirectoryRoot { get; set; }
        public string DirectoryArtifact { get; set; }
        public List<string> ComposedDirectories { get; set; }
        public string ContentFilter { get; set; }
        public List<string> DynamicContents { get; set; }
    }
    #endregion SealDirectorySpecificDetails

    #region WriteFileSpecificDetails
    /// <nodoc/>
    public class WriteFileSpecificDetails
    {
        public string Contents { get; set; }
        public string FileEncoding { get; set; }
    }
    #endregion WriteFileSpecificDetails

    #region ObservedFileAccesses
    public class ReportedProcessData
    {
        public uint ProcessId { get; set; }
        public uint ParentProcessId { get; set; }
        public string Path { get; set; }
        public string ProcessArgs { get; set; }
        public string CreationTime { get; set; }
        public string ExitTime { get; set; }
        public uint ExitCode { get; set; }
        public long? KernelTime { get; set; }
        public long? UserTime { get; set; }
        public string IOCountersRead { get; set; }
        public string IOCountersWrite { get; set; }
        public string IOCountersOther { get; set; }
    }

    public class ReportedFileAccessData
    { 
        public string Path { get; set; }
        public string RequestedAccess { get; set; }
        public string CreationDisposition { get; set; }
        public string DesiredAccess { get; set; }
        public string ShareMode { get; set; }
        public string Status { get; set; }
        public string Operation { get; set; }
        public string FlagsAndAttributes { get; set; }
        public uint Error { get; set; }
        public ulong Usn { get; set; }
        public string ManifestPath { get; set; }
        public uint Process { get; set; }
        public bool ExplicitlyReported { get; set; }
        public string EnumeratePattern { get; set; }
    }
    #endregion ObservedFileAccesses

#pragma warning restore 1591
}
