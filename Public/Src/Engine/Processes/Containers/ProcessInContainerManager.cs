// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Processes.Containers
{
    /// <summary>
    /// The result of merging outputs back into their original location
    /// </summary>
    internal enum MergeResult
    {
        /// <nodoc/>
        Success,

        /// <nodoc/>
        DisallowedDoubleWrite,

        /// <summary>
        /// An unexpected failure. E.g. access denied on creation.
        /// </summary>
        Failure,
    }

    /// <summary>
    /// This class manages different aspects of a process running in a Helium container.
    /// </summary>
    public sealed class ProcessInContainerManager
    {
        private readonly LoggingContext m_loggingContext;
        private readonly PathTable m_pathTable;

        /// <nodoc/>
        public ProcessInContainerManager(LoggingContext loggingContext, PathTable pathTable)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(pathTable != null);

            m_loggingContext = loggingContext;
            m_pathTable = pathTable;
        }

        /// <summary>
        /// Creates a container configuration for a given process
        /// </summary>
        public ContainerConfiguration CreateContainerConfigurationForProcess(Process process)
        {
            Contract.Requires(process != null);

            // TODO: In the future we need to consider input dependencies to make the isolation include them, not only outputs
            if (process.ContainerIsolationLevel.IsolateInputs())
            {
                throw new NotImplementedException("Input isolation is not implemented yet!");
            }

            CreateMappingForOutputs(process, out var originalDirectories, out var redirectedDirectories);

            return new ContainerConfiguration(m_pathTable, redirectedDirectories, originalDirectories);
        }

        /// <summary>
        /// Returns the redirected output corresponding to the given original declared output file
        /// </summary>
        /// <remarks>
        /// The provided original output is assumed to be part of the container configuration
        /// </remarks>
        public AbsolutePath GetRedirectedDeclaredOutputFile(AbsolutePath originalOutput, ContainerConfiguration containerConfiguration)
        {
            if (!TryGetRedirectedDeclaredOutputFile(originalOutput, containerConfiguration, out var redirectedOutputFile))
            {
                Contract.Assert(false, $"A redirected directory for '{originalOutput.GetParent(m_pathTable).ToString(m_pathTable)}' should be present in the container configuration");
            }

            return redirectedOutputFile;
        }

        /// <summary>
        /// Tries to return the redirected output corresponding to the given original declared output file
        /// </summary>
        public bool TryGetRedirectedDeclaredOutputFile(AbsolutePath originalOutput, ContainerConfiguration containerConfiguration, out AbsolutePath redirectedOutputFile)
        {
            AbsolutePath containingDirectory = originalOutput.GetParent(m_pathTable);
            if (!containerConfiguration.OriginalDirectories.TryGetValue(containingDirectory, out var redirectedDirectories))
            {
                redirectedOutputFile = AbsolutePath.Invalid;
                return false;
            }

            var redirectedDirectory = redirectedDirectories.Single();
            redirectedOutputFile = redirectedDirectory.Path.Combine(m_pathTable, originalOutput.GetName(m_pathTable));

            return true;
        }

        /// <summary>
        /// Tries to return the redirected output corresponding to the given original opaque directory root and opaque output
        /// </summary>
        public bool TryGetRedirectedOpaqueFile(AbsolutePath originalOutput, AbsolutePath sharedOpaqueRoot, ContainerConfiguration containerConfiguration, out AbsolutePath redirectedOutputFile)
        {
            if (!containerConfiguration.OriginalDirectories.TryGetValue(sharedOpaqueRoot, out var redirectedDirectories))
            {
                redirectedOutputFile = AbsolutePath.Invalid;
                return false;
            }

            var redirectedDirectory = redirectedDirectories.Single();
            redirectedOutputFile = originalOutput.Relocate(m_pathTable, sharedOpaqueRoot, redirectedDirectory.Path);
            return true;
        }

        /// <summary>
        /// Merges the outputs of the given redirected process to its original location based on configured policies
        /// </summary>
        public Task<bool> MergeOutputsIfNeededAsync(Process process, ContainerConfiguration containerConfiguration, PipExecutionContext pipExecutionContext, IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>> sharedDynamicWrites)
        {
            Contract.Requires(containerConfiguration != null);
            Contract.Requires(pipExecutionContext != null);

            if (!process.NeedsToRunInContainer)
            {
                return Task.FromResult(true);
            }

            return Task.Run(
                () =>
                {
                    try
                    {
                        using (var createdDirectoriesWrapper = Pools.AbsolutePathSetPool.GetInstance())
                        {
                            HashSet<AbsolutePath> cretedDirectories = createdDirectoriesWrapper.Instance;
                            var result = HardlinkAllDeclaredOutputs(process, containerConfiguration, pipExecutionContext, cretedDirectories);
                            if (result != MergeResult.Success)
                            {
                                return false;
                            }

                            result = HardlinkOpaqueDirectories(process, containerConfiguration, pipExecutionContext, sharedDynamicWrites, cretedDirectories);
                            if (result != MergeResult.Success)
                            {
                                return false;
                            }
                        }
                    }
                    catch (BuildXLException ex)
                    {
                        // The operation above may throw, and in that case we don't want to propagate the exception, but grab the error message
                        // and interpret it as a merge failure
                        Tracing.Logger.Log.FailedToMergeOutputsToOriginalLocation(m_loggingContext, process.SemiStableHash, process.GetDescription(pipExecutionContext), ex.Message);
                        return false;
                    }

                    return true;
                });
        }

        private MergeResult HardlinkAllDeclaredOutputs(Process process, ContainerConfiguration containerConfiguration, PipExecutionContext pipExecutionContext, HashSet<AbsolutePath> createdDirectories)
        {
            if (!process.ContainerIsolationLevel.IsolateOutputFiles())
            {
                return MergeResult.Success;
            }

            foreach (FileArtifactWithAttributes fileOutput in process.FileOutputs)
            {
                // If the output is not there, just continue. Checking all outputs are present already happened, so this means an optional output.
                // If the output is a WCI reparse point or tombstone file, continue as well. This means the output was either not actually generated (but a file with that name was read) or the file was generated and later deleted.
                string sourcePath = GetRedirectedOutputPathForDeclaredOutput(containerConfiguration, pipExecutionContext, fileOutput.Path);
                var sourcePathExists = FileUtilities.Exists(sourcePath);
                if (!sourcePathExists || FileUtilities.IsWciReparseArtifact(sourcePath))
                {
                    // If the declared output is a WCI artifact, that means it is not a real output. If we reached this point it is because the sandbox process executor determined this output
                    // was an optional one, and the content was actually not produced. Since when storing the process into the cache we will try to use the redirected file to retrieve its content,
                    // make sure we remove the WCI artifact, so the file actually appears as absent
                    // Observe this is not really needed for the case of opaque directories, since the content that gets reported is based on the existence of the file in its original location, which
                    // is not hardlinked if the file is a WCI artifact. We could remove them in the opaque case as well, just for a matter of symetry, but since this may have perf implications, we are not
                    // doing that right now.
                    if (sourcePathExists)
                    {
                        FileUtilities.DeleteFile(sourcePath);
                    }

                    continue;
                }

                string destinationPath = fileOutput.Path.ToString(m_pathTable);
                var mergeResult = TryCreateHardlinkForOutput(fileOutput.Path.Expand(m_pathTable), fileOutput.RewriteCount, sourcePath, process, pipExecutionContext, createdDirectories);
                if (mergeResult != MergeResult.Success)
                {
                    return mergeResult;
                }
            }
            
            return MergeResult.Success;
        }

        private MergeResult HardlinkOpaqueDirectories(
            Process process, 
            ContainerConfiguration containerConfiguration, 
            PipExecutionContext pipExecutionContext, 
            IReadOnlyDictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>> sharedDynamicWrites, 
            HashSet<AbsolutePath> createdDirectories)
        {
            bool isolateSharedOpaques = process.ContainerIsolationLevel.IsolateSharedOpaqueOutputDirectories();
            bool isolateExclusiveOpaques = process.ContainerIsolationLevel.IsolateExclusiveOpaqueOutputDirectories();

            // Shortcut the iteration of output directories are not isolated at all
            if (!isolateExclusiveOpaques && !isolateSharedOpaques)
            {
                return MergeResult.Success;
            }

            foreach (DirectoryArtifact directoryOutput in process.DirectoryOutputs)
            {
                if (directoryOutput.IsSharedOpaque && isolateSharedOpaques)
                {
                    AbsolutePath redirectedDirectory = GetRedirectedDirectoryForOutputContainer(containerConfiguration, directoryOutput.Path).Path;
                    
                    // Here we don't need to check for WCI reparse points. We know those outputs are there based on what detours is saying.
                    // However, we need to check for tombstone files: those represent deleted files in some scenarios (for example, moves), so we don't hardlink them
                    var sharedOpaqueContent = sharedDynamicWrites[directoryOutput.Path];
                    foreach (AbsolutePath sharedOpaqueFile in sharedOpaqueContent)
                    {
                        string sourcePath = sharedOpaqueFile.Relocate(m_pathTable, directoryOutput.Path, redirectedDirectory).ToString(m_pathTable);
                        // The file may not exist because the pip could have created it but later deleted it, or be a tombstone file 
                        if (!FileUtilities.Exists(sourcePath) || FileUtilities.IsWciTombstoneFile(sourcePath))
                        {
                            continue;
                        }

                        ExpandedAbsolutePath destinationPath = sharedOpaqueFile.Expand(m_pathTable);
                        // Files in an opaque always have rewrite count 1
                        var result = TryCreateHardlinkForOutput(destinationPath, rewriteCount: 1, sourcePath, process, pipExecutionContext, createdDirectories);
                        if (result != MergeResult.Success)
                        {
                            return result;
                        }
                    }
                }
                else if (!directoryOutput.IsSharedOpaque && isolateExclusiveOpaques)
                {
                    // We need to enumerate to discover the content of an exclusive opaque, and also skip the potential reparse artifacts
                    // TODO: Enumeration will happen again when the file content manager tries to discover the content of the exclusive opaque. Consider doing this only once instead.

                    // An output directory should only have one redirected path
                    ExpandedAbsolutePath redirectedDirectory = containerConfiguration.OriginalDirectories[directoryOutput.Path].Single();
                    foreach (string exclusiveOpaqueFile in Directory.EnumerateFiles(redirectedDirectory.ExpandedPath, "*", SearchOption.AllDirectories))
                    {
                        if (FileUtilities.IsWciReparseArtifact(exclusiveOpaqueFile))
                        {
                            continue;
                        }

                        AbsolutePath exclusiveOpaqueFilePath = AbsolutePath.Create(m_pathTable, exclusiveOpaqueFile);
                        AbsolutePath outputFile = exclusiveOpaqueFilePath.Relocate(m_pathTable, redirectedDirectory.Path, directoryOutput.Path);
                        // Files in an opaque always have rewrite count 1
                        var result = TryCreateHardlinkForOutput(outputFile.Expand(m_pathTable), rewriteCount: 1, exclusiveOpaqueFile, process, pipExecutionContext, createdDirectories);
                        if (result != MergeResult.Success)
                        {
                            return result;
                        }
                    }
                }
            }

            return MergeResult.Success;
        }

        private MergeResult TryCreateHardlinkForOutput(ExpandedAbsolutePath fileOutput, int rewriteCount, string sourcePath, Process process, PipExecutionContext pipExecutionContext, HashSet<AbsolutePath> createdDirectories)
        {
            if (!CanMerge(fileOutput.ExpandedPath, rewriteCount, process, pipExecutionContext, out bool isDisallowed, out bool shouldDelete))
            {
                if (isDisallowed)
                {
                    Tracing.Logger.Log.DisallowedDoubleWriteOnMerge(m_loggingContext, process.SemiStableHash, process.GetDescription(pipExecutionContext), fileOutput.ExpandedPath, sourcePath);
                    return MergeResult.DisallowedDoubleWrite;
                }

                // The file cannot be merged, but no merging is allowed
                return MergeResult.Success;
            }

            if (shouldDelete)
            {
                FileUtilities.DeleteFile(fileOutput.ExpandedPath, waitUntilDeletionFinished: true);
            }
            else
            {
                // If we need to delete the target file, the directory is already created. So only do this otherwise.

                // Make sure the destination directory exists before trying to create the hardlink
                var containingDirectory = fileOutput.Path.GetParent(m_pathTable);
                if (!createdDirectories.Contains(containingDirectory))
                {
                    string destinationDirectory = containingDirectory.ToString(m_pathTable);

                    if (!FileUtilities.DirectoryExistsNoFollow(destinationDirectory))
                    {
                        FileUtilities.CreateDirectory(destinationDirectory);
                    }

                    createdDirectories.Add(containingDirectory);
                }
            }

            var createHardlinkStatus = FileUtilities.TryCreateHardLink(fileOutput.ExpandedPath, sourcePath);

            if (createHardlinkStatus != CreateHardLinkStatus.Success)
            {
                // Maybe somebody else raced and successfully created the hardlink since the last time we checked,
                // so let's check again before failing
                if (!CanMerge(fileOutput.ExpandedPath, rewriteCount, process, pipExecutionContext, out isDisallowed, out bool _))
                {
                    if (isDisallowed)
                    {
                        Tracing.Logger.Log.DisallowedDoubleWriteOnMerge(m_loggingContext, process.SemiStableHash, process.GetDescription(pipExecutionContext), fileOutput.ExpandedPath, sourcePath);
                        return MergeResult.DisallowedDoubleWrite;
                    }

                    // The file cannot be merged, but no merging is allowed
                    return MergeResult.Success;
                }

                Tracing.Logger.Log.FailedToCreateHardlinkOnMerge(m_loggingContext, process.SemiStableHash, process.GetDescription(pipExecutionContext), fileOutput.ExpandedPath, sourcePath, createHardlinkStatus.ToString());
                return MergeResult.Failure;
            }

            return MergeResult.Success;
        }

        private string GetRedirectedOutputPathForDeclaredOutput(ContainerConfiguration containerConfiguration, PipExecutionContext pipExecutionContext, AbsolutePath fileOutput)
        {
            AbsolutePath containingDirectory = fileOutput.GetParent(m_pathTable);
            var redirectedDirectory = GetRedirectedDirectoryForOutputContainer(containerConfiguration, containingDirectory);
            var sourcePath = Path.Combine(redirectedDirectory.ExpandedPath, fileOutput.GetName(m_pathTable).ToString(pipExecutionContext.StringTable));

            return sourcePath;
        }

        private ExpandedAbsolutePath GetRedirectedDirectoryForOutputContainer(ContainerConfiguration containerConfiguration, AbsolutePath containingDirectory)
        {
            if (!containerConfiguration.OriginalDirectories.TryGetValue(containingDirectory, out var redirectedDirectories))
            {
                Contract.Assert(false, $"A redirected directory for '{containingDirectory.ToString(m_pathTable)}' should be present in the container configuration");
            }

            var redirectedDirectory = redirectedDirectories.Single();
            return redirectedDirectory;
        }

        private bool CanMerge(string destPath, int rewriteCount, Process process, PipExecutionContext pipExecutionContext, out bool isDisallowed, out bool shouldDelete)
        {
            // If the file already exists, we fail or ignore depending on the configured policy
            if (FileUtilities.Exists(destPath))
            {
                // The file exists on destination, but this artifact has rewrite count > 1.
                // This means presence of the file is expected and this is a legit rewrite. 
                // The BuildXL Scheduler guarantees the file we just found has the proper rewrite count
                if (rewriteCount > 1)
                {
                    isDisallowed = false;
                    shouldDelete = true;
                    return true;
                }

                if (process.DoubleWritePolicy == DoubleWritePolicy.DoubleWritesAreErrors)
                {
                    // Error logged by the caller
                    isDisallowed = true;
                    shouldDelete = false;
                    return false;
                }

                // DoubleWritePolicy.UnsafeFirstDoubleWriteWins case
                Contract.Assert(process.DoubleWritePolicy == DoubleWritePolicy.UnsafeFirstDoubleWriteWins);

                // Just log a verbose message for tracking purposes
                Tracing.Logger.Log.DoubleWriteAllowedDueToPolicy(m_loggingContext, process.SemiStableHash, process.GetDescription(pipExecutionContext), destPath);

                isDisallowed = false;
                shouldDelete = false;

                return false;
            }

            isDisallowed = false;
            shouldDelete = false;
            return true;
        }

        private void CreateMappingForOutputs(Process process, out MultiValueDictionary<AbsolutePath, ExpandedAbsolutePath> originalDirectories, out MultiValueDictionary<ExpandedAbsolutePath, ExpandedAbsolutePath> redirectedDirectories)
        {
            // Collect all predicted outputs (directories and files) for the given process
            var directories = CollectAllOutputDirectories(m_pathTable, process);

            // In order to keep the filter configuration to its minimum, let's remove directories that are nested within each other
            var dedupDirectories = AbsolutePathUtilities.CollapseDirectories(directories, m_pathTable, out var originalToCollapsedMapping);

            var stringTable = m_pathTable.StringTable;
            var reserveFoldersResolver = new ReserveFoldersResolver(new object());

            originalDirectories = new MultiValueDictionary<AbsolutePath, ExpandedAbsolutePath>(originalToCollapsedMapping.Count);
            redirectedDirectories = new MultiValueDictionary<ExpandedAbsolutePath, ExpandedAbsolutePath>(dedupDirectories.Count, ExpandedAbsolutePathEqualityComparer.Instance);

            // Map from original dedup directories to unique redirected directories
            var uniqueRedirectedDirectories = new Dictionary<AbsolutePath, ExpandedAbsolutePath>(dedupDirectories.Count);

            foreach (var kvp in originalToCollapsedMapping)
            {
                AbsolutePath originalDirectory = kvp.Key;
                AbsolutePath originalCollapsedDirectory = kvp.Value;

                if (!uniqueRedirectedDirectories.TryGetValue(originalCollapsedDirectory, out var uniqueRedirectedDirectory))
                {
                    uniqueRedirectedDirectory = GetUniqueRedirectedDirectory(process, ref reserveFoldersResolver, originalCollapsedDirectory).Expand(m_pathTable);

                    uniqueRedirectedDirectories.Add(originalCollapsedDirectory, uniqueRedirectedDirectory);
                    redirectedDirectories.Add(uniqueRedirectedDirectory, originalCollapsedDirectory.Expand(m_pathTable));
                }

                // Let's reconstruct the redirected directory
                var redirectedDirectory = originalDirectory.Relocate(m_pathTable, originalCollapsedDirectory, uniqueRedirectedDirectory.Path);

                originalDirectories.Add(originalDirectory, redirectedDirectory.Expand(m_pathTable));
            }
        }

        /// <summary>
        /// Returns a unique redirected directory (located under the process unique redirected directory root) that is stable across builds
        /// </summary>
        private AbsolutePath GetUniqueRedirectedDirectory(Process process, ref ReserveFoldersResolver reserveFoldersResolver, AbsolutePath originalDirectory)
        {
            var name = originalDirectory.GetName(m_pathTable);
            var count = reserveFoldersResolver.GetNextId(name);
            var stringTable = m_pathTable.StringTable;

            var pathAtom = count == 0
                ? name
                : PathAtom.Create(stringTable, $"{name.ToString(stringTable)}_{count}");

            var redirectedDirectory = process.UniqueRedirectedDirectoryRoot.Combine(m_pathTable, pathAtom);
            return redirectedDirectory;
        }

        private ICollection<AbsolutePath> CollectAllOutputDirectories(PathTable pathTable, Process process)
        {
            var outputDirectories = new HashSet<AbsolutePath>();

            if (process.ContainerIsolationLevel.IsolateOutputFiles())
            {
                outputDirectories.AddRange(process.FileOutputs.Select(fileOutput => fileOutput.Path.GetParent(pathTable)));
            }

            // If both shared and exclusive directories are configured, let's just iterate once
            if (process.ContainerIsolationLevel.IsolateOutputDirectories())
            {
                outputDirectories.AddRange(process.DirectoryOutputs.Select(directoryOutput => directoryOutput.Path));
            }
            else
            {
                if (process.ContainerIsolationLevel.IsolateSharedOpaqueOutputDirectories())
                {
                    outputDirectories.AddRange(process.DirectoryOutputs.Where(directoryOutput => directoryOutput.IsSharedOpaque).Select(directoryOutput => directoryOutput.Path));
                }

                if (process.ContainerIsolationLevel.IsolateExclusiveOpaqueOutputDirectories())
                {
                    outputDirectories.AddRange(process.DirectoryOutputs.Where(directoryOutput => !directoryOutput.IsSharedOpaque).Select(directoryOutput => directoryOutput.Path));
                }
            }

            return outputDirectories;
        }
    }
}
