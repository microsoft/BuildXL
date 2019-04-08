// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Utils;
using CLAP;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
    internal sealed partial class Application
    {
        /// <summary>
        ///     Stats verb.
        /// </summary>
        [Verb(Aliases = "ds", Description = "Show bytes used/saved by the cache")]
        internal void Stats([Required, Description("Content cache root directory")] string root)
        {
            Initialize();
            var rootPath = new AbsolutePath(root);
            RunStats(rootPath).Wait();
        }

        private static string Percent(long numerator, long denominator)
        {
            return denominator == 0
                ? "0%"
                : string.Format(CultureInfo.InvariantCulture, "{0}%", 100 * numerator / denominator);
        }

        private async Task RunStats(AbsolutePath rootPath)
        {
            const int dividerWidth = 20;

            try
            {
                var allContentFiles = default(FileStats);
                var sharedFiles = default(FileStats);
                var privateFiles = default(FileStats);
                var cacheDeduplicatedFiles = default(FileStats);
                var outputDeduplicatedFiles = default(FileStats);

                var processFileInfoActionBlock = new ActionBlock<FileInfo>(
                    fileInfo =>
                    {
                        var hardLinkCount = 0;
                        try
                        {
                            hardLinkCount = _fileSystem.GetHardLinkCount(fileInfo.FullPath);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning("Failed to open content file at {0}. {1}", fileInfo.FullPath, ex);
                        }

                        _logger.Diagnostic(
                            "Processing file=[{0}] size=[{1}]  hardLinkCount=[{2}]", fileInfo.FullPath, fileInfo.Length, hardLinkCount);

                        allContentFiles.Increase(1, fileInfo.Length);
                        if (hardLinkCount == 1)
                        {
                            privateFiles.Increase(1, fileInfo.Length);
                        }
                        else
                        {
                            if (hardLinkCount > 1)
                            {
                                sharedFiles.Increase(1, fileInfo.Length);
                                cacheDeduplicatedFiles.Increase(hardLinkCount - 1, fileInfo.Length);

                                if (hardLinkCount > 2)
                                {
                                    outputDeduplicatedFiles.Increase(hardLinkCount - 2, fileInfo.Length);
                                }
                            }
                        }
                    },
                    new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }
                    );

                var processFileInfoTask =
                    processFileInfoActionBlock.PostAllAndComplete(EnumerateBlobPathsFromDisk(rootPath));

                var metadataFileInfos = _fileSystem.EnumerateFiles(rootPath, EnumerateOptions.None).ToList();
                var metadataFiles = new FileStats(
                    metadataFileInfos.Count, metadataFileInfos.Select(fileInfo => fileInfo.Length).Aggregate((s1, s2) => s1 + s2));

                await processFileInfoTask.ConfigureAwait(false);

                _logger.Always("Statistics for cache at {0}", rootPath);
                _logger.Always(new string('=', dividerWidth));
                _logger.Always(
                    "{0:N0} total content files ({1}) in cache",
                    allContentFiles.FileCount,
                    allContentFiles.ByteCount.ToSizeExpression());
                _logger.Always(
                    "{0:N0} private content files ({1}) in cache",
                    privateFiles.FileCount,
                    privateFiles.ByteCount.ToSizeExpression());
                _logger.Always(
                    "{0:N0} non-content files ({1})",
                    metadataFiles.FileCount,
                    metadataFiles.ByteCount.ToSizeExpression());

                _logger.Always(string.Empty);

                _logger.Always("Shared Content");
                _logger.Always(new string('-', dividerWidth));
                _logger.Always(
                    "{0:N0} shared files ({1}) in cache",
                    sharedFiles.FileCount,
                    sharedFiles.ByteCount.ToSizeExpression());
                _logger.Always(
                    "Use of hard links avoided duplication on disk of {0} for {1} savings",
                    cacheDeduplicatedFiles.ByteCount.ToSizeExpression(),
                    Percent(cacheDeduplicatedFiles.ByteCount, sharedFiles.ByteCount + cacheDeduplicatedFiles.ByteCount));
                _logger.Always(
                    "Use of hard links avoided duplication in output directories of {0} for {1} savings",
                    outputDeduplicatedFiles.ByteCount.ToSizeExpression(),
                    Percent(outputDeduplicatedFiles.ByteCount, sharedFiles.ByteCount + outputDeduplicatedFiles.ByteCount));
            }
            catch (Exception exception)
            {
                _logger.Error(exception.Message);
                _logger.Debug(exception);
            }
        }

        private IEnumerable<FileInfo> EnumerateBlobPathsFromDisk(AbsolutePath contentRootPath)
        {
            if (!_fileSystem.DirectoryExists(contentRootPath))
            {
                return Enumerable.Empty<FileInfo>();
            }

            return _fileSystem
                .EnumerateFiles(contentRootPath, EnumerateOptions.Recurse)
                .Where(fileInfo => fileInfo.FullPath.Path.EndsWith("blob", StringComparison.OrdinalIgnoreCase));
        }

        private struct FileStats
        {
            private long _fileCount;
            private long _byteCount;

            public long FileCount => _fileCount;

            public long ByteCount => _byteCount;

            public FileStats(long fileCount, long byteCount)
            {
                _fileCount = fileCount;
                _byteCount = byteCount;
            }

            public void Increase(long fileCount, long bytesPerFile)
            {
                Interlocked.Add(ref _fileCount, fileCount);
                Interlocked.Add(ref _byteCount, fileCount * bytesPerFile);
            }
        }
    }
}
