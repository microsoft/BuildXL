// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Native.IO;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.Scheduler
{
    [Trait("Category", "FileContentManagerTests")]
    public sealed partial class FileContentManagerTests : SchedulerTestBase
    {
        public FileContentManagerTests(ITestOutputHelper output) : base(output)
        {
        }

        private TestHarness CreateDefaultHarness()
        {
            return new TestHarness(
                Context,
                SourceRootPath.Combine(Context.PathTable, PathAtom.Create(Context.StringTable, "config.dsc")));
        }

        [Fact]
        public async Task RestoreContentInCacheCopySourceAndWriteFile()
        {
            var harness = CreateDefaultHarness();

            harness.Seal();

            FileArtifact copyChainFinalOutput = CreateOutputFile(fileName: "copyFileOutput.txt");
            var copyFileOutputPath = copyChainFinalOutput.Path.ToString(harness.PipContext.PathTable);

            string copyFileOutputContents = "copyFileOutput";

            harness.HashAndReportStringContent(copyFileOutputContents, copyChainFinalOutput);

            // Create the source file which is copied as a part of the copy chain
            FileArtifact copySource = CreateSourceFile(fileName: "copySource");
            harness.WriteText(copySource.Path, copyFileOutputContents);

            // Create the write file output which is copied
            string writeFileOutputContents = "writeFileOutput";
            FileArtifact writeFileOutput = CreateOutputFile(fileName: "writeFileOutput");
            FileArtifact copiedWriteFileOutput = CreateOutputFile(fileName: "copiedWriteFileOutput");
            harness.Environment.CopyFileSources[copiedWriteFileOutput] = writeFileOutput;

            // Ensure host can materialize write file output
            harness.Environment.HostMaterializedFileContents[writeFileOutput] = writeFileOutputContents;

            // Report only the hash of the copied write file output
            harness.HashAndReportStringContent(writeFileOutputContents, copiedWriteFileOutput);

            // Create copy chain
            FileArtifact copyChainFile1 = CreateOutputFile(fileName: "copyChainFile1");
            FileArtifact copyChainFile2 = CreateOutputFile(fileName: "copyChainFile2");
            FileArtifact copyChainFile3 = CreateOutputFile(fileName: "copyChainFile3");
            FileArtifact copyChainFile4 = CreateOutputFile(fileName: "copyChainFile4");

            harness.Environment.CopyFileSources[copyChainFile1] = copySource;
            harness.Environment.CopyFileSources[copyChainFile2] = copyChainFile1;
            harness.Environment.CopyFileSources[copyChainFile3] = copyChainFile2;
            harness.Environment.CopyFileSources[copyChainFile4] = copyChainFile3;
            harness.Environment.CopyFileSources[copyChainFinalOutput] = copyChainFile4;

            Assert.False(File.Exists(copyFileOutputPath));

            var consumer = CreateCmdProcess(
                dependencies: new[] { copiedWriteFileOutput, copyChainFinalOutput },
                outputs: new[] { CreateOutputFile() });

            // Hash the dependencies of a dummy process so the default input files for test processes are hashed
            // i.e. the process executable. TryMaterialize requires that all the file hashes have been reported
            var dummyHashResult = await harness.FileContentManager.TryHashDependenciesAsync(consumer, harness.UntrackedOpContext);
            Assert.True(dummyHashResult.Succeeded);
            Assert.False(File.Exists(copyFileOutputPath));

            // Call the file content manager to ensure that the source file is materialized
            var fileMaterializationResult = await harness.FileContentManager.TryMaterializeDependenciesAsync(consumer, harness.UntrackedOpContext);
            Assert.True(fileMaterializationResult);
            Assert.True(File.Exists(copyFileOutputPath));

            harness.VerifyContent(copyChainFinalOutput, copyFileOutputContents);
        }

        [Fact]
        public async Task SourceFilesMaterialization()
        {
            var harness = CreateDefaultHarness();
            
            // Source file materialization only happens on distributed workers
            harness.Configuration.Distribution.BuildRole = DistributedBuildRoles.Worker;
            harness.Configuration.Distribution.EnableSourceFileMaterialization = true;

            harness.Seal();

            var sourceFileContents = "Source file";

            FileArtifact sourceFile = CreateSourceFile(fileName: "source.txt");

            harness.WriteText(sourceFile, "Source file content which should be replaced");

            await harness.StoreAndReportStringContent(sourceFileContents, sourceFile);

            var dummyConsumer = CreateCmdProcess(
                dependencies: new FileArtifact[0],
                outputs: new[] { CreateOutputFile() });

            // Hash the dependencies of a dummy process so the default input files for test processes are hashed
            // i.e. the process executable. TryMaterialize requires that all the file hashes have been reported
            var dummyHashResult = await harness.FileContentManager.TryHashDependenciesAsync(dummyConsumer, harness.UntrackedOpContext);
            Assert.True(dummyHashResult.Succeeded);

            var consumer = CreateCmdProcess(
                dependencies: new[] { sourceFile },
                outputs: new[] { CreateOutputFile() });

            // Call the file content manager to ensure that the source file is materialized
            var fileMaterializationResult = await harness.FileContentManager.TryMaterializeDependenciesAsync(consumer, harness.UntrackedOpContext);
            Assert.True(fileMaterializationResult);

            harness.VerifyContent(sourceFile, sourceFileContents);
        }

        [Fact]
        public async Task HostFileMaterialization()
        {
            var harness = CreateDefaultHarness();

            harness.Seal();

            FileArtifact hostFileOutput = CreateOutputFile(fileName: "hostFileOutput.txt");
            var hostFileOutputPath = hostFileOutput.Path.ToString(harness.PipContext.PathTable);

            string hostFileOutputContents = "HostFileOutput";

            harness.Environment.HostMaterializedFileContents[hostFileOutput] = hostFileOutputContents;

            harness.HashAndReportStringContent(hostFileOutputContents, hostFileOutput);
            Assert.False(File.Exists(hostFileOutputPath));

            var consumer = CreateCmdProcess(
                dependencies: new[] { hostFileOutput },
                outputs: new[] { CreateOutputFile() });

            // Hash the dependencies of a dummy process so the default input files for test processes are hashed
            // i.e. the process executable. TryMaterialize requires that all the file hashes have been reported
            var dummyHashResult = await harness.FileContentManager.TryHashDependenciesAsync(consumer, harness.UntrackedOpContext);
            Assert.True(dummyHashResult.Succeeded);
            Assert.False(File.Exists(hostFileOutputPath));

            // Call the file content manager to ensure that the source file is materialized
            var fileMaterializationResult = await harness.FileContentManager.TryMaterializeDependenciesAsync(consumer, harness.UntrackedOpContext);
            Assert.True(fileMaterializationResult);
            Assert.True(File.Exists(hostFileOutputPath));

            harness.VerifyContent(hostFileOutput, hostFileOutputContents);
        }

        [Fact]
        public async Task DynamicDirectoryMaterializationDoesNotDeleteDeclaredFiles()
        {
            var harness = CreateDefaultHarness();
            harness.Seal();

            var pathTable = harness.Environment.Context.PathTable;

            DirectoryArtifact dynamicOutputDirectory = CreateDirectory();

            FileArtifact explicitDeclaredOutputFile = CreateOutputFile(rootPath: dynamicOutputDirectory.Path, fileName: "expOut.txt");
            string explicitNestedDeclaredOutputFileContents = "This is a nested declared file dependency";

            var nestedDirectory = dynamicOutputDirectory.Path.Combine(pathTable, "nested");
            FileArtifact explicitNestedDeclaredOutputFile = CreateOutputFile(rootPath: nestedDirectory, fileName: "expNestOut.txt");
            string explicitDeclaredOutputFileContents = "This is a declared file dependency";

            FileArtifact dynamicOutputFile = CreateOutputFile(rootPath: dynamicOutputDirectory.Path, fileName: "dynout.txt");
            string dynamicOutputFileContents = "This is a dynamic file dependency";

            await harness.StoreAndReportStringContent(explicitDeclaredOutputFileContents, explicitDeclaredOutputFile);
            await harness.StoreAndReportStringContent(explicitNestedDeclaredOutputFileContents, explicitNestedDeclaredOutputFile);
            await harness.StoreAndReportStringContent(dynamicOutputFileContents, dynamicOutputFile);

            const string PriorOutputText = "Prior output";
            harness.WriteText(dynamicOutputFile, PriorOutputText);
            harness.WriteText(explicitDeclaredOutputFile, PriorOutputText);
            harness.WriteText(explicitNestedDeclaredOutputFile, PriorOutputText);

            // Add some extraneous files/directories to be removed
            const string RemovedFileText = "Removed file text";
            var removedNestedDirectory = dynamicOutputDirectory.Path.Combine(pathTable, "removedNested");
            harness.WriteText(CreateOutputFile(rootPath: dynamicOutputDirectory.Path, fileName: "removedRootFile.txt"), RemovedFileText);
            harness.WriteText(CreateOutputFile(rootPath: dynamicOutputDirectory.Path, fileName: "removedRootFile2.txt"), RemovedFileText);
            harness.WriteText(CreateOutputFile(rootPath: nestedDirectory, fileName: "nestedremoved.txt"), RemovedFileText);
            harness.WriteText(CreateOutputFile(rootPath: nestedDirectory, fileName: "nestedremoved1.txt"), RemovedFileText);
            harness.WriteText(CreateOutputFile(rootPath: removedNestedDirectory, fileName: "removedNestedFile.txt"), RemovedFileText);
            Directory.CreateDirectory(dynamicOutputDirectory.Path.Combine(pathTable, "removedEmptyNested").ToString(pathTable));
            Directory.CreateDirectory(dynamicOutputDirectory.Path.Combine(pathTable, "removedEmptyNested3").ToString(pathTable));

            harness.Environment.RegisterDynamicOutputDirectory(dynamicOutputDirectory);

            var dynamicDirectoryContents = new[] { explicitDeclaredOutputFile, explicitNestedDeclaredOutputFile, dynamicOutputFile };

            // Report files for directory
            harness.FileContentManager.ReportDynamicDirectoryContents(
                dynamicOutputDirectory,
                dynamicDirectoryContents,
                PipOutputOrigin.NotMaterialized);

            var producer = CreateCmdProcess(
                dependencies: new FileArtifact[0],
                outputs: new[] { explicitDeclaredOutputFile, explicitNestedDeclaredOutputFile },
                directoryOutputs: new[] { dynamicOutputDirectory });

            var fileConsumer = CreateCmdProcess(
                dependencies: new[] { explicitDeclaredOutputFile, explicitNestedDeclaredOutputFile },
                outputs: new[] { CreateOutputFile() });

            var directoryConsumer = CreateCmdProcess(
                dependencies: new FileArtifact[0],
                directoryDependencies: new[] { dynamicOutputDirectory },
                outputs: new[] { CreateOutputFile() });

            // First materialize the files by materializing inputs for the pip which consumes the files directly
            var fileMaterializationResult = await harness.FileContentManager.TryMaterializeDependenciesAsync(fileConsumer, harness.UntrackedOpContext);
            Assert.True(fileMaterializationResult);

            // Verify that only the explicit declared dependencies have their contents materialized
            harness.VerifyContent(explicitDeclaredOutputFile, explicitDeclaredOutputFileContents);
            harness.VerifyContent(explicitNestedDeclaredOutputFile, explicitNestedDeclaredOutputFileContents);

            // The dynamic file shouldn't be materialized at this point
            harness.VerifyContent(dynamicOutputFile, PriorOutputText);

            // Modify the explicit outputs so that we can check later that materialization did not
            // delete and re-materialize these files
            const string ModifiedExplicitOutputText = "Modified explicit output";

            harness.WriteText(explicitDeclaredOutputFile, ModifiedExplicitOutputText);
            harness.WriteText(explicitNestedDeclaredOutputFile, ModifiedExplicitOutputText);

            // Now materialize the directory
            var directoryMaterializationResult = await harness.FileContentManager.TryMaterializeDependenciesAsync(directoryConsumer, harness.UntrackedOpContext);
            Assert.True(directoryMaterializationResult);

            var filesAfterMaterialization = new HashSet<string>(Directory.GetFiles(dynamicOutputDirectory.Path.ToString(pathTable), "*.*", SearchOption.AllDirectories), StringComparer.OrdinalIgnoreCase);

            HashSet<string> dynamicDirectoryContentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            dynamicDirectoryContentPaths.UnionWith(dynamicDirectoryContents.Select(f => f.Path.ToString(pathTable)));

            // Check that the dynamic directory contents are the only remaining files
            Assert.Subset(dynamicDirectoryContentPaths, filesAfterMaterialization);
            Assert.Superset(dynamicDirectoryContentPaths, filesAfterMaterialization);

            var directoriesAfterMaterialization = new HashSet<string>(Directory.GetDirectories(dynamicOutputDirectory.Path.ToString(pathTable), "*.*", SearchOption.AllDirectories), StringComparer.OrdinalIgnoreCase);

            HashSet<string> dynamicDirectorySubDirectoryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            dynamicDirectorySubDirectoryPaths.UnionWith(dynamicDirectoryContents.Select(f => f.Path.GetParent(pathTable).ToString(pathTable)));

            // Don't count the root directory
            dynamicDirectorySubDirectoryPaths.Remove(dynamicOutputDirectory.Path.ToString(pathTable));

            // Check that the dynamic directory contents parent directories are the only remaining directories
            Assert.Subset(dynamicDirectorySubDirectoryPaths, directoriesAfterMaterialization);
            Assert.Superset(dynamicDirectorySubDirectoryPaths, directoriesAfterMaterialization);

            XAssert.IsTrue(fileMaterializationResult);

            // Verify all files have the expected contents
            // Dynamic file should have the content for the cache
            // Explicit dependencies should have the modified explicit content because
            // the file content manager skips content which has been materialized already so
            // the later modifications should go unnoticed
            harness.VerifyContent(explicitDeclaredOutputFile, ModifiedExplicitOutputText);
            harness.VerifyContent(explicitNestedDeclaredOutputFile, ModifiedExplicitOutputText);

            harness.VerifyContent(dynamicOutputFile, dynamicOutputFileContents);
        }

        [Fact]
        public async Task TryLoadAvailableContentAsync()
        {
            var harness = CreateDefaultHarness();
            harness.Seal();

            FileArtifact sourceFile = CreateSourceFile(fileName: "source.txt");
            harness.WriteText(sourceFile, "Source file");

            FileArtifact outputFile = CreateOutputFile(fileName: "output.txt");
            harness.WriteText(outputFile, "Output file");

            var process = CreateCmdProcess(
                dependencies: new[] {sourceFile},
                outputs: new[] {outputFile});

            var possiblyStored = await
                harness.Environment.LocalDiskContentStore.TryStoreAsync(
                    harness.Environment.InMemoryContentCache,
                    FileRealizationMode.Copy,
                    path: outputFile.Path,
                    tryFlushPageCacheToFileSystem: false);

            XAssert.IsTrue(possiblyStored.Succeeded);

            harness.DeleteFileIfExists(outputFile.Path);
            var filesAndContentHashes = new List<(FileArtifact, ContentHash)>
                                        {
                                            (
                                                outputFile,
                                                possiblyStored.Result.Hash)
                                        };

            var loadAvailableOutput = await harness.FileContentManager.TryLoadAvailableOutputContentAsync(
                harness.CreatePipInfo(process),
                harness.UntrackedOpContext,
                filesAndContentHashes,
                onFailure: () => XAssert.Fail("TryLoadAvailableOutputContentAsync failed"),
                onContentUnavailable:
                    (i, s) =>
                        XAssert.Fail(
                            I(
                                $"Content of '{filesAndContentHashes[i].Item1.Path.ToString(harness.PipContext.PathTable)}' with content hash '{filesAndContentHashes[i].Item2.ToHex()}' is not available")));

            XAssert.IsTrue(loadAvailableOutput);
        }

        [Trait(Test.BuildXL.TestUtilities.Features.Feature, Test.BuildXL.TestUtilities.Features.NonStandardOptions)]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TryLoadAvailableContentAsyncWithCheckingFileOnDisk(bool checkFileOnDiskForUpToDate)
        {
            var harness = CreateDefaultHarness();
            harness.Configuration.Schedule.ReuseOutputsOnDisk = checkFileOnDiskForUpToDate;
            harness.Seal();

            FileArtifact sourceFile = CreateSourceFile(fileName: "source.txt");
            harness.WriteText(sourceFile, "Source file");

            FileArtifact outputFile = CreateOutputFile(fileName: "output.txt");
            harness.WriteText(outputFile, "Output file");

            var process = CreateCmdProcess(
                dependencies: new[] { sourceFile },
                outputs: new[] { outputFile });

            var possiblyStored = await
                harness.Environment.LocalDiskContentStore.TryStoreAsync(
                    harness.Environment.InMemoryContentCache,
                    FileRealizationMode.Copy,
                    path: outputFile.Path,
                    tryFlushPageCacheToFileSystem: false);

            XAssert.IsTrue(possiblyStored.Succeeded);

            harness.Environment.InMemoryContentCache.DiscardContentIfPresent(possiblyStored.Result.Hash, CacheSites.LocalAndRemote);

            var filesAndContentHashes = new List<(FileArtifact fileArtifact, ContentHash contentHash)>
                                        {
                                            (
                                                outputFile,
                                                possiblyStored.Result.Hash)
                                        };

            var loadAvailableOutput = await harness.FileContentManager.TryLoadAvailableOutputContentAsync(
                harness.CreatePipInfo(process),
                harness.UntrackedOpContext,
                filesAndContentHashes,
                onFailure: () => XAssert.Fail("TryLoadAvailableOutputContentAsync failed"),
                onContentUnavailable:
                    (i, s) =>
                    {
                        if (checkFileOnDiskForUpToDate)
                        {
                            XAssert.Fail(
                                I(
                                    $"Content of '{filesAndContentHashes[i].fileArtifact.Path.ToString(harness.PipContext.PathTable)}' with content hash '{filesAndContentHashes[i].contentHash.ToHex()}' is not available"));
                        }
                        else
                        {
                            XAssert.AreEqual(filesAndContentHashes[i].Item2.ToHex(), s);
                        }
                    });

            XAssert.IsTrue(checkFileOnDiskForUpToDate ? loadAvailableOutput : !loadAvailableOutput);
        }

        private class TestHarness
        {
            /// <summary>
            /// Execution environment.
            /// </summary>
            public DummyPipExecutionEnvironment Environment { get; private set; }

            /// <summary>
            /// File content manager.
            /// </summary>
            public FileContentManager FileContentManager { get; private set; }

            /// <summary>
            /// Configuration.
            /// </summary>
            public CommandLineConfiguration Configuration { get; }

            /// <summary>
            /// Pip execution context.
            /// </summary>
            public PipExecutionContext PipContext => Environment?.Context;

            /// <summary>
            /// Checks if harness is sealed.
            /// </summary>
            public bool IsSealed { get; private set; }

            /// <summary>
            /// Untracked operation context.
            /// </summary>
            public OperationContext UntrackedOpContext { get; private set; }

            private readonly BuildXLContext m_context;

            /// <summary>
            /// Constructor.
            /// </summary>
            public TestHarness(BuildXLContext context, AbsolutePath configFile)
            {
                m_context = context;
                Configuration = ConfigurationHelpers.GetDefaultForTesting(context.PathTable, configFile);
            }

            /// <summary>
            /// Seal the harness.
            /// </summary>
            public void Seal()
            {
                if (IsSealed)
                {
                    return;
                }

                Environment = new DummyPipExecutionEnvironment(CreateLoggingContextForTest(), m_context, Configuration, sandboxConnection: GetSandboxConnection());
                FileContentManager = new FileContentManager(Environment, new NullOperationTracker());
                UntrackedOpContext = OperationContext.CreateUntracked(Environment.LoggingContext);

                IsSealed = true;
            }

            /// <summary>
            /// Verifies content.
            /// </summary>
            public void VerifyContent(FileArtifact artifact, string expectedContents)
            {
                Contract.Requires(IsSealed);

                XAssert.AreEqual(expectedContents, File.ReadAllText(artifact.Path.ToString(PipContext.PathTable)));
            }

            /// <summary>
            /// Writes text.
            /// </summary>
            public void WriteText(AbsolutePath path, string text)
            {
                Contract.Requires(IsSealed);

                var filePath = path.ToString(PipContext.PathTable);
                WriteText(filePath, text);
            }

            /// <summary>
            /// Writes text.
            /// </summary>
            public void WriteText(string filePath, string text)
            {
                Contract.Requires(IsSealed);

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                FileUtilities.DeleteFile(filePath);
                File.WriteAllText(filePath, text);
            }

            /// <summary>
            /// Deletes file if exists.
            /// </summary>
            /// <param name="path"></param>
            public void DeleteFileIfExists(AbsolutePath path)
            {
                Contract.Requires(IsSealed);

                var expandedPath = path.ToString(PipContext.PathTable);
                if (File.Exists(expandedPath))
                {
                    File.Delete(expandedPath);
                }
            }

            /// <summary>
            /// Creates pip info.
            /// </summary>
            public PipInfo CreatePipInfo(Pip pip)
            {
                return new PipInfo(pip, PipContext);
            }

            /// <summary>
            /// Stores and reports string content.
            /// </summary>
            public void HashAndReportStringContent(string content, FileArtifact file)
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                var hash = ContentHashingUtilities.HashBytes(bytes);
                FileContentManager.ReportInputContent(
                    file,
                    FileMaterializationInfo.CreateWithUnknownName(
                        new FileContentInfo(hash, bytes.Length)));
            }

            /// <summary>
            /// Stores and reports string content.
            /// </summary>
            public async Task StoreAndReportStringContent(string content, FileArtifact file)
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                var hash = ContentHashingUtilities.HashBytes(bytes);
                var artifactCache = Environment.Cache.ArtifactContentCache;
                using (var stream = new MemoryStream(bytes))
                {
                    var storeResult = await artifactCache.TryStoreAsync(stream, hash);
                    XAssert.IsTrue(storeResult.Succeeded);

                    FileContentManager.ReportInputContent(
                        file,
                        FileMaterializationInfo.CreateWithUnknownName(
                            new FileContentInfo(hash, bytes.Length)));
                }
            }
        }
    }
}
