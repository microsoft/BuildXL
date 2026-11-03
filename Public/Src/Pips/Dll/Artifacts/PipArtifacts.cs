// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Artifacts
{
    /// <summary>
    /// Helpers functions for interacting with pip inputs/outputs
    /// </summary>
    public static class PipArtifacts
    {
        /// <summary>
        /// Processes a pip input, identifying whether it is a hash-only process dependency.
        /// </summary>
        /// <returns>True to continue traversal; false to stop.</returns>
        public delegate bool PipInputAction(FileOrDirectoryArtifact input, bool isHashOnly);

        /// <summary>
        /// Gets whether outputs must remain writable for the given pip
        /// </summary>
        public static bool IsOutputMustRemainWritablePip(Pip pip)
        {
            Contract.Requires(pip != null);

            switch (pip.PipType)
            {
                case PipType.Process:
                    return ((Process)pip).OutputsMustRemainWritable;
                case PipType.CopyFile:
                    return ((CopyFile)pip).OutputsMustRemainWritable;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Indicates whether pips of the given type can produce outputs
        /// </summary>
        public static bool CanProduceOutputs(PipType pipType)
        {
            switch (pipType)
            {
                case PipType.WriteFile:
                case PipType.CopyFile:
                case PipType.Process:
                case PipType.Ipc:
                case PipType.SealDirectory:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Checks if a pip can preserve the given output path
        /// </summary>
        /// <remarks>
        /// If the given output path represents a dynamic file output, 
        /// then we check whether the given path is within any allowlistedpath.
        /// </remarks>
        public static bool IsPreservedOutputByPip(Pip pip, AbsolutePath outputPath, PathTable pathTable, int sandBoxPreserveOuputTrstLevel, bool isDynamicFileOutput = false)
        {
            var process = pip as Process;
            if (process == null || !process.AllowPreserveOutputs || sandBoxPreserveOuputTrstLevel > process.PreserveOutputsTrustLevel)
            {
                return false;
            }

            if (process.PreserveOutputAllowlist.Length == 0)
            {
                // If allowlist is not given, we preserve all outputs of the given pip.
                return true;
            }

            Func<AbsolutePath, bool> checkFunc;
            if (isDynamicFileOutput)
            {
                // If the given path represents the dynamic file output, we cannot compare
                // that path with the paths in the allowlist as only declared outputs are specified
                // in the allowlist. Declared outputs are static file outputs and directory outputs.
                // That's why, we need to check whether the given file path is under one of the 
                // directory paths in the allowlist.
                checkFunc = (p) => outputPath.IsWithin(pathTable, p);
            }
            else
            {
                checkFunc = (p) => outputPath == p;
            }

            foreach (var path in process.PreserveOutputAllowlist)
            {
                if (checkFunc(path))
                {
                    // If the outputPath exists in the array, return true.
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the outputs produced by a pip and calls an action
        /// </summary>
        public static bool ForEachOutput(
            Pip pip,
            Func<FileOrDirectoryArtifact, bool> outputAction,
            Func<DirectoryArtifact, IReadOnlySet<FileArtifact>> getExistenceAssertionsUnderOpaqueDirectory,
            bool includeUncacheable)
        {
            bool result = true;

            switch (pip.PipType)
            {
                case PipType.CopyFile:
                    CopyFile copyFile = (CopyFile)pip;
                    result = outputAction(FileOrDirectoryArtifact.Create(copyFile.Destination));
                    break;
                case PipType.SealDirectory:
                    SealDirectory sealDirectory = (SealDirectory)pip;
                    result = outputAction(FileOrDirectoryArtifact.Create(sealDirectory.Directory));
                    break;
                case PipType.Process:
                    Process process = (Process)pip;
                    foreach (var output in process.FileOutputs)
                    {
                        if (includeUncacheable || output.CanBeReferencedOrCached())
                        {
                            if (!outputAction(FileOrDirectoryArtifact.Create(output.ToFileArtifact())))
                            {
                                return false;
                            }
                        }
                    }

                    foreach (var output in process.DirectoryOutputs)
                    {
                        if (!outputAction(FileOrDirectoryArtifact.Create(output)))
                        {
                            return false;
                        }

                        // Existence assertions under opaque directories are also considered output files.
                        foreach (var existenceAssertion in getExistenceAssertionsUnderOpaqueDirectory(output))
                        {
                            if (!outputAction(FileOrDirectoryArtifact.Create(existenceAssertion)))
                            {
                                return false;
                            }
                        }
                    }

                    break;
                case PipType.WriteFile:
                    WriteFile writeFile = (WriteFile)pip;
                    result = outputAction(FileOrDirectoryArtifact.Create(writeFile.Destination));
                    break;
                case PipType.Ipc:
                    IpcPip ipcPip = (IpcPip)pip;
                    result = outputAction(FileOrDirectoryArtifact.Create(ipcPip.OutputFile));
                    break;
            }

            return result;
        }

        /// <summary>
        /// Gets the inputs consumed by a pip and calls an action
        /// </summary>
        /// <remarks>
        /// Hash-only process inputs are content dependencies independently of IPC lazy-materialization policy.
        /// Include them for graph/content traversal, but not for filesystem access or byte materialization.
        /// </remarks>
        public static bool ForEachInput(
            Pip pip,
            Func<FileOrDirectoryArtifact, bool> inputAction,
            bool includeLazyInputs,
            Func<FileOrDirectoryArtifact, bool> overrideLazyInputAction = null,
            bool includeHashOnlyInputs = true)
        {
            return ForEachInput(pip, (input, isHashOnly) => inputAction(input), includeLazyInputs, overrideLazyInputAction, includeHashOnlyInputs);
        }

        /// <summary>
        /// Gets the inputs consumed by a pip and calls an action with whether each input is hash-only.
        /// </summary>
        /// <remarks>
        /// Only process hash-only inputs are flagged; IPC lazy materialization is an independent policy.
        /// </remarks>
        public static bool ForEachInput(
            Pip pip,
            PipInputAction inputAction,
            bool includeLazyInputs,
            Func<FileOrDirectoryArtifact, bool> overrideLazyInputAction = null,
            bool includeHashOnlyInputs = true)
        {
            // NOTE: Lazy inputs must be processed AFTER regular inputs
            // This behavior is required by FileContentManager.PopulateDepdencies
            bool result = true;

            switch (pip.PipType)
            {
                case PipType.CopyFile:
                    CopyFile copyFile = (CopyFile)pip;
                    result = inputAction(FileOrDirectoryArtifact.Create(copyFile.Source), isHashOnly: false);
                    break;
                case PipType.Process:
                    Process process = (Process)pip;
                    foreach (var input in process.RegularDependencies)
                    {
                        if (!inputAction(FileOrDirectoryArtifact.Create(input), isHashOnly: false))
                        {
                            return false;
                        }
                    }

                    if (includeHashOnlyInputs)
                    {
                        foreach (var input in process.HashOnlyFileDependencies)
                        {
                            if (!inputAction(FileOrDirectoryArtifact.Create(input), isHashOnly: true))
                            {
                                return false;
                            }
                        }
                    }

                    foreach (var input in process.RegularDirectoryDependencies)
                    {
                        if (!inputAction(FileOrDirectoryArtifact.Create(input), isHashOnly: false))
                        {
                            return false;
                        }
                    }

                    if (includeHashOnlyInputs)
                    {
                        foreach (var input in process.HashOnlyDirectoryDependencies)
                        {
                            if (!inputAction(FileOrDirectoryArtifact.Create(input), isHashOnly: true))
                            {
                                return false;
                            }
                        }
                    }

                    break;
                case PipType.SealDirectory:
                    SealDirectory sealDirectory = (SealDirectory)pip;
                    foreach (var input in sealDirectory.Contents)
                    {
                        if (!inputAction(FileOrDirectoryArtifact.Create(input), isHashOnly: false))
                        {
                            return false;
                        }
                    }

                    break;
                case PipType.Ipc:
                    IpcPip ipcPip = (IpcPip)pip;
                    foreach (var input in ipcPip.FileDependencies)
                    {
                        if (!inputAction(FileOrDirectoryArtifact.Create(input), isHashOnly: false))
                        {
                            return false;
                        }
                    }

                    foreach (var input in ipcPip.DirectoryDependencies)
                    {
                        if (!inputAction(FileOrDirectoryArtifact.Create(input), isHashOnly: false))
                        {
                            return false;
                        }
                    }

                    if (includeLazyInputs)
                    {
                        foreach (var input in ipcPip.LazilyMaterializedDependencies)
                        {
                            if (!(overrideLazyInputAction != null ? overrideLazyInputAction(input) : inputAction(input, isHashOnly: false)))
                            {
                                return false;
                            }
                        }
                    }

                    break;
            }

            return result;
        }
    }
}
