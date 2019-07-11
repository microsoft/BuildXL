// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using Microsoft.Windows.ProjFS;
using System.Diagnostics;
using BuildXL.Cache.ContentStore.Logging;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Vfs;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Vfs.Provider
{
    using Utils = Microsoft.Windows.ProjFS.Utils;

    /// <summary>
    /// This is a simple file system "reflector" provider.  It projects files and directories from
    /// a directory called the "layer root" into the virtualization root, also called the "scratch root".
    /// </summary>
    internal class VfsProvider
    {
        private Logger Log;
        private VfsCasConfiguration Configuration;
        private VfsContentManager ContentManager;
        private VfsTree Tree;

        private static readonly byte[] s_contentId = new byte[] { 0 };
        private static readonly byte[] s_providerId = new byte[] { 1 };

        // These variables hold the layer and scratch paths.
        private readonly int currentProcessId = Process.GetCurrentProcess().Id;

        private readonly VirtualizationInstance virtualizationInstance;

        // TODO: Cache enumeration listings
        private readonly ObjectCache<(VfsDirectoryNode node, int version), List<VfsNode>> enumerationCache = new ObjectCache<(VfsDirectoryNode node, int version), List<VfsNode>>(1103);
        private readonly ConcurrentDictionary<Guid, ActiveEnumeration> activeEnumerations = new ConcurrentDictionary<Guid, ActiveEnumeration>();
        private readonly ConcurrentDictionary<int, CancellationTokenSource> activeCommands = new ConcurrentDictionary<int, CancellationTokenSource>();

        public VfsProvider(Logger log, VfsCasConfiguration configuration, VfsContentManager contentManager, VfsTree tree)
        {
            Log = log;
            Configuration = configuration;
            Tree = tree;
            ContentManager = contentManager;

            // Enable notifications if the user requested them.
            var notificationMappings = new List<NotificationMapping>();

            try
            {
                // This will create the virtualization root directory if it doesn't already exist.
                virtualizationInstance = new VirtualizationInstance(
                    configuration.VfsRootPath.Path,
                    poolThreadCount: 0,
                    concurrentThreadCount: 0,
                    enableNegativePathCache: false,
                    notificationMappings: notificationMappings);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to create VirtualizationInstance.");
                throw;
            }
        }

        public bool StartVirtualization()
        {
            return Log.PerformOperation(Configuration.VfsRootPath.Path, () =>
            {
                virtualizationInstance.OnQueryFileName = QueryFileNameCallback;
                virtualizationInstance.OnNotifyNewFileCreated = OnNotifyNewFileCreated;

                RequiredCallbacks requiredCallbacks = new RequiredCallbacks(this);
                HResult hr = virtualizationInstance.StartVirtualizing(requiredCallbacks);
                if (hr != HResult.Ok)
                {
                    Log.Error("Failed to start virtualization instance: {Result}", hr);
                    return false;
                }

                foreach (var mount in Configuration.VirtualizationMounts)
                {
                    hr = Log.PerformOperation(mount.Key, () =>
                    {
                        return virtualizationInstance.MarkDirectoryAsPlaceholder(
                            targetDirectoryPath: (Configuration.VfsMountRootPath / mount.Key).Path,
                            contentId: s_contentId,
                            providerId: s_providerId);
                    },
                    caller: "MarkMountDirectoryAsPlaceholder");

                    if (hr != HResult.Ok)
                    {
                        Log.Error("Failed to start virtualization instance: {Result}", hr);
                        return false;
                    }
                }

                return true;
            });
        }

        private void OnNotifyNewFileCreated(
            string relativePath,
            bool isDirectory,
            uint triggeringProcessId,
            string triggeringProcessImageFileName,
            out NotificationType notificationMask)
        {
            notificationMask = NotificationType.UseExistingMask;

            if (isDirectory)
            {
                virtualizationInstance.MarkDirectoryAsPlaceholder(
                    Path.Combine(Configuration.VfsRootPath.Path, relativePath),
                    contentId: s_contentId,
                    providerId: s_providerId);
            }
        }

        private HResult HandleCommandAsynchronously(int commandId, Func<CancellationToken, Task<HResult>> handleAsync, [CallerMemberName] string caller = null)
        {
            var cts = new CancellationTokenSource();
            if (!activeCommands.TryAdd(commandId, cts))
            {
                cts.Dispose();
                Log.Error($"{caller}.Async(Id={commandId}) duplicate command addition.");
            }

            runAsync();
            return HResult.Pending;

            async void runAsync()
            {
                try
                {
                    using (cts)
                    {
                        Log.Debug($"{caller}.Async(Id={commandId})");
                        var result = await Task.Run(() => handleAsync(cts.Token));
                        Log.Debug($"{caller}.Async(Id={commandId}) => [{result}]");
                        completeCommand(result);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"{caller}.Async(Id={commandId}) error");
                    completeCommand(HResult.InternalError);
                }
            }

            void completeCommand(HResult result)
            {
                if (activeCommands.TryRemove(commandId, out _))
                {
                    var completionResult = virtualizationInstance.CompleteCommand(commandId, result);
                    if (completionResult != HResult.Ok)
                    {
                        Log.Error($"{caller}.Async(Id={commandId}) error completing command.");

                    }
                }
            }
        }

        protected IReadOnlyList<VfsNode> GetChildItemsSorted(string relativePath)
        {
            if (Tree.TryGetNode(relativePath, out var node) && node is VfsDirectoryNode dirNode)
            {
                if (!enumerationCache.TryGetValue((dirNode, dirNode.Version), out var items))
                {
                    var version = dirNode.Version;
                    items = EnumerateChildItems(relativePath).ToListSorted(ProjectedFileNameSorter.Instance);
                    enumerationCache.AddItem((dirNode, version), items);
                }

                return items;
            }
            else
            {
                return CollectionUtilities.EmptyArray<VfsNode>();
            }
        }

        protected IEnumerable<VfsNode> EnumerateChildItems(string relativePath)
        {
            if (Tree.TryGetNode(relativePath, out var node) && node is VfsDirectoryNode dirNode)
            {
                return dirNode.EnumerateChildren();
            }

            return Enumerable.Empty<VfsNode>();
        }

        #region Callback implementations

        // To keep all the callback implementations together we implement the required callbacks in
        // the SimpleProvider class along with the optional QueryFileName callback.  Then we have the
        // IRequiredCallbacks implementation forward the calls to here.

        internal HResult StartDirectoryEnumerationCallback(
            int commandId,
            Guid enumerationId,
            string relativePath,
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            // Enumerate the corresponding directory in the layer and ensure it is sorted the way
            // ProjFS expects.
            ActiveEnumeration activeEnumeration = new ActiveEnumeration(GetChildItemsSorted(relativePath));

            // Insert the layer enumeration into our dictionary of active enumerations, indexed by
            // enumeration ID.  GetDirectoryEnumerationCallback will be able to find this enumeration
            // given the enumeration ID and return the contents to ProjFS.
            if (!activeEnumerations.TryAdd(enumerationId, activeEnumeration))
            {
                return HResult.InternalError;
            }

            return HResult.Ok;
        }

        internal HResult GetDirectoryEnumerationCallback(
            int commandId,
            Guid enumerationId,
            string filterFileName,
            bool restartScan,
            IDirectoryEnumerationResults enumResult)
        {
            // Find the requested enumeration.  It should have been put there by StartDirectoryEnumeration.
            if (!activeEnumerations.TryGetValue(enumerationId, out ActiveEnumeration enumeration))
            {
                return HResult.InternalError;
            }

            if (restartScan)
            {
                // The caller is restarting the enumeration, so we reset our ActiveEnumeration to the
                // first item that matches filterFileName.  This also saves the value of filterFileName
                // into the ActiveEnumeration, overwriting its previous value.
                enumeration.RestartEnumeration(filterFileName);
            }
            else
            {
                // The caller is continuing a previous enumeration, or this is the first enumeration
                // so our ActiveEnumeration is already at the beginning.  TrySaveFilterString()
                // will save filterFileName if it hasn't already been saved (only if the enumeration
                // is restarting do we need to re-save filterFileName).
                enumeration.TrySaveFilterString(filterFileName);
            }

            bool entryAdded = false;
            HResult hr = HResult.Ok;

            while (enumeration.IsCurrentValid)
            {
                VfsNode node = enumeration.Current;

                if (enumResult.Add(
                    fileName: node.Name,
                    fileSize: node.Size,
                    isDirectory: node.IsDirectory,
                    fileAttributes: node.Attributes,
                    creationTime: node.Timestamp,
                    lastAccessTime: node.Timestamp,
                    lastWriteTime: node.Timestamp,
                    changeTime: node.Timestamp))
                {
                    entryAdded = true;
                    enumeration.MoveNext();
                }
                else
                {
                    if (entryAdded)
                    {
                        hr = HResult.Ok;
                    }
                    else
                    {
                        hr = HResult.InsufficientBuffer;
                    }

                    break;
                }
            }

            return hr;
        }

        internal HResult EndDirectoryEnumerationCallback(
            Guid enumerationId)
        {
            if (!activeEnumerations.TryRemove(enumerationId, out ActiveEnumeration enumeration))
            {
                return HResult.InternalError;
            }

            return HResult.Ok;
        }

        internal HResult GetPlaceholderInfoCallback(
            int commandId,
            string relativePath,
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            if (triggeringProcessId == currentProcessId)
            {
                // The current process cannot trigger placeholder creation to prevent deadlock do to re-entrancy
                // Just pretend the file doesn't exist.
                return HResult.FileNotFound;
            }

            // FileRealizationMode.Copy = just create a normal placeholder

            // FileRealizationMode.Hardlink:
            // 1. Try to create a placeholder by hardlinking from local CAS
            // 2. Create hardlink in VFS unified CAS

            if (!Tree.TryGetNode(relativePath, out var node))
            {
                return HResult.FileNotFound;
            }
            else
            {
                relativePath = relativePath.EndsWith(node.Name) ? relativePath : Path.Combine(Path.GetDirectoryName(relativePath), node.Name);
                if (node.IsDirectory)
                {
                    return virtualizationInstance.WritePlaceholderInfo(
                        relativePath: relativePath,
                        creationTime: node.Timestamp,
                        lastAccessTime: node.Timestamp,
                        lastWriteTime: node.Timestamp,
                        changeTime: node.Timestamp,
                        fileAttributes: node.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
                        endOfFile: node.Size,
                        isDirectory: node.IsDirectory,
                        contentId: s_contentId,
                        providerId: s_providerId);
                }
                else
                {
                    return HandleCommandAsynchronously(commandId, async token =>
                    {
                        var fileNode = (VfsFileNode)node;
                        await ContentManager.PlaceHydratedFileAsync(relativePath, new VfsFilePlacementData(fileNode.Hash, fileNode.RealizationMode, fileNode.AccessMode), token);

                        // TODO: Create hardlink / move to original location to replace symlink?
                        return HResult.Ok;
                    });
                }
            }
        }

        internal HResult GetFileDataCallback(
            int commandId,
            string relativePath,
            ulong byteOffset,
            uint length,
            Guid dataStreamId,
            byte[] contentId,
            byte[] providerId,
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            // We should never create file placeholders so this should not be necessary
            return HResult.FileNotFound;
        }

        private HResult QueryFileNameCallback(string relativePath)
        {
            // First try normal lookup
            if (Tree.TryGetNode(relativePath, out var node))
            {
                return HResult.Ok;
            }

            // TODO: Check whether this handles long paths appropriately.
            if (!Utils.DoesNameContainWildCards(relativePath))
            {
                // No wildcards and normal lookup failed so file must not exist
                return HResult.FileNotFound;
            }

            // There are wildcards, enumerate and try to find a matching child
            string parentPath = Path.GetDirectoryName(relativePath);

            if (Tree.TryGetNode(parentPath, out var parent) && parent is VfsDirectoryNode parentDirectory)
            {
                string childName = Path.GetFileName(relativePath);
                if (parentDirectory.EnumerateChildren().Any(child => Utils.IsFileNameMatch(child.Name, childName)))
                {
                    return HResult.Ok;
                }
            }

            return HResult.FileNotFound;
        }

        #endregion


        private class RequiredCallbacks : IRequiredCallbacks
        {
            private readonly VfsProvider provider;

            public RequiredCallbacks(VfsProvider provider) => this.provider = provider;

            // We implement the callbacks in the SimpleProvider class.

            public HResult StartDirectoryEnumerationCallback(
                int commandId,
                Guid enumerationId,
                string relativePath,
                uint triggeringProcessId,
                string triggeringProcessImageFileName)
            {
                return provider.Log.PerformOperation(
                    $"Id={commandId}, Path={relativePath}, EnumId={enumerationId}, ProcId={triggeringProcessId}, ProcName={triggeringProcessImageFileName}",
                    () => provider.StartDirectoryEnumerationCallback(
                        commandId,
                        enumerationId,
                        relativePath,
                        triggeringProcessId,
                        triggeringProcessImageFileName));
            }

            public HResult GetDirectoryEnumerationCallback(
                int commandId,
                Guid enumerationId,
                string filterFileName,
                bool restartScan,
                IDirectoryEnumerationResults enumResult)
            {
                return provider.Log.PerformOperation(
                    $"Id={commandId}, EnumId={enumerationId}, Filter={filterFileName}",
                    () => provider.GetDirectoryEnumerationCallback(
                        commandId,
                        enumerationId,
                        filterFileName,
                        restartScan,
                        enumResult));
            }

            public HResult EndDirectoryEnumerationCallback(
                Guid enumerationId)
            {
                return provider.Log.PerformOperation($"EnumId={enumerationId}", () => provider.EndDirectoryEnumerationCallback(enumerationId));
            }

            public HResult GetPlaceholderInfoCallback(
                int commandId,
                string relativePath,
                uint triggeringProcessId,
                string triggeringProcessImageFileName)
            {
                return provider.Log.PerformOperation(
                    $"Id={commandId}, Path={relativePath}, ProcId={triggeringProcessId}, ProcName={triggeringProcessImageFileName}",
                    () => provider.GetPlaceholderInfoCallback(
                        commandId,
                        relativePath,
                        triggeringProcessId,
                        triggeringProcessImageFileName));
            }

            public HResult GetFileDataCallback(
                int commandId,
                string relativePath,
                ulong byteOffset,
                uint length,
                Guid dataStreamId,
                byte[] contentId,
                byte[] providerId,
                uint triggeringProcessId,
                string triggeringProcessImageFileName)
            {
                return provider.Log.PerformOperation(
                    $"Id={commandId}, Path={relativePath}, ProcId={triggeringProcessId}, ProcName={triggeringProcessImageFileName}",
                    () => provider.GetFileDataCallback(
                        commandId,
                        relativePath,
                        byteOffset,
                        length,
                        dataStreamId,
                        contentId,
                        providerId,
                        triggeringProcessId,
                        triggeringProcessImageFileName));
            }
        }
    }
}
