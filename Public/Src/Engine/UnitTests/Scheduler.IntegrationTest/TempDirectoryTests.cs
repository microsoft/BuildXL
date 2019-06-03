// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.Domino.Scheduler
{
    /// <summary>
    /// Tests that validate file access policies and caching behavior
    /// </summary>
    [Feature(Features.TempDirectory)]
    public class TempDirectoryTests : SchedulerIntegrationTestBase
    {
        /// <summary>
        /// For tests that creates pips that declare temporary directories or files,
        /// which all share the same caching behavior.
        /// Used by Xunit Theory to run the test multiple times with different inputs.
        /// </summary>
        protected readonly struct TempArtifactType
        {
            public const int AdditionalTempDirectory = 0;
            public const int TempDirectory = 1;
            public const int TempFile = 2;
        }

        protected readonly string TempRoot;

        protected AbsolutePath TempRootPath { get; private set; }

        public TempDirectoryTests(ITestOutputHelper output) : base(output)
        {
            // Create a temp root
            TempRoot = Path.Combine(ObjectRoot, "temp");
            Directory.CreateDirectory(TempRoot);
            TempRootPath = AbsolutePath.Create(Context.PathTable, TempRoot);
            Expander.Add(Context.PathTable, new SemanticPathInfo(
                rootName: PathAtom.Create(Context.PathTable.StringTable, "TempRoot"),
                root: TempRootPath,
                allowHashing: true,
                readable: true,
                writable: true));
        }

        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        [InlineData(TempArtifactType.TempFile)]
        public void ValidateTempDirectoryCleaned(int tempArtifactType)
        {
            FileArtifact src = CreateSourceFile();
            FileArtifact tempOut = CreateOutputFileArtifact(TempRoot);

            // Produces file, which is not listed as output, in temporary directory
            var ops = new Operation[]
            {
                Operation.ReadFile(src),
                Operation.WriteFile(tempOut, doNotInfer: true), // appends random content
                Operation.WriteFile(tempOut, System.Environment.NewLine, doNotInfer: true), // append new line
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            Process pip = CreateAndScheduleTempDirProcess(ops, tempArtifactType, TempRootPath, tempOut.Path);

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Store output to compare
            string[] outA = File.ReadAllLines(ArtifactToString(tempOut));
            XAssert.IsTrue(outA.Length == 1);

            // Trigger cache miss
            File.WriteAllText(ArtifactToString(src), "src");
            RunScheduler().AssertCacheMiss(pip.PipId);

            // Store output to compare
            string[] outB = File.ReadAllLines(ArtifactToString(tempOut));
            XAssert.IsTrue(outB.Length == 1);

            // If temp folder was cleaned correctly between runs, outA and outB will be different
            XAssert.AreNotEqual(outA[0], outB[0]);
        }

        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        [InlineData(TempArtifactType.TempFile)]
        public void ValidateCachingTempDirectory(int tempArtifactType)
        {
            var pip = CreateAndSchedulePipWithTemp(tempArtifactType, out var tempOut);

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Modify file in temp directory /tempOut
            File.WriteAllText(ArtifactToString(tempOut), "tempOut");
            RunScheduler().AssertCacheHit(pip.PipId);

            // Add /newFile to temp directory
            File.WriteAllText(ArtifactToString(CreateOutputDirectoryArtifact(TempRoot)), "newFile");
            RunScheduler().AssertCacheHit(pip.PipId);

            // Add /newDir to temp directory
            Directory.CreateDirectory(ArtifactToString(CreateOutputDirectoryArtifact(TempRoot)));
            RunScheduler().AssertCacheHit(pip.PipId);

            // Delete temp directory
            Directory.Delete(TempRoot, recursive: true);
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        /// <summary>
        /// Tests that pips should not be able to consume input from other pips' temp directories.
        /// </summary>
        /// <param name="tempArtifactType"></param>
        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        [InlineData(TempArtifactType.TempFile)]
        public void FailOnTempInput(int tempArtifactType)
        {
            // First pip writes a file to its temp directory
            CreateAndSchedulePipWithTemp(tempArtifactType, out var tempOut);

            try
            {
                // Second pip consumes a file from pipA's temp directory
                CreateAndSchedulePipBuilder(new Operation[]
                {
                    Operation.ReadFile(tempOut),
                    Operation.WriteFile(CreateOutputFileArtifact())
                });
            }
            catch (System.Exception contractException)
            {
#pragma warning disable EPC12 // Suspicious exception handling: only Message property is observed in exception block.
                // input files in temp directories throw this contract exception
                contractException.Message.StartsWith("Assumption failed: Output artifact has no producer.");
#pragma warning restore EPC12 // Suspicious exception handling: only Message property is observed in exception block.

                // temp files error
                if (tempArtifactType == TempArtifactType.TempFile)
                {
                    AssertErrorEventLogged(EventId.InvalidInputSinceCorrespondingOutputIsTemporary);
                }
            }
        }

        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        [InlineData(TempArtifactType.TempFile)]
        public void FailWhenOnlyOutputIsTemporary(int tempArtifactType)
        {
            var tempOut = CreateOutputFileArtifact(TempRoot);
            Exception exception = null;
            try
            {
                CreateAndScheduleTempDirProcess(
                new Operation[] { },
                tempArtifactType,
                TempRootPath,
                tempOut.Path);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            XAssert.IsTrue(exception != null);
            XAssert.IsTrue(exception.Message.Contains("Failed to add process pip"));

            AssertErrorEventLogged(EventId.InvalidProcessPipDueToNoOutputArtifacts);
        }

        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        public void FailWhenTempDirectoryContainsSourceFile(int tempArtifactType)
        {
            CreateAndSchedulePipWithTemp(tempArtifactType, out var tempOut);

            // Declare a dependency on a source file within a different pip's temp directory
            var src = CreateSourceFile(TempRootPath);
            CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(src),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            PipGraphBuilder.Build();
            AssertErrorEventLogged(EventId.InvalidGraphSinceArtifactPathOverlapsTempPath);
        }

        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        public void FailWhenCopyFileOutputsToTemp(int tempArtifactType)
        {
            CreateAndSchedulePipWithTemp(tempArtifactType, out var tempOut);

            var srcFile = CreateSourceFile();
            var destFile = CreateOutputFileArtifact(TempRoot);
            var copyFilePip = CreateAndScheduleCopyFile(srcFile, destFile);

            PipGraphBuilder.Build();
            AssertErrorEventLogged(EventId.InvalidGraphSinceArtifactPathOverlapsTempPath);
        }

        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        public void FailWhenTempDirectoryContainsAnotherPipsOutputFile(int tempArtifactType)
        {
            CreateAndSchedulePipWithTemp(tempArtifactType, out var tempOut);

            // Pip attempts to declare an output in a temp directory (that was declared by a different pip)
            var pipInvalidOutInTempOps = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact(TempRoot))
            }).Process;

            PipGraphBuilder.Build();
            AssertErrorEventLogged(EventId.InvalidGraphSinceArtifactPathOverlapsTempPath);
        }


        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        public void FailWhenTempDirectoryContainsAnotherPipsOutputDirectory(int tempArtifactType)
        {
            CreateAndSchedulePipWithTemp(tempArtifactType, out var tempOut);

            // Pip attempts to declare an output directory in a temp directory (that was declared by a different pip)
            var builder = CreatePipBuilder(new Operation[] { });

            var opaqueDir = Path.Combine(TempRoot, "opaquedir");
            var opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            builder.AddOutputDirectory(opaqueDirPath, SealDirectoryKind.Opaque);
            SchedulePipBuilder(builder);

            PipGraphBuilder.Build();
            AssertErrorEventLogged(EventId.InvalidGraphSinceArtifactPathOverlapsTempPath);
        }

        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        public void FailWhenTempDirectoryContainsPipsOutputFile(int tempArtifactType)
        {
            var tempOut = CreateOutputFileArtifact(TempRoot);

            // Pip attempts to declare an output in its own temp
            CreateAndScheduleTempDirProcess(new Operation[]
            {
                Operation.WriteFile(tempOut),
                Operation.WriteFile(CreateOutputFileArtifact())
            },
            tempArtifactType,
            TempRootPath,
            tempOut.Path);

            PipGraphBuilder.Build();
            AssertErrorEventLogged(EventId.InvalidGraphSinceArtifactPathOverlapsTempPath);
        }

        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        public void FailWhenTempDirectoryContainsPipsOutputDirectory(int tempArtifactType)
        {
            var tempOut = CreateOutputFileArtifact(TempRoot);

            // Pip attempts to declare an output in its own temp
            var builder = CreateTempDirProcessBuilder(new Operation[]
            {
                Operation.WriteFile(tempOut, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            },
            tempArtifactType,
            TempRootPath,
            tempOut.Path);

            var opaqueDir = Path.Combine(TempRoot, "opaquedir");
            var opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            builder.AddOutputDirectory(opaqueDirPath, SealDirectoryKind.Opaque);
            SchedulePipBuilder(builder);

            PipGraphBuilder.Build();
            AssertErrorEventLogged(EventId.InvalidGraphSinceArtifactPathOverlapsTempPath);
        }

        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        public void FailWhenTempDirectoryIsAlsoPipsOutputDirectory(int tempArtifactType)
        {
            var tempDir = Path.Combine(TempRoot, "opaquedir");
            var tempDirPath = AbsolutePath.Create(Context.PathTable, tempDir);
            var tempOut = CreateOutputFileArtifact(tempDirPath);

            var builder = CreateTempDirProcessBuilder(
                new Operation[]
                {
                    Operation.WriteFile(CreateOutputFileArtifact())
                },
                tempArtifactType,
                tempRootPath: tempDirPath,
                tempOut: tempOut);

            // Pip tries to declare a temp directory or temp output as a directory output
            builder.AddOutputDirectory(tempDirPath, SealDirectoryKind.Opaque);
            var pip = SchedulePipBuilder(builder).Process;

            PipGraphBuilder.Build();
            AssertErrorEventLogged(EventId.InvalidGraphSinceArtifactPathOverlapsTempPath);
        }

        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        public void FailWhenTempDirectoryIsAlsoAnotherPipsOutputDirectory(int tempArtifactType)
        {
            var tempDir = Path.Combine(TempRoot, "opaquedir");
            var tempDirPath = AbsolutePath.Create(Context.PathTable, tempDir);
            CreateAndSchedulePipWithTemp(tempArtifactType, out var tempOut, tempRoot: tempDirPath);

            // Pip tries to declare a temp directory or temp output as a directory output
            var builderWithOutputDirectory = CreatePipBuilder(
                new Operation[]
                {
                    Operation.WriteFile(CreateOutputFileArtifact())
                });
            builderWithOutputDirectory.AddOutputDirectory(tempDirPath, SealDirectoryKind.Opaque);
            SchedulePipBuilder(builderWithOutputDirectory);

            PipGraphBuilder.Build();
            AssertErrorEventLogged(EventId.InvalidGraphSinceArtifactPathOverlapsTempPath);
        }

        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory, SealDirectoryKind.SharedOpaque)]
        [InlineData(TempArtifactType.TempDirectory, SealDirectoryKind.SharedOpaque)]
        [InlineData(TempArtifactType.AdditionalTempDirectory, SealDirectoryKind.Opaque)]
        [InlineData(TempArtifactType.TempDirectory, SealDirectoryKind.Opaque)]
        public void FailWhenTempDirectoryContainsSharedOpaque(int tempArtifactType, SealDirectoryKind sealDirectoryKind)
        {
            // Place a temp underneath previous pip's output directory
            CreateAndSchedulePipWithTemp(tempArtifactType, out var tempOut);

            var outputDir = Path.Combine(TempRoot, "opaquedir");
            var outputDirPath = AbsolutePath.Create(Context.PathTable, outputDir);

            var builderWithOutputDirectory = CreatePipBuilder(
                new Operation[]
                {
                    Operation.WriteFile(CreateOutputFileArtifact(outputDirPath), doNotInfer: true),
                });
            builderWithOutputDirectory.AddOutputDirectory(outputDirPath, sealDirectoryKind);
            SchedulePipBuilder(builderWithOutputDirectory);

            PipGraphBuilder.Build();
            AssertErrorEventLogged(EventId.InvalidGraphSinceArtifactPathOverlapsTempPath);
        }

        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        public void FailWhenSealedSourceDirectoryIsTempArtifact(int tempArtifactType)
        {
            var sealDir = SealDirectory(SourceRootPath, SealDirectoryKind.SourceAllDirectories);

            var builder = CreatePipBuilder(new Operation[]
                {
                    Operation.WriteFile(CreateOutputFileArtifact())
                });
            builder.AddInputDirectory(sealDir);
            SchedulePipBuilder(builder);

            CreateAndSchedulePipWithTemp(tempArtifactType, out var tempOut, tempRoot: sealDir.Path);

            PipGraphBuilder.Build();
            AssertErrorEventLogged(EventId.InvalidGraphSinceArtifactPathOverlapsTempPath);
        }

        [Theory]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        public void FailWhenTempDirectoryContainsTempDirectory(int tempArtifactType)
        {
            var parentTemp = CreateUniqueDirectory(TempRoot);
            CreateAndSchedulePipWithTemp(tempArtifactType, out var tempOut, parentTemp);

            var childTemp = CreateUniqueDirectory(parentTemp);
            CreateAndSchedulePipWithTemp(tempArtifactType, out var tempOut2, childTemp);

            PipGraphBuilder.Build();
        }

        [Theory(Skip = "Bug 1463540")]
        [InlineData(TempArtifactType.AdditionalTempDirectory)]
        [InlineData(TempArtifactType.TempDirectory)]
        public void FailWhenTwoPipsHaveSameTempDirectory(int tempArtifactType)
        {
            Exception exception = null;
            try
            {
                CreateAndSchedulePipWithTemp(tempArtifactType, out var tempOut1);
                CreateAndSchedulePipWithTemp(tempArtifactType, out var tempOut2);

                PipGraphBuilder.Build();
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            XAssert.AreNotEqual(null, exception);
        }

        /// <summary>
        /// This tests shows an inconsistent behavior regarding temp directories creation as mentioned in Bug 1549282.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void InconsistentTempDirectoriesCreation(bool ensureTempDirectoriesCreation)
        {
            Configuration.Sandbox.EnsureTempDirectoriesExistenceBeforePipExecution = ensureTempDirectoriesCreation;

            AbsolutePath additionalTempDirectory = TempRootPath.Combine(
                Context.PathTable, 
                nameof(InconsistentTempDirectoriesCreation) + "_" + ensureTempDirectoriesCreation);

            var pipBuilder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(CreateSourceFile()),
                Operation.CreateDir(DirectoryArtifact.CreateWithZeroPartialSealId(additionalTempDirectory), additionalArgs: "--failIfExists"),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            pipBuilder.EnableTempDirectory();
            pipBuilder.AdditionalTempDirectories = ReadOnlyArray<AbsolutePath>.FromWithoutCopy(new AbsolutePath[] { additionalTempDirectory });

            var processWithOutputs = SchedulePipBuilder(pipBuilder);
            var result = RunScheduler();

            if (ensureTempDirectoriesCreation)
            {
                result.AssertFailure();
                AssertErrorEventLogged(EventId.PipProcessError, 1);
            }
            else
            {
                result.AssertSuccess();
            }
        }

        /// <summary>
        /// Creates and schedules a pip that writes a file to a temp file or folder location as specified.
        /// </summary>
        /// <param name="tempArtifactType">
        /// <see cref="TempArtifactType"/>.
        /// </param>
        /// <param name="tempFileWritten">
        /// The path to the temp file that will be written by pip.
        /// </param>
        /// <param name="tempRoot">
        /// Optional location for the temp directory/file, defaults to <see cref="TempRootPath"/>.
        /// </param>
        private Process CreateAndSchedulePipWithTemp(int tempArtifactType, out FileArtifact tempFileWritten, AbsolutePath? tempRoot = null)
        {
            var tempRootPath = tempRoot ?? TempRootPath;

            tempFileWritten = CreateOutputFileArtifact(tempRootPath);
            var pip = CreateAndScheduleTempDirProcess(new Operation[]
            {
                Operation.WriteFile(tempFileWritten, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            },
            tempArtifactType,
            tempRootPath,
            tempFileWritten.Path);

            return pip;
        }

        protected ProcessBuilder CreateTempDirProcessBuilder(Operation[] ops, int tempArtifactType, AbsolutePath tempRootPath, AbsolutePath tempOut)
        {
            var pipBuilder = CreatePipBuilder(ops);
            switch (tempArtifactType)
            {
                case TempArtifactType.AdditionalTempDirectory:
                    var temp = new AbsolutePath[pipBuilder.AdditionalTempDirectories.Length + 1];
                    for (int i = 0; i < pipBuilder.AdditionalTempDirectories.Length; i++)
                    {
                        temp[i] = pipBuilder.AdditionalTempDirectories[i];
                    }

                    temp[pipBuilder.AdditionalTempDirectories.Length] = tempRootPath;
                    pipBuilder.AdditionalTempDirectories = ReadOnlyArray<AbsolutePath>.FromWithoutCopy(temp);
                    break;
                case TempArtifactType.TempDirectory:
                    pipBuilder.SetTempDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(tempRootPath));
                    break;
                case TempArtifactType.TempFile:
                    pipBuilder.AddOutputFile(tempOut, FileExistence.Temporary);
                    break;
                default:
                    XAssert.Fail("A test using a temporary file or directory didn't specify the temp artifact correctly.");
                    return null;
            }

            return pipBuilder;
        }

        protected Process CreateAndScheduleTempDirProcess(Operation[] ops, int tempArtifactType, AbsolutePath tempRootPath, AbsolutePath tempOut)
        {
            var pipBuilder = CreateTempDirProcessBuilder(ops, tempArtifactType, tempRootPath, tempOut);
            return SchedulePipBuilder(pipBuilder).Process;
        }
    }
}
