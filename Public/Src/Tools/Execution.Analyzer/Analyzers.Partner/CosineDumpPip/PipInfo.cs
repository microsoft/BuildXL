// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using global::BuildXL.Pips.Operations;

namespace BuildXL.Execution.Analyzer.Analyzers
{
    /// <summary>
    /// A class for storing information about a BuildXL Pip
    /// </summary>
    [DataContract]
    public class PipInfo
    {
        public PipInfo() { }

        [DataMember(EmitDefaultValue = false)]
        public PipMetadata PipMetadata { get; set; }

        /// Only one of the following Details should be present for a given PipInfo
        [DataMember(EmitDefaultValue = false)]
        public CopyFilePipDetails CopyFilePipDetails { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public ProcessPipDetails ProcessPipDetails { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public IpcPipDetails IpcPipDetails { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public ValuePipDetails ValuePipDetails { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public SpecFilePipDetails SpecFilePipDetails { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public ModulePipDetails ModulePipDetails { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public HashSourceFilePipDetails HashSourceFilePipDetails { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public SealDirectoryPipDetails SealDirectoryPipDetails { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public WriteFilePipDetails WriteFilePipDetails { get; set; }
    }

    [DataContract]
    public class PipMetadata
    {
        public PipMetadata() { }
        
        [DataMember(EmitDefaultValue = false)]
        public uint PipId { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string SemiStableHash { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public PipType PipType { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<string> Tags { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string Qualifier { get; set; }

        [DataMember (EmitDefaultValue = false)]
        public string Usage { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string SpecFilePath { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string OutputValueSymbol { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public int ModuleId { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<string> PipDependencies { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<string> PipDependents { get; set; }
    }

    [DataContract]
    public class CopyFilePipDetails
    {
        public CopyFilePipDetails() { }

        [DataMember(EmitDefaultValue = false)]
        public string Source { get; set; }
        
        [DataMember(EmitDefaultValue = false)]
        public string Destination { get; set; }
    }

    [DataContract]
    public class ProcessPipDetails
    {
        public ProcessPipDetails(){ }

        [DataMember(EmitDefaultValue = false)]
        public InvocationDetails InvocationDetails { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public InputOutputDetails InputOutputDetails { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public DirectoryDetails DirectoryDetails { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public AdvancedOptions AdvancedOptions { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public ProcessInputOutputDetails ProcessInputOutputDetails { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public ServiceDetails ServiceDetails { get; set; }
    }

    #region ProcessPipDetailsClasses
    [DataContract]
    public class InvocationDetails
    {
        public InvocationDetails() { }

        [DataMember(EmitDefaultValue = false)]
        public string Executable { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string ToolDescription { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string Arguments { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string ResponseFilePath { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string ResponseFileContents { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<KeyValuePair<string, string>> EnvironmentVariables { get; set; }

    }

    [DataContract]
    public class InputOutputDetails
    {
        public InputOutputDetails() { }

        [DataMember(EmitDefaultValue = false)]
        public string STDInFile { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string STDInData { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string STDOut { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string STDError { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string STDDirectory { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string WarningRegex { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string ErrorRegex { get; set; }
    }

    [DataContract]
    public class DirectoryDetails
    {
        public DirectoryDetails() { }

        [DataMember(EmitDefaultValue = false)]
        public string WorkingDirectory { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string UniqueOutputDirectory { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string TempDirectory { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<string> ExtraTempDirectories { get; set; }
    }

    [DataContract]
    public class AdvancedOptions
    {
        public AdvancedOptions() { }

        [DataMember(EmitDefaultValue = false)]
        public TimeSpan TimeoutWarning { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public TimeSpan TimeoutError { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<int> SuccessCodes { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<string> Semaphores { get; set; }

        [DataMember]
        public bool HasUntrackedChildProcesses { get; set; }

        [DataMember]
        public bool ProducesPathIndependentOutputs { get; set; }

        [DataMember]
        public bool OutputsMustRemainWritable { get; set; }

        [DataMember]
        public bool AllowPreserveOutputs { get; set; }
    }

    [DataContract]
    public class ProcessInputOutputDetails
    {
        public ProcessInputOutputDetails() { }

        [DataMember(EmitDefaultValue = false)]
        public List<string> FileDependencies { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<string> DirectoryDependencies { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<uint> OrderDependencies { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<string> FileOutputs { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<string> DirectoryOuputs { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<string> UntrackedPaths { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<string> UntrackedScopes { get; set; }
    }

    [DataContract]
    public class ServiceDetails
    {
        [DataMember]
        public bool IsService { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public uint ShutdownProcessPipId { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<uint> ServicePipDependencies { get; set; }

        [DataMember]
        public bool IsStartOrShutdownKind { get; set; }
    }
    #endregion

    [DataContract]
    public class IpcPipDetails
    {
        public IpcPipDetails() { }

        [DataMember(EmitDefaultValue = false)]
        public int IpcMonikerId { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string MessageBody { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string OutputFile { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<uint> ServicePipDependencies { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<string> FileDependencies { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<string> LazilyMaterializedDependencies { get; set; }

        [DataMember]
        public bool IsServiceFinalization { get; set; }

        [DataMember]
        public bool MustRunOnMaster { get; set; }
    }

    [DataContract]
    public class ValuePipDetails
    {
        public ValuePipDetails() { }

        [DataMember(EmitDefaultValue = false)]
        public string Symbol { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public int Qualifier { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string SpecFilePath { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string Location { get; set; }
    }

    [DataContract]
    public class SpecFilePipDetails
    {
        public SpecFilePipDetails() { }

        [DataMember(EmitDefaultValue = false)]
        public string SpecFile { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string DefinitionFilePath { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string Location { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public int ModuleId { get; set; }
    }

    [DataContract]
    public class ModulePipDetails
    {
        public ModulePipDetails() { }

        [DataMember(EmitDefaultValue = false)]
        public string Identity { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string DefinitionFilePath { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string DefinitionPath { get; set; }
    }

    [DataContract]
    public class HashSourceFilePipDetails
    {
        public HashSourceFilePipDetails() { }

        [DataMember(EmitDefaultValue = false)]
        public string FileHashed { get; set; }
    }

    [DataContract]
    public class SealDirectoryPipDetails
    {
        public SealDirectoryPipDetails() { }

        [DataMember(EmitDefaultValue = false)]
        public SealDirectoryKind Kind { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public bool Scrub { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string DirectoryRoot { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<string> Contents { get; set; }
    }

    [DataContract]
    public class WriteFilePipDetails
    {
        public WriteFilePipDetails() { }

        [DataMember(EmitDefaultValue = false)]
        public string Destination { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public WriteFileEncoding FileEncoding { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public List<string> Tags { get; set; }
    }
}