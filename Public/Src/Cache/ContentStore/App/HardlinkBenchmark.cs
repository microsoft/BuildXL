// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using CLAP;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.UtilitiesCore;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     HardLinkBenchmark verb.
        /// </summary>
        [Verb(Description = "Run a set of hardlink scenarios for benchmarking")]
        internal void HardLinkBenchmark
            (
            [Required,
             Description("Root directory under which the hardlinks will be created. Choose a location on the disk you wish to measure.")] string rootPath,
            [DefaultValue(null), Description(".csv output file.")] string outputPath
            )
        {
            var root = new AbsolutePath(rootPath);
            var sourceRoot = root / "sources";
            var destinationRoot = root / "destinations";

            int[] fileCounts = {20000, 50000, 100000};
            int[] sourceDirectoryCounts = {1, 16, 4096};
            int[] destinationDirectoryCounts = {1, 16, 4096};

            var results = new StringBuilder();
            results.AppendLine("FileCount, SourceDirectoryCount, DestinationDirectoryCount, ElapsedSeconds");

            foreach (var fileCount in fileCounts)
            {
                foreach (var sourceDirectoryCount in sourceDirectoryCounts)
                {
                    var sourceDir = sourceRoot / $"{fileCount}.{sourceDirectoryCount}";
                    SetUpSourceDirectories(sourceDir, fileCount, sourceDirectoryCount);

                    foreach (var destinationDirectoryCount in destinationDirectoryCounts)
                    {
                        var destinationDir = destinationRoot / $"{fileCount}.{sourceDirectoryCount}.{destinationDirectoryCount}";
                        SetUpDestinationDirectories(destinationDir, destinationDirectoryCount);

                        LetDiskSettle(rootPath);

                        _logger.Always(
                            $"Linking {fileCount} files from {sourceDirectoryCount} source directories to {destinationDirectoryCount} destination directories.");
                        Stopwatch stopwatch = Stopwatch.StartNew();
                        int counter = 0;
                        foreach (var sourceFileInfo in _fileSystem.EnumerateFiles(sourceDir, EnumerateOptions.Recurse))
                        {
                            var source = sourceFileInfo.FullPath;
                            var destination = destinationDir / ThreadSafeRandom.Generator.Next(destinationDirectoryCount).ToString() /
                                              counter.ToString();
                            var createHardLinkResult = _fileSystem.CreateHardLink(source, destination, false);
                            if (createHardLinkResult != CreateHardLinkResult.Success)
                            {
                                _logger.Always(
                                    $"Failed to link from {source} to {destination}. CreateHardLink result was {createHardLinkResult}.");
                            }

                            counter++;
                        }

                        var elapsed = stopwatch.Elapsed;
                        _logger.Always(
                            $"Linked {fileCount} files from {sourceDirectoryCount} directories to {destinationDirectoryCount} directories in {elapsed.TotalSeconds}s");

                        results.AppendLine($"{fileCount}, {sourceDirectoryCount}, {destinationDirectoryCount}, {elapsed.TotalSeconds}");
                    }
                }
            }

            _logger.Always(results.ToString());
            if (outputPath != null)
            {
                _fileSystem.WriteAllBytes(new AbsolutePath(outputPath), Encoding.UTF8.GetBytes(results.ToString()));
            }
        }

        private void SetUpSourceDirectories(AbsolutePath sourceDir, int fileCount, int sourceDirectoryCount)
        {
            _logger.Always($"Deleting source directory: {sourceDir}");
            if (_fileSystem.DirectoryExists(sourceDir))
            {
                _fileSystem.DeleteDirectory(sourceDir, DeleteOptions.All);
            }

            _logger.Always($"Creating source directory: {sourceDir}");
            _fileSystem.CreateDirectory(sourceDir);
            for (var i = 0; i < fileCount; i++)
            {
                var subDirectory = sourceDir / ThreadSafeRandom.Generator.Next(sourceDirectoryCount).ToString();
                _fileSystem.CreateDirectory(subDirectory);
                var filePath = subDirectory / (i + ".txt");
                _fileSystem.WriteAllBytes(filePath, Encoding.UTF8.GetBytes($"file contents {i}"));
            }
        }

        private void SetUpDestinationDirectories(AbsolutePath destinationDir, int destinationDirectoryCount)
        {
            _logger.Always($"Deleting destination directory: {destinationDir}");
            if (_fileSystem.DirectoryExists(destinationDir))
            {
                _fileSystem.DeleteDirectory(destinationDir, DeleteOptions.All);
            }

            for (int i = 0; i < destinationDirectoryCount; i++)
            {
                _fileSystem.CreateDirectory(destinationDir / i.ToString());
            }
        }

        // Let the disk settle before measuring.
        private void LetDiskSettle(string root)
        {
            _logger.Always("Flushing disk and waiting for it to settle...");
            try
            {
                var driveRoot = Path.GetPathRoot(root);
                if (driveRoot != null)
                {
                    _fileSystem.FlushVolume(driveRoot[0]);
                }
            }
            catch (IOException ex)
            {
                _logger.Error(ex, $"Failed to flush disk for hard link benchmark at {root}.");
            }

            Thread.Sleep(60000);
        }
    }
}
