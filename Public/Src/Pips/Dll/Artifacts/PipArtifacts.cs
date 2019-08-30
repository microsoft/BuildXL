// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Pips.Artifacts
{
    /// <summary>
    /// Helpers functions for interacting with pip inputs/outputs
    /// </summary>
    public static class PipArtifacts
    {
        /// <summary>
        /// Gets whether outputs must remain writable for the given pip
        /// </summary>
        public static bool IsOutputMustRemainWritablePip(Pip pip)
        {
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
        /// then we check whether the given path is within any whitelistedpath.
        /// </remarks>
        public static bool IsPreservedOutputByPip(Pip pip, AbsolutePath outputPath, PathTable pathTable, bool isDynamicFileOutput = false)
        {
            var process = pip as Process;
            if (process == null || !process.AllowPreserveOutputs)
            {
                return false;
            }

            if (process.PreserveOutputWhitelist.Length == 0)
            {
                // If whitelist is not given, we preserve all outputs of the given pip.
                return true;
            }

            Func<AbsolutePath, bool> checkFunc;
            if (isDynamicFileOutput)
            {
                // If the given path represents the dynamic file output, we cannot compare
                // that path with the paths in the whitelist as only declared outputs are specified
                // in the whitelist. Declared outputs are static file outputs and directory outputs.
                // That's why, we need to check whether the given file path is under one of the 
                // directory paths in the whitelist.
                checkFunc = (p) => outputPath.IsWithin(pathTable, p);
            }
            else
            {
                checkFunc = (p) => outputPath == p;
            }

            foreach (var path in process.PreserveOutputWhitelist)
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
        public static bool ForEachOutput(Pip pip, Func<FileOrDirectoryArtifact, bool> outputAction, bool includeUncacheable)
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
        public static bool ForEachInput(
            Pip pip,
            Func<FileOrDirectoryArtifact, bool> inputAction,
            bool includeLazyInputs,
            Func<FileOrDirectoryArtifact, bool> overrideLazyInputAction = null)
        {
            // NOTE: Lazy inputs must be processed AFTER regular inputs
            // This behavior is required by FileContentManager.PopulateDepdencies
            bool result = true;

            switch (pip.PipType)
            {
                case PipType.CopyFile:
                    CopyFile copyFile = (CopyFile)pip;
                    result = inputAction(FileOrDirectoryArtifact.Create(copyFile.Source));
                    break;
                case PipType.Process:
                    Process process = (Process)pip;
                    foreach (var input in process.Dependencies)
                    {
                        if (!inputAction(FileOrDirectoryArtifact.Create(input)))
                        {
                            return false;
                        }
                    }

                    foreach (var input in process.DirectoryDependencies)
                    {
                        if (!inputAction(FileOrDirectoryArtifact.Create(input)))
                        {
                            return false;
                        }
                    }

                    break;
                case PipType.SealDirectory:
                    SealDirectory sealDirectory = (SealDirectory)pip;
                    foreach (var input in sealDirectory.Contents)
                    {
                        if (!inputAction(FileOrDirectoryArtifact.Create(input)))
                        {
                            return false;
                        }
                    }

                    break;
                case PipType.Ipc:
                    IpcPip ipcPip = (IpcPip)pip;
                    foreach (var input in ipcPip.FileDependencies)
                    {
                        if (!inputAction(FileOrDirectoryArtifact.Create(input)))
                        {
                            return false;
                        }
                    }

                    foreach (var input in ipcPip.DirectoryDependencies)
                    {
                        if (!inputAction(FileOrDirectoryArtifact.Create(input)))
                        {
                            return false;
                        }
                    }

                    if (includeLazyInputs)
                    {
                        overrideLazyInputAction = overrideLazyInputAction ?? inputAction;
                        foreach (var input in ipcPip.LazilyMaterializedDependencies)
                        {
                            if (!overrideLazyInputAction(input))
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
