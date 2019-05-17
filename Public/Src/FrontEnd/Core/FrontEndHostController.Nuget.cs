// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Core
{
    public sealed partial class FrontEndHostController
    {
        /// <summary>
        /// The mode to use the cache
        /// </summary>
        /// <remarks>
        /// The motivation for the copy is that in cloudbuild machines the cache is shared between other tasks (CASAS)
        /// On the build machines there can be other jobs running with files from the cache. If we use hardlinks
        /// we can end up with the case that a service running on the box has the same file as we have in our package cache
        /// and therefore has a 'lock' on the file. Our delete logic is not robust enough in this case so when we have to download
        /// the nuget package and have to clean the folder first, we fail. By doing a copy we hope to prevent this double-use.
        /// </remarks>
        private const FileRealizationMode PackageFileRealizationMode = FileRealizationMode.Copy;

        private PathTable PathTable => FrontEndContext.PathTable;

        private string GetPackageHashFile(AbsolutePath packageFolder) =>
            packageFolder.Combine(PathTable, "hash.txt").ToString(PathTable);

        /// <inheritdoc />
        /// <remarks>
        /// This function should ideally queue up the work
        /// </remarks>
        public override async Task<Possible<PackageDownloadResult>> DownloadPackage(
            string weakPackageFingerprint,
            PackageIdentity package,
            AbsolutePath packageTargetFolder,
            Func<Task<Possible<IReadOnlyList<RelativePath>>>> producePackage)
        {
            // Check if we can reuse a package that is layed out on disk already.
            // If the hash file is present packageHash variable will contain the content of it.
            if (TryGetPackageFromFromDisk(package, packageTargetFolder, weakPackageFingerprint, out var possiblePackageFromDisk, out var packageHash))
            {
                return possiblePackageFromDisk;
            }

            // Slower path: checking the cache first
            var targetLocation = packageTargetFolder.ToString(PathTable);
            var friendlyName = package.GetFriendlyName();

            using (var pm = PerformanceMeasurement.StartWithoutStatistic(
                LoggingContext,
                nestedLoggingContext => m_logger.StartRetrievingPackage(nestedLoggingContext, friendlyName, targetLocation),
                nestedLoggingContext => m_logger.EndRetrievingPackage(nestedLoggingContext, friendlyName)))
            {
                var loggingContext = pm.LoggingContext;

                // Getting the package content from the cache
                var packageFromCache = await TryGetPackageFromCache(loggingContext, weakPackageFingerprint, package, packageTargetFolder, packageHash);
                if (!packageFromCache.Succeeded || packageFromCache.Result.IsValid)
                {
                    // Cache failure or package was successfully obtained from the cache.
                    return packageFromCache;
                }

                // Cache miss
                m_logger.PackageNotFoundInCacheAndStartedDownloading(loggingContext, package.Id, package.Version);

                // Step: We couldn't retrieve the package from the cache, or some blobs were missing, generate the package with the given function
                var possiblePackageContents = await producePackage();
                if (!possiblePackageContents.Succeeded)
                {
                    return possiblePackageContents.Failure;
                }

                // Now we can save the content to the cache
                Analysis.IgnoreResult(
                    await TryAddPackageToCache(
                        loggingContext,
                        weakPackageFingerprint,
                        package,
                        packageTargetFolder,
                        possiblePackageContents.Result),
                    justification: "Okay to ignore putting in cache failure, will happen next time"
                );

                // Step: Return descriptor indicating it wasn't restored from the cache
                return PackageDownloadResult.FromRemote(package, packageTargetFolder, possiblePackageContents.Result);
            }
        }

        private bool TryGetPackageFromFromDisk(PackageIdentity package, AbsolutePath packageFolder, string weakPackageFingerprint, out Possible<PackageDownloadResult> possibleResult, out PackageHashFile? packageHash)
        {
            // There are two options that control this behavior:
            // /forcePopulatePackageCache and /usePackagesFromFileSystemOnly

            if (FrontEndConfiguration.UsePackagesFromFileSystem())
            {
                if (TryGetPackageFromFromDiskOnly(package, packageFolder, out var fromFile, out packageHash))
                {
                    possibleResult = fromFile;
                }
                else
                {
                    possibleResult = new PackageShouldBeOnDiskFailure(package.GetFriendlyName(), packageFolder.ToString(PathTable));
                }

                // If /usePackagesFromFileSystemOnly is specified, nothing except file check should happen.
                // Return true to stop the other lookups (like cache or nuget).
                return true;
            }


            if (TryGetPackageFromFromDiskWithFallBack(package, packageFolder, weakPackageFingerprint, out var fromDisk, out packageHash))
            {
                possibleResult = fromDisk;
                return true;
            }

            possibleResult = default(Possible<PackageDownloadResult>);
            return false;
        }

        private bool TryGetPackageFromFromDiskOnly(PackageIdentity package, AbsolutePath packageFolder, out PackageDownloadResult fromDisk, out PackageHashFile? packageHash)
        {
            fromDisk = null;
            packageHash = null;

            var packageHashFilePath = GetPackageHashFile(packageFolder);

            // Read the package hash.
            var possibleHashContent = PackageHashFile.TryReadFrom(packageHashFilePath);

            // Return if the hash file is missing or incorrect.
            if (!possibleHashContent.Succeeded)
            {
                return false;
            }

            // Creating a nuget package from the file system.
            var contents = possibleHashContent.Result.Content
                .Select(p => RelativePath.Create(PathTable.StringTable, p))
                .ToList();

            fromDisk = PackageDownloadResult.FromDisk(package, packageFolder, contents, possibleHashContent.Result.SpecsFormatIsUpToDate);
            packageHash = possibleHashContent.Result;
            return fromDisk.IsValid;
        }

        private bool TryGetPackageFromFromDiskWithFallBack(PackageIdentity package, AbsolutePath packageFolder, string weakPackageFingerprint, out PackageDownloadResult fromDisk, out PackageHashFile? packageHash)
        {
            fromDisk = null;
            packageHash = null;

            // Return if a user wants to repopulate the cache.
            if (FrontEndConfiguration.ForcePopulatePackageCache())
            {
                // The message was already logged that all the packages should be re-downloaded.
                // No needs for a special message per package.
                return false;
            }

            var packageHashFilePath = GetPackageHashFile(packageFolder);

            // Read the package hash.
            var possibleHashContent = PackageHashFile.TryReadFrom(packageHashFilePath);

            // Return if the hash file is missing or incorrect.
            if (!possibleHashContent.Succeeded)
            {
                m_logger.CanNotReusePackageHashFile(LoggingContext, packageHashFilePath, possibleHashContent.Failure.Describe());
                return false;
            }

            // Creating a nuget package from the file system.
            var contents = possibleHashContent.Result.Content
                .Select(p => RelativePath.Create(PathTable.StringTable, p))
                .ToList();

            fromDisk = PackageDownloadResult.FromDisk(package, packageFolder, contents, possibleHashContent.Result.SpecsFormatIsUpToDate);
            packageHash = possibleHashContent.Result;

            // .NET Core builds do not support nuget at all, so we use files from disk regardless of their correctness
            // Reuse package from disk when fingerprint matches or when the fingeprint check is disabled.
            if (FrontEndConfiguration.RespectWeakFingerprintForNugetUpToDateCheck() && packageHash.Value.FingerprintText != weakPackageFingerprint)
            {
                m_logger.CanNotReusePackageHashFile(
                    LoggingContext,
                    packageHashFilePath,
                    "/respectWeakFingerprintForNugetUpToDateCheck option was specified and the package's fingerprint has changed.");
                return false;
            }

            Logger.PackagePresumedUpToDateWithoutHashComparison(LoggingContext, package.GetFriendlyName());

            return true;
        }

        private async Task<Possible<PackageDownloadResult>> TryGetPackageFromCache(
            LoggingContext loggingContext,
            string weakPackageFingerprint,
            PackageIdentity package,
            AbsolutePath packageTargetFolder,
            PackageHashFile? packageHash)
        {
            var friendlyName = package.GetFriendlyName();
            var targetLocation = packageTargetFolder.ToString(PathTable);

            // Return if a user wants to repopulate the cache.
            if (FrontEndConfiguration.ForcePopulatePackageCache())
            {
                // The message was already logged, no needs for another entry per package.
                return PackageDownloadResult.RecoverableError(package);
            }

            // Making sure that the cache is initialized correctly
            var possibleCache = await m_nugetCache;
            if (!possibleCache.Succeeded)
            {
                return new PackageDownloadFailure(friendlyName, targetLocation, PackageDownloadFailure.FailureType.CacheError, possibleCache.Failure);
            }

            // Step: Check to see if we can find a cache entry for the weak fingerprint of the package
            var cache = possibleCache.Cache;
            var weakPackageFingerprintHash = possibleCache.GetDownloadFingerprint(weakPackageFingerprint);
            var cacheQueryData = new CacheQueryData();

            var possibleEntry = await possibleCache.SinglePhaseStore.TryGetFingerprintEntryAsync(weakPackageFingerprintHash, cacheQueryData);
            if (!possibleEntry.Succeeded)
            {
                m_logger.DownloadPackageFailedDueToCacheError(loggingContext, friendlyName, possibleEntry.Failure.Describe());
                return new PackageDownloadFailure(
                    friendlyName,
                    targetLocation,
                    PackageDownloadFailure.FailureType.GetFingerprintFromCache,
                    possibleEntry.Failure);
            }

            var packageHashFile = GetPackageHashFile(packageTargetFolder);

            // Step: When found check that it is a proper package descriptor for this package
            PipFingerprintEntry entry = possibleEntry.Result;
            if (entry != null &&
                entry.Kind != PipFingerprintEntryKind.PackageDownload)
            {
                // Cache hit, but wrong descriptor
                string additionalInfo = I($" Unexpected descriptor kind '{entry.Kind}'.");
                m_logger.DownloadPackageFailedDueToInvalidCacheContents(loggingContext, friendlyName, additionalInfo);
                return PackageDownloadResult.RecoverableError(package);
            }

            if (entry == null)
            {
                // The entry is missing on disk. Nothing to log about, the caller will log that the package is missing and the package will be obtained from nuget.
                return PackageDownloadResult.RecoverableError(package);
            }

            // Cache hit
            var packageDescriptor = (PackageDownloadDescriptor)entry.Deserialize(cacheQueryData);
            if (!string.Equals(packageDescriptor.FriendlyName, friendlyName, StringComparison.Ordinal))
            {
                // Cache is corrupted.
                string additionalInfo = I($" The name '{packageDescriptor.FriendlyName}' doesn't match the expected package's friendly name.");
                m_logger.DownloadPackageFailedDueToInvalidCacheContents(loggingContext, friendlyName, additionalInfo);
                return PackageDownloadResult.RecoverableError(package);
            }

            bool forcePopulatePackageCache = FrontEndConfiguration.ForcePopulatePackageCache();
            if (!forcePopulatePackageCache && packageHash?.FingerprintHash == weakPackageFingerprintHash.Hash.ToHex())
            {
                m_logger.PackagePresumedUpToDate(loggingContext, package.GetFriendlyName());

                return PackageDownloadResult.FromCache(
                    package,
                    packageTargetFolder,
                    packageDescriptor.Contents.Select(c => RelativePath.Create(FrontEndContext.StringTable, c.Key)).ToList());
            }

            // Step: Try to bring all the contents of the package locally from the content cache.
            var hashes = packageDescriptor.Contents.SelectArray(hashByPath => hashByPath.ContentHash.ToContentHash());

            var possibleResults = await cache.ArtifactContentCache.TryLoadAvailableContentAsync(hashes);
            if (!possibleResults.Succeeded)
            {
                m_logger.DownloadPackageFailedDueToCacheError(loggingContext, friendlyName, possibleResults.Failure.Describe());
                return new PackageDownloadFailure(
                    friendlyName,
                    targetLocation,
                    PackageDownloadFailure.FailureType.LoadAvailableContentFromCache,
                    possibleResults.Failure);
            }

            if (!possibleResults.Result.AllContentAvailable)
            {
                // This one is a proper fall back case.
                string additionalInfo = I($" Not the full content of the package is available.");
                m_logger.DownloadPackageFailedDueToInvalidCacheContents(loggingContext, friendlyName, additionalInfo);
                return PackageDownloadResult.RecoverableError(package);
            }

            var downloadTasks = new List<Task<Possible<ContentMaterializationResult, Failure>>>(packageDescriptor.Contents.Count());

            foreach (var outputHasByRelativePath in packageDescriptor.Contents)
            {
                var targetFileLocation = packageTargetFolder.Combine(
                    PathTable,
                    RelativePath.Create(FrontEndContext.StringTable, outputHasByRelativePath.Key));

                downloadTasks.Add(
                    Engine.TryMaterializeContentAsync(
                        cache.ArtifactContentCache,
                        PackageFileRealizationMode,
                        targetFileLocation,
                        outputHasByRelativePath.ContentHash.ToContentHash(),
                        // Files downloaded by NuGet are not subject to tracking. When the evaluation or
                        // execution read the downloaded files, then those file will be tracked.
                        trackPath: false,
                        // However, to avoid re-hashing, we still want to record the downloaded file in the file content table.
                        recordPathInFileContentTable: true));
            }

            var possiblyPlacedResults = await Task.WhenAll(downloadTasks);

            foreach (var possiblyPlaced in possiblyPlacedResults)
            {
                if (!possiblyPlaced.Succeeded)
                {
                    m_logger.DownloadPackageFailedDueToCacheError(loggingContext, friendlyName, possiblyPlaced.Failure.Describe());
                    return new PackageDownloadFailure(
                        friendlyName,
                        targetLocation,
                        PackageDownloadFailure.FailureType.MaterializeFromCache,
                        possiblyPlaced.Failure);
                }
            }

            // Saving package's hash file on disk.      
            // But we can do this only when the content is not empty.
                var packagesContent = packageDescriptor.Contents.Select(k => k.Key).ToList();

            if (packagesContent.Count == 0)
            {
                string additionalInfo = I($" The content is empty.");
                m_logger.DownloadPackageFailedDueToInvalidCacheContents(loggingContext, friendlyName, additionalInfo);
                return PackageDownloadResult.RecoverableError(package);
            }

            var newPackageHash = new PackageHashFile(weakPackageFingerprintHash.Hash.ToHex(), weakPackageFingerprint, packagesContent, specsFormatIsUpToDate: false);
            TryUpdatePackageHashFile(loggingContext, package, packageHashFile, packageHash, newPackageHash);

            m_logger.PackageRestoredFromCache(loggingContext, package.GetFriendlyName());

            if (forcePopulatePackageCache)
            {
                // Even though the fingerprint entry was retrieved from the cache try storing it again to ensure that the
                // the cache is populated.
                var possibleFingerprintForceStored = await possibleCache.SinglePhaseStore.TryStoreFingerprintEntryAsync(
                    weakPackageFingerprintHash,
                    entry,
                    replaceExisting: false /* Package download is deterministic so no need to update existing entry */);

                if (!possibleFingerprintForceStored.Succeeded)
                {
                    m_logger.DownloadPackageCannotCacheError(
                        loggingContext,
                        friendlyName,
                        targetLocation,
                        weakPackageFingerprintHash.ToString(),
                        possibleFingerprintForceStored.Failure.Describe());

                    return new PackageDownloadFailure(
                        friendlyName,
                        targetLocation,
                        PackageDownloadFailure.FailureType.CannotCacheFingerprint,
                        possibleFingerprintForceStored.Failure);
                }
            }

            // Step: Return descriptor indicating it was successfully restored from the cache
            return PackageDownloadResult.FromCache(
                package,
                packageTargetFolder,
                packageDescriptor.Contents.Select(c => RelativePath.Create(FrontEndContext.StringTable, c.Key)).ToList());
        }

        private async Task<Possible<Unit>> TryAddPackageToCache(
            LoggingContext loggingContext,
            string weakPackageFingerprint,
            PackageIdentity package,
            AbsolutePath packageTargetFolder,
            IReadOnlyList<RelativePath> packageContent)
        {
            var friendlyName = package.GetFriendlyName();
            var targetLocation = packageTargetFolder.ToString(PathTable);
            var packageContents = packageContent;

            if (packageContent.Count == 0)
            {
                // Make no sense to store an empty content, it is unlikely correct.
                m_logger.DownloadPackageCannotCacheWarning(loggingContext, friendlyName, targetLocation, string.Empty, "The content of the package is empty.");
                return Unit.Void;
            }

            // Cache was already initialized
            var cache = await m_nugetCache;

            // Step: Store all the files into the content cache
            var stringKeyedHashes = new List<StringKeyedHash>();
            foreach (var relativePath in packageContents)
            {
                var targetFileLocation = packageTargetFolder.Combine(PathTable, relativePath).Expand(PathTable);

                ContentHash contentHash;
                try
                {
                    contentHash = await ContentHashingUtilities.HashFileAsync(targetFileLocation.ExpandedPath);
                }
                catch (BuildXLException e)
                {
                    m_logger.DownloadPackageCouldntHashPackageFile(loggingContext, friendlyName, targetFileLocation.ExpandedPath, e.LogEventMessage);
                    return new PackageDownloadFailure(friendlyName, targetFileLocation.ExpandedPath, PackageDownloadFailure.FailureType.HashingOfPackageFile, e);
                }

                stringKeyedHashes.Add(new StringKeyedHash() { Key = relativePath.ToString(FrontEndContext.StringTable), ContentHash = contentHash.ToBondContentHash() });

                var possiblyContentStored = await cache.Cache.ArtifactContentCache.TryStoreAsync(
                    PackageFileRealizationMode,
                    targetFileLocation,
                    contentHash);
                if (!possiblyContentStored.Succeeded)
                {
                    m_logger.DownloadPackageCannotCacheWarning(loggingContext, friendlyName, targetLocation, contentHash.ToHex(), possiblyContentStored.Failure.Describe());
                    return new PackageDownloadFailure(friendlyName, targetLocation, PackageDownloadFailure.FailureType.CannotCacheContent, possiblyContentStored.Failure);
                }
            }

            var weakPackageFingerprintHash = cache.GetDownloadFingerprint(weakPackageFingerprint);
            // Step: Create a descriptor and store that in the fingerprint store under the weak fingerprint.
            var cacheDescriptor = PackageDownloadDescriptor.Create(
                friendlyName,
                stringKeyedHashes,
                loggingContext.Session.Environment);
            var possibleFingerprintStored = await cache.SinglePhaseStore.TryStoreFingerprintEntryAsync(
                weakPackageFingerprintHash,
                cacheDescriptor.ToEntry(),
                replaceExisting: false /* Package download is deterministic so no need to update existing entry */);
            if (!possibleFingerprintStored.Succeeded)
            {
                m_logger.DownloadPackageCannotCacheWarning(loggingContext, friendlyName, targetLocation, weakPackageFingerprintHash.ToString(), possibleFingerprintStored.Failure.Describe());
                return new PackageDownloadFailure(friendlyName, targetLocation, PackageDownloadFailure.FailureType.CannotCacheFingerprint, possibleFingerprintStored.Failure);
            }

            m_logger.PackageNotFoundInCacheAndDownloaded(loggingContext, package.Id, package.Version, weakPackageFingerprintHash.Hash.ToHex(), weakPackageFingerprint);

            // The content should have relative paths
            var content = packageContents.Select(rp => rp.ToString(PathTable.StringTable)).ToList();
            var newHash = new PackageHashFile(weakPackageFingerprintHash.Hash.ToHex(), weakPackageFingerprint, content, specsFormatIsUpToDate: false);
            TryUpdatePackageHashFile(loggingContext, package, GetPackageHashFile(packageTargetFolder), oldHash: null, newHash: newHash);

            return Unit.Void;
        }

        private void TryUpdatePackageHashFile(
            LoggingContext loggingContext,
            PackageIdentity package,
            string hashFilePath,
            PackageHashFile? oldHash,
            PackageHashFile newHash)
        {
            var saveResult = PackageHashFile.TrySaveTo(hashFilePath, newHash);
            if (!saveResult.Succeeded)
            {
                m_logger.CanNotUpdatePackageHashFile(LoggingContext, hashFilePath, saveResult.Failure.Describe());
                return;
            }

            // If the hash has changed log the old and the new one for potential investigation purposes.
            if (oldHash != null && oldHash.Value.FingerprintHash != newHash.FingerprintHash)
            {
                m_logger.PackageCacheMissInformation(
                    loggingContext,
                    package.Id,
                    package.Version,
                    oldHash.Value.FingerprintWithHash(),
                    newHash.FingerprintWithHash());
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// This function should ideally queue up the work
        /// </remarks>
        public override async Task<Possible<ContentHash>> DownloadFile(string url, AbsolutePath targetLocation, ContentHash? expectedContentHash, string friendlyName)
        {
            Contract.Requires(!string.IsNullOrEmpty(url));
            Contract.Requires(targetLocation.IsValid);
            Contract.Requires(!string.IsNullOrEmpty(friendlyName));

            var expandedTargetLocation = targetLocation.Expand(FrontEndContext.PathTable);
            var targetFilePath = expandedTargetLocation.ExpandedPath;
            var loggingContext = FrontEndContext.LoggingContext;

            // If we know the hash we can check if the target file matches.
            // I know this is ridiculous as the cache takes care of this, but w are suffering due to slow
            // cache initialization, hence the quick check duplicated.
            if (File.Exists(targetFilePath) && expectedContentHash.HasValue)
            {
                try
                {
                    var existingContentHash = await ContentHashingUtilities.HashFileAsync(targetFilePath);
                    if (existingContentHash == expectedContentHash.Value)
                    {
                        m_logger.DownloadToolIsUpToDate(loggingContext, friendlyName, targetFilePath, expectedContentHash.Value.ToHex());
                        return existingContentHash;
                    }
                }
                catch (BuildXLException e)
                {
                    // Failed to hash existing file, attempt to download anyway.
                    m_logger.DownloadToolFailedToHashExisting(loggingContext, friendlyName, targetFilePath, e.LogEventMessage);
                    return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.HashExistingFile, e);
                }
            }

            using (var pm = PerformanceMeasurement.StartWithoutStatistic(
                loggingContext,
                nestedLoggingContext => m_logger.StartDownloadingTool(nestedLoggingContext, friendlyName, url, targetFilePath),
                nestedLoggingContext => m_logger.EndDownloadingTool(nestedLoggingContext, friendlyName)))
            {
                loggingContext = pm.LoggingContext;

                // we need to check the cache. This is potentially block
                var possibleCache = await m_nugetCache;
                if (!possibleCache.Succeeded)
                {
                    m_logger.DownloadToolFailedDueToCacheError(loggingContext, friendlyName, possibleCache.Failure.Describe());
                    return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.InitializeCache, possibleCache.Failure);
                }

                var cache = possibleCache.Cache;
                ContentFingerprint downloadFingerprint = ContentFingerprint.Zero;

                ContentHash? contentHash = expectedContentHash;
                if (!contentHash.HasValue)
                {
                    // If we don't have an expected content hash, lookup up the fingerprint for the URL in the local cache.
                    downloadFingerprint = CreateDownloadFingerprint("downloadFromWeb:\n" + url);

                    var cacheQueryData = new CacheQueryData();
                    var possibleEntry = await possibleCache.SinglePhaseStore.TryGetFingerprintEntryAsync(downloadFingerprint, cacheQueryData);
                    if (!possibleEntry.Succeeded)
                    {
                        m_logger.DownloadToolFailedDueToCacheError(loggingContext, friendlyName, possibleEntry.Failure.Describe());
                        return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.GetFingerprintFromCache, possibleEntry.Failure);
                    }

                    PipFingerprintEntry entry = possibleEntry.Result;
                    if (entry != null && // Normal miss case
                        entry.Kind == PipFingerprintEntryKind.FileDownload)
                    {
                        var fileDownloadDescriptor = (FileDownloadDescriptor)entry.Deserialize(cacheQueryData);
                        contentHash = fileDownloadDescriptor.Content.ToContentHash();
                    }
                }

                // We have a content hash, try to materialize it from the cache
                if (contentHash.HasValue)
                {
                    var possiblyLoaded = await cache.ArtifactContentCache.TryLoadAvailableContentAsync(new[] { contentHash.Value });
                    if (!possiblyLoaded.Succeeded)
                    {
                        m_logger.DownloadToolFailedDueToCacheError(loggingContext, friendlyName, possiblyLoaded.Failure.Describe());
                        return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.LoadContentFromCache, possiblyLoaded.Failure);
                    }

                    if (possiblyLoaded.Result.AllContentAvailable)
                    {
                        var possiblyPlaced = await cache.ArtifactContentCache.TryMaterializeAsync(PackageFileRealizationMode, expandedTargetLocation, contentHash.Value);
                        if (!possiblyPlaced.Succeeded)
                        {
                            m_logger.DownloadToolFailedDueToCacheError(loggingContext, friendlyName, possiblyPlaced.Failure.Describe());
                            return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.PlaceContentFromCache, possiblyPlaced.Failure);
                        }

                        m_logger.DownloadToolIsRetrievedFromCache(loggingContext, friendlyName, targetFilePath, contentHash.Value.ToString());
                        return contentHash.Value;
                    }
                }

                try
                {
                    FileUtilities.CreateDirectory(Path.GetDirectoryName(targetFilePath));
                    FileUtilities.DeleteFile(targetFilePath, waitUntilDeletionFinished: true);
                }
                catch (BuildXLException e)
                {
                    m_logger.DownloadToolErrorDownloading(loggingContext, friendlyName, url, targetFilePath, e.Message);
                    return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.Download, e);
                }

                // Files is out of date, we'll have to download it.
                var downloadResult = await DownloadFile(loggingContext, friendlyName, url, targetFilePath);
                if (!downloadResult.Succeeded)
                {
                    return downloadResult.Failure;
                }

                if (!File.Exists(targetFilePath))
                {
                    m_logger.DownloadToolErrorFileNotDownloaded(loggingContext, friendlyName, targetFilePath);
                    return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.DownloadResultMissing);
                }

                // Compute the hash of the file for validation
                ContentHash downloadedHash;
                try
                {
                    downloadedHash = await ContentHashingUtilities.HashFileAsync(targetFilePath);
                }
                catch (BuildXLException e)
                {
                    m_logger.DownloadToolWarnCouldntHashDownloadedFile(loggingContext, friendlyName, targetFilePath, e.Message);
                    return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.HashingOfDownloadedFile, e);
                }

                // If we had a hash guard, validate it.
                if (expectedContentHash.HasValue && expectedContentHash.Value != downloadedHash)
                {
                    m_logger.DownloadToolErrorDownloadedToolWrongHash(loggingContext, friendlyName, targetFilePath, url, expectedContentHash.Value.ToString(), downloadedHash.ToHex());
                    return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.MismatchedHash);
                }

                // Store the downloaded file in the content cache
                var possiblyContentStored = await cache.ArtifactContentCache.TryStoreAsync(
                    PackageFileRealizationMode,
                    expandedTargetLocation,
                    downloadedHash);
                if (!possiblyContentStored.Succeeded)
                {
                    m_logger.DownloadToolCannotCache(loggingContext, friendlyName, targetFilePath, url, downloadedHash.ToHex(), possiblyContentStored.Failure.Describe());
                    return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.CannotCacheContent, possiblyContentStored.Failure);
                }

                // Store the fingerprint in the cache
                if (!expectedContentHash.HasValue)
                {
                    var possibleFingerprintStored = await possibleCache.SinglePhaseStore.TryStoreFingerprintEntryAsync(
                        downloadFingerprint,
                        FileDownloadDescriptor.Create(downloadedHash.ToBondContentHash(), url, loggingContext.Session.Environment).ToEntry());
                    if (!possibleFingerprintStored.Succeeded)
                    {
                        m_logger.DownloadToolCannotCache(loggingContext, friendlyName, targetFilePath, url, downloadedHash.ToHex(), possibleFingerprintStored.Failure.Describe());
                        return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.CannotCacheFingerprint, possibleFingerprintStored.Failure);
                    }
                }

                return downloadedHash;
            }
        }

        private static ContentFingerprint CreateDownloadFingerprint(string baseText)
        {
            // In case something in the cached Bond data becomes incompatible, we must not match.
            const string VersionText = ", BondDataVersion=2;FingerprintVersion=1";
            var fingerprint = FingerprintUtilities.Hash(baseText + VersionText);
            return new ContentFingerprint(fingerprint);
        }

        /// <summary>
        /// Download a file asynchronously
        /// </summary>
        public async Task<Possible<string>> DownloadFile(LoggingContext loggingContext, string friendlyName, string url, string targetFilePath)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(!string.IsNullOrEmpty(friendlyName));
            Contract.Requires(!string.IsNullOrEmpty(url));
            Contract.Requires(!string.IsNullOrEmpty(targetFilePath));
            Contract.EnsuresOnThrow<BuildXLException>(true);

            if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var sourceUri))
            {
                m_logger.DownloadToolErrorInvalidUri(loggingContext, friendlyName, url);
                return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.InvalidUri);
            }

            if (IsHttp(sourceUri))
            {
                try
                {
                    using (var client = new HttpClient())
                    {
                        var response = await client.GetAsync(sourceUri);
                        var stream = await response.Content.ReadAsStreamAsync();

                        using (var targetStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await stream.CopyToAsync(targetStream);
                        }
                    }
                }
                catch (IOException e)
                {
                    return LogAndReturnDownloadFailure(e);
                }
                catch (UnauthorizedAccessException e)
                {
                    return LogAndReturnDownloadFailure(e);
                }
                catch (HttpRequestException e)
                {
                    return LogAndReturnDownloadFailure(e);
                }
            }
            // Copy file if (sourceUri is absolute AND protocol is 'file://') OR (url is a valid relative path)
            else if ((sourceUri.IsAbsoluteUri && sourceUri.IsFile) ||
                (RelativePath.TryCreate(FrontEndContext.StringTable, url, out RelativePath relativePath)))
            {
                try
                {
                    var sourcePath = relativePath.IsValid
                        ? url
                        : sourceUri.AbsolutePath;
                    await FileUtilities.CopyFileAsync(sourcePath, targetFilePath);
                }
                catch (BuildXLException e)
                {
                    m_logger.DownloadToolErrorCopyFile(loggingContext, friendlyName, url, targetFilePath, e.Message);
                    return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.CopyFile, e);
                }
            }
            else
            {
                m_logger.DownloadToolErrorInvalidUri(loggingContext, friendlyName, url);
                return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.InvalidUri);
            }

            return targetFilePath;

            FileDownloadFailure LogAndReturnDownloadFailure(Exception e)
            {
                m_logger.DownloadToolErrorDownloading(loggingContext, friendlyName, url, targetFilePath, e.GetLogEventMessage());
                return new FileDownloadFailure(friendlyName, url, targetFilePath, FailureType.Download, e);
            }
        }

        private static bool IsHttp(Uri source)
        {
            if (!source.IsAbsoluteUri)
            {
                return false;
            }

            string scheme = source.Scheme;
            return string.Equals("http", scheme, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals("https", scheme, StringComparison.OrdinalIgnoreCase);
        }
    }
}
