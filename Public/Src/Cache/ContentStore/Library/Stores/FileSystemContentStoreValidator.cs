// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using FileInfo = BuildXL.Cache.ContentStore.Interfaces.FileSystem.FileInfo;

#nullable enable

namespace BuildXL.Cache.ContentStore.Stores
{
    internal class FileSystemContentStoreValidator
    {
        private readonly Tracer _tracer;
        private readonly IAbsFileSystem _fileSystem;
        private readonly bool _applyDenyWriteAttributesOnContent;
        private readonly IContentDirectory _contentDirectory;
        private readonly IClock _clock;
        private readonly Func<IEnumerable<FileInfo>> _enumerateBlobPathsFromDisk;

        public FileSystemContentStoreValidator(
            Tracer tracer,
            IAbsFileSystem fileSystem,
            bool applyDenyWriteAttributesOnContent,
            IContentDirectory contentDirectory,
            IClock clock,
            Func<IEnumerable<FileInfo>> enumerateBlobPathsFromDisk)
        {
            _tracer = tracer;
            _fileSystem = fileSystem;
            _applyDenyWriteAttributesOnContent = applyDenyWriteAttributesOnContent;
            _contentDirectory = contentDirectory;
            _clock = clock;
            _enumerateBlobPathsFromDisk = enumerateBlobPathsFromDisk;
        }

        public async Task<bool> ValidateAsync(Context context)
        {
            bool foundIssue = false;

            foundIssue |= !await ValidateNameHashesMatchContentHashesAsync(context);
            foundIssue |= !ValidateAcls(context);
            foundIssue |= !await ValidateContentDirectoryAsync(context);

            return !foundIssue;
        }

        private async Task<bool> ValidateNameHashesMatchContentHashesAsync(Context context)
        {
            int mismatchedParentDirectoryCount = 0;
            int mismatchedContentHashCount = 0;
            _tracer.Always(context, "Validating local CAS content hashes...");
            await TaskSafetyHelpers.WhenAll(_enumerateBlobPathsFromDisk().Select(
                async blobPath =>
                {
                    var contentFile = blobPath.FullPath;
                    if (!contentFile.FileName.StartsWith(contentFile.GetParent().FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        mismatchedParentDirectoryCount++;

                        _tracer.Debug(
                            context,
                            $"The first {FileSystemContentStoreInternal.HashDirectoryNameLength} characters of the name of content file at {contentFile}" +
                            $" do not match the name of its parent directory {contentFile.GetParent().FileName}.");
                    }

                    if (!FileSystemContentStoreInternal.TryGetHashFromPath(context, _tracer, contentFile, out var hashFromPath))
                    {
                        _tracer.Debug(
                            context,
                            $"The path '{contentFile}' does not contain a well-known hash name.");
                        return;
                    }

                    var hasher = HashInfoLookup.GetContentHasher(hashFromPath.HashType);
                    ContentHash hashFromContents;
                    using (var contentStream = await _fileSystem.OpenSafeAsync(
                        contentFile, FileAccess.Read, FileMode.Open, FileShare.Read | FileShare.Delete, FileOptions.SequentialScan, HashingExtensions.HashStreamBufferSize))
                    {
                        hashFromContents = await hasher.GetContentHashAsync(contentStream);
                    }

                    if (hashFromContents != hashFromPath)
                    {
                        mismatchedContentHashCount++;

                        _tracer.Debug(
                            context,
                            $"Content at {contentFile} content hash {hashFromContents.ToShortString()} did not match expected value of {hashFromPath.ToShortString()}.");
                    }
                }));

            _tracer.Always(context, $"{mismatchedParentDirectoryCount} mismatches between content file name and parent directory.");
            _tracer.Always(context, $"{mismatchedContentHashCount} mismatches between content file name and file contents.");

            return mismatchedContentHashCount == 0 && mismatchedParentDirectoryCount == 0;
        }

        private bool ValidateAcls(Context context)
        {
            // Getting ACLs currently requires using File.GetAccessControl.  We should extend IAbsFileSystem to enable this query.
            if (!(_fileSystem is PassThroughFileSystem))
            {
                _tracer.Always(context, "Skipping validation of ACLs because the CAS is not using a PassThroughFileSystem.");
                return true;
            }

            _tracer.Always(context, "Validating local CAS content file ACLs...");

            int missingDenyAclCount = 0;

            foreach (var blobPath in _enumerateBlobPathsFromDisk())
            {
                var contentFile = blobPath.FullPath;

                // FileSystem has no GetAccessControl API, so we must bypass it here.  We can relax the restriction to PassThroughFileSystem once we implement GetAccessControl in IAbsFileSystem.
                bool denyAclExists = true;
#if NET_FRAMEWORK
                const string WorldSidValue = "Everyone";
                var security = File.GetAccessControl(contentFile.Path);
                var fileSystemAccessRules =
                    security.GetAccessRules(true, false, typeof(NTAccount)).Cast<FileSystemAccessRule>();
                denyAclExists = fileSystemAccessRules.Any(rule =>
                                                              rule.IdentityReference.Value.Equals(WorldSidValue, StringComparison.OrdinalIgnoreCase) &&
                                                              rule.AccessControlType == AccessControlType.Deny &&
                                                              rule.FileSystemRights == (_applyDenyWriteAttributesOnContent
                                                                  ? (FileSystemRights.WriteData | FileSystemRights.AppendData)
                                                                  : FileSystemRights.Write) && // Should this be exact (as it is now), or at least, deny ACLs?
                                                              rule.InheritanceFlags == InheritanceFlags.None &&
                                                              rule.IsInherited == false &&
                                                              rule.PropagationFlags == PropagationFlags.None
                );
#endif

                if (!denyAclExists)
                {
                    missingDenyAclCount++;
                    _tracer.Always(context, $"Content at {contentFile} is missing proper deny ACLs.");
                }
            }

            _tracer.Always(context, $"{missingDenyAclCount} projects are missing proper deny ACLs.");

            return missingDenyAclCount == 0;
        }

        private async Task<bool> ValidateContentDirectoryAsync(Context context)
        {
            _tracer.Always(context, "Validating local CAS content directory");
            int contentDirectoryMismatchCount = 0;

            var fileSystemContentDirectory = _enumerateBlobPathsFromDisk()
                .Select(blobPath => FileSystemContentStoreInternal.TryGetHashFromPath(context, _tracer, blobPath.FullPath, out var hash) ? (ContentHash?)hash : null)
                .Where(hash => hash != null)
                .GroupBy(hash => hash!.Value)
                .ToDictionary(replicaGroup => replicaGroup.Key, replicaGroup => replicaGroup.Count());

            foreach (var x in fileSystemContentDirectory.Keys)
            {
                var fileSystemHash = x;
                int fileSystemHashReplicaCount = fileSystemContentDirectory[fileSystemHash];

                await _contentDirectory.UpdateAsync(fileSystemHash, false, _clock, fileInfo =>
                                                                                 {
                                                                                     if (fileInfo == null)
                                                                                     {
                                                                                         contentDirectoryMismatchCount++;
                                                                                         _tracer.Always(context, $"Cache content directory for hash {fileSystemHash.ToShortString()} from disk does not exist.");
                                                                                     }
                                                                                     else if (fileInfo.ReplicaCount != fileSystemHashReplicaCount)
                                                                                     {
                                                                                         contentDirectoryMismatchCount++;
                                                                                         _tracer.Always(
                                                                                             context,
                                                                                             $"Directory for hash {fileSystemHash.ToShortString()} describes {fileInfo.ReplicaCount} replicas, but {fileSystemHashReplicaCount} replicas exist on disk.");
                                                                                     }

                                                                                     return null;
                                                                                 });
            }

            foreach (var x in (await _contentDirectory.EnumerateContentHashesAsync())
                .Where(hash => !fileSystemContentDirectory.ContainsKey(hash)))
            {
                var missingHash = x;
                contentDirectoryMismatchCount++;
                await _contentDirectory.UpdateAsync(missingHash, false, _clock, fileInfo =>
                                                                              {
                                                                                  if (fileInfo != null)
                                                                                  {
                                                                                      _tracer.Always(
                                                                                          context,
                                                                                          $"Directory for hash {missingHash.ToShortString()} describes {fileInfo.ReplicaCount} replicas, but no replicas exist on disk.");
                                                                                  }

                                                                                  return null;
                                                                              });
            }

            _tracer.Always(
                context, $"{contentDirectoryMismatchCount} mismatches between cache content directory and content files on disk.");

            return contentDirectoryMismatchCount == 0;
        }
    }
}
