// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Specification of a Process invocation via an IPC message.
    /// </summary>
    public sealed class IpcPip : Pip
    {
        /// <nodoc />
        public IpcPip(
            IpcClientInfo ipcInfo,
            PipData arguments,
            FileArtifact outputFile,
            ReadOnlyArray<PipId> servicePipDependencies,
            ReadOnlyArray<FileArtifact> fileDependencies,
            ReadOnlyArray<DirectoryArtifact> directoryDependencies,
            ReadOnlyArray<FileOrDirectoryArtifact> skipMaterializationFor,
            ReadOnlyArray<StringId> tags,
            bool isServiceFinalization,
            bool mustRunOnMaster,
            PipProvenance provenance)
        {
            Contract.Requires(ipcInfo != null);
            Contract.Requires(arguments.IsValid);
            Contract.Requires(outputFile.IsValid);
            Contract.Requires(servicePipDependencies.IsValid);
            Contract.Requires(fileDependencies.IsValid);
            Contract.Requires(directoryDependencies.IsValid);
            Contract.Requires(skipMaterializationFor.IsValid);
            Contract.RequiresForAll(servicePipDependencies, dependency => dependency.IsValid);
            Contract.RequiresForAll(fileDependencies, dependency => dependency.IsValid);
            Contract.RequiresForAll(directoryDependencies, dependency => dependency.IsValid);

            IpcInfo = ipcInfo;
            MessageBody = arguments;
            OutputFile = outputFile;
            ServicePipDependencies = servicePipDependencies;
            FileDependencies = fileDependencies;
            LazilyMaterializedDependencies = skipMaterializationFor;
            Tags = tags;
            IsServiceFinalization = isServiceFinalization;
            MustRunOnMaster = mustRunOnMaster;
            Provenance = provenance;
            DirectoryDependencies = directoryDependencies;
        }

        /// <inheritdoc />
        public override PipType PipType => PipType.Ipc;

        /// <summary>
        /// All the necessary information needed to execute this pip in-proc
        /// via the IPC mechanisms provided by the BuildXL.Ipc assembly.
        /// </summary>
        public IpcClientInfo IpcInfo { get; }

        /// <summary>
        /// The arguments to the IPC call.
        /// </summary>
        public PipData MessageBody { get; }

        /// <summary>
        /// Service pip dependencies.
        /// </summary>
        public ReadOnlyArray<PipId> ServicePipDependencies { get; }

        /// <summary>
        /// Input file artifact dependencies.
        /// </summary>
        public ReadOnlyArray<FileArtifact> FileDependencies { get; }

        /// <summary>
        /// Input directory artifact dependencies.
        /// </summary>
        public ReadOnlyArray<DirectoryArtifact> DirectoryDependencies { get; }

        /// <summary>
        /// Artifacts (files and/or directories) not to materialize eagerly.
        /// </summary>
        /// <remarks>
        /// IPC pips may want to use this option when they will explicitly request file/directory materialization
        /// from BuildXL, via a BuildXL service identified by the Transformer.getIpcServerMoniker()
        /// moniker, just before the files are needed. This makes sense for pips that expect that often
        /// times they will not have to access the actual files on disk.
        /// </remarks>
        public ReadOnlyArray<FileOrDirectoryArtifact> LazilyMaterializedDependencies { get; }

        /// <summary>
        /// Output file where the response from the server will be written.
        /// </summary>
        public FileArtifact OutputFile { get; }

        /// <inheritdoc />
        public override ReadOnlyArray<StringId> Tags { get; }

        /// <inheritdoc />
        public override PipProvenance Provenance { get; }

        /// <summary>Whether this pip is used as a finalization of a service pip</summary>
        public bool IsServiceFinalization { get; }

        /// <summary>Whether this pip must be executed on master in a distributed build.</summary>
        public bool MustRunOnMaster { get; }

        /// <summary>
        /// Clone and override select properties.
        /// </summary>
        public IpcPip Override(
            IpcClientInfo ipcInfo = null,
            PipData? messageBody = null,
            FileArtifact? outputFile = null,
            ReadOnlyArray<PipId>? servicePipDependencies = null,
            ReadOnlyArray<FileArtifact>? fileDependencies = null,
            ReadOnlyArray<DirectoryArtifact>? directoryDependencies = null,
            ReadOnlyArray<FileOrDirectoryArtifact>? lazilyMaterializedDependencies = null,
            ReadOnlyArray<StringId>? tags = null,
            bool? isServiceFinalization = null,
            bool? mustRunOnMaster = null,
            PipProvenance provenance = null)
        {
            return new IpcPip(
                ipcInfo ?? IpcInfo,
                messageBody ?? MessageBody,
                outputFile ?? OutputFile,
                servicePipDependencies ?? ServicePipDependencies,
                fileDependencies ?? FileDependencies,
                directoryDependencies ?? DirectoryDependencies,
                lazilyMaterializedDependencies ?? LazilyMaterializedDependencies,
                tags ?? Tags,
                isServiceFinalization ?? IsServiceFinalization,
                mustRunOnMaster ?? MustRunOnMaster,
                provenance ?? Provenance);
        }

        /// <summary>
        /// Creates an IpcPip from a single string representing IPC operation payload.
        ///
        /// The standard output property of the created pip is set to <paramref name="workingDir"/>\stdout.txt
        /// </summary>
        internal static IpcPip CreateFromStringPayload(
            PipExecutionContext context,
            AbsolutePath workingDir,
            IpcClientInfo ipcInfo,
            string operationPayload,
            PipProvenance provenance,
            FileArtifact outputFile = default(FileArtifact),
            IEnumerable<PipId> servicePipDependencies = null,
            IEnumerable<FileArtifact> fileDependencies = null,
            IEnumerable<DirectoryArtifact> directoryDependencies = null,
            IEnumerable<StringId> tags = null,
            bool isServiceFinalization = false,
            bool mustRunOnMaster = false)
        {
            var stdoutPath = workingDir.Combine(context.PathTable, PathAtom.Create(context.StringTable, "stdout.txt"));
            var stdoutFile = outputFile.IsValid ? outputFile : FileArtifact.CreateOutputFile(stdoutPath);

            var pipDataBuilder = new PipDataBuilder(context.StringTable);
            pipDataBuilder.Add(operationPayload);

            return new IpcPip(
                ipcInfo,
                arguments: pipDataBuilder.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                outputFile: stdoutFile,
                servicePipDependencies: ToReadOnlyArray(servicePipDependencies),
                fileDependencies: ToReadOnlyArray(fileDependencies),
                directoryDependencies: ToReadOnlyArray(directoryDependencies),
                skipMaterializationFor: ReadOnlyArray<FileOrDirectoryArtifact>.Empty,
                tags: ToReadOnlyArray(tags),
                isServiceFinalization: isServiceFinalization,
                mustRunOnMaster: mustRunOnMaster,
                provenance: provenance);
        }

        private static ReadOnlyArray<T> ToReadOnlyArray<T>(IEnumerable<T> e)
        {
            return e == null
                ? ReadOnlyArray<T>.Empty
                : ReadOnlyArray<T>.From(e);
        }

        internal static IpcPip InternalDeserialize(PipReader reader)
        {            
            bool hasProvenance = reader.ReadBoolean();
            return new IpcPip(
                ipcInfo: IpcClientInfo.Deserialize(reader),
                arguments: reader.ReadPipData(),
                outputFile: reader.ReadFileArtifact(),
                servicePipDependencies: reader.ReadReadOnlyArray(reader1 => ((PipReader)reader1).ReadPipId()),
                fileDependencies: reader.ReadReadOnlyArray(reader1 => reader1.ReadFileArtifact()),
                directoryDependencies: reader.ReadReadOnlyArray(reader1 => reader1.ReadDirectoryArtifact()),
                skipMaterializationFor: reader.ReadReadOnlyArray(reader1 => reader1.ReadFileOrDirectoryArtifact()),
                tags: reader.ReadReadOnlyArray(reader1 => reader1.ReadStringId()),
                isServiceFinalization: reader.ReadBoolean(),
                mustRunOnMaster: reader.ReadBoolean(),
                provenance: hasProvenance ? reader.ReadPipProvenance() : null);
        }

        internal override void InternalSerialize(PipWriter writer)
        {
            var hasProvenance = Provenance != null;
            writer.Write(hasProvenance);
            IpcInfo.Serialize(writer);
            writer.Write(MessageBody);
            writer.Write(OutputFile);
            writer.Write(ServicePipDependencies, (w, v) => ((PipWriter)w).Write(v));
            writer.Write(FileDependencies, (w, v) => w.Write(v));
            writer.Write(DirectoryDependencies, (w, v) => w.Write(v));
            writer.Write(LazilyMaterializedDependencies, (w, v) => w.Write(v));
            writer.Write(Tags, (w, v) => w.Write(v));
            writer.Write(IsServiceFinalization);
            writer.Write(MustRunOnMaster);
            if (hasProvenance)
            {
                writer.Write(Provenance);
            }
        }
    }
}
