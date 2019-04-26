// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using LogEventId = BuildXL.Scheduler.Tracing.LogEventId;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "SharedOpaqueDirectoryTests")]
    [Feature(Features.SharedOpaqueDirectory)]
    public class DirectoryRenameTests : SchedulerIntegrationTestBase
    {
        private const string NestedDirSrc   = "nested-tmp";
        private const string NestedDirDest  = "nested";
        private const string FileNameInSrc  = "file-in-src";
        private const string FileNameInDest = "file-in-dest";

        public DirectoryRenameTests(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>
        /// Creates the following process
        /// 
        ///   CreateDir (sodPath)/(NestedDirSrc)
        ///   WriteFile (sodPath)/(NestedDirSrc)/(FileNameInSrc)
        ///   MoveDir   (sodPath)/(NestedDirSrc) (sodPath)/(NestedDirDest)
        ///   WriteFile (sodPath)/(NestedDirDest)/(FileNameInDest)
        /// </summary>
        private ProcessBuilder CreateMoveDirectoryProcessBuilder(AbsolutePath rootDir)
        {
            return CreatePipBuilder(new[]
            {
                OpCreateDir(NestedDirSrc),
                OpWriteFile(NestedDirSrc, FileNameInSrc),
                OpMoveDir(NestedDirSrc, NestedDirDest),
                OpWriteFile(NestedDirDest, FileNameInDest)
            });

            Operation OpCreateDir(string nestedDirName)
            {
                return Operation.CreateDir(SodDir(nestedDirName), doNotInfer: true);
            }

            Operation OpWriteFile(string nestedDirName, string fileName)
            {
                var file = FileArtifact.CreateOutputFile(SodDir(nestedDirName).Path.Combine(Context.PathTable, fileName));
                return Operation.WriteFile(file, doNotInfer: true);
            }

            Operation OpMoveDir(string srcDir, string destDir)
            {
                return Operation.MoveDir(srcPath: SodDir(srcDir), destPath: SodDir(destDir));
            }

            DirectoryArtifact SodDir(string nestedDirName)
            {
                return OutputDirectory.Create(rootDir.Combine(Context.PathTable, nestedDirName));
            }
        }

        [Fact]
        public void TestRenameDirectoryInsideSharedOpaqueDirectory()
        {
            AbsolutePath rootDirPath = CreateUniqueObjPath("sod-nested-mov");

            var pipBuilder = CreateMoveDirectoryProcessBuilder(rootDirPath);
            pipBuilder.AddOutputDirectory(rootDirPath, SealDirectoryKind.SharedOpaque);

            SchedulePipBuilder(pipBuilder);

            RunScheduler().AssertSuccess();
            AssertSharedOpaqueOputputsInNestedDestDirectory(rootDirPath);
        }

        [Fact]
        public void TestRenameUntrackedDirectoryToSharedOpaqueDirectory()
        {
            AbsolutePath rootDirPath = CreateUniqueObjPath("untracked-mov-sod");

            var pipBuilder = CreateMoveDirectoryProcessBuilder(rootDirPath);
            pipBuilder.AddUntrackedDirectoryScope(Combine(rootDirPath, NestedDirSrc));
            pipBuilder.AddOutputDirectory(Combine(rootDirPath, NestedDirDest), SealDirectoryKind.SharedOpaque);

            SchedulePipBuilder(pipBuilder);

            RunScheduler().AssertSuccess();
            AssertSharedOpaqueOputputsInNestedDestDirectory(rootDirPath);
        }

        [Fact]
        public void TestExplicitOutputsAndRenameUntrackedDirectory()
        {
            AbsolutePath rootDirPath = CreateUniqueObjPath("untracked-mov-sod");

            string root = ToString(rootDirPath);
            var expectedOutputs = new[]
            {
                X($"{root}/{NestedDirDest}/{FileNameInSrc}"),
                X($"{root}/{NestedDirDest}/{FileNameInDest}"),
            };

            var pipBuilder = CreateMoveDirectoryProcessBuilder(rootDirPath);
            pipBuilder.AddUntrackedDirectoryScope(Combine(rootDirPath, NestedDirSrc));
            foreach (var outPath in expectedOutputs)
            {
                pipBuilder.AddOutputFile(FileArtifact.CreateOutputFile(AbsolutePath.Create(Context.PathTable, outPath)));
            }

            SchedulePipBuilder(pipBuilder);

            RunScheduler().AssertSuccess();
        }

        private void AssertSharedOpaqueOputputsInNestedDestDirectory(AbsolutePath rootDirPath)
        {
            string root = ToString(rootDirPath);
            var expectedOutputs = new[]
            {
                X($"{root}/{NestedDirDest}/{FileNameInSrc}"),
                X($"{root}/{NestedDirDest}/{FileNameInDest}"),
            };

            foreach (var path in expectedOutputs)
            {
                XAssert.IsTrue(
                    SharedOpaqueOutputHelper.IsSharedOpaqueOutput(path),
                    $"Path '{path}' does not have magic shared opaque output timestamp");
            }
        }

        private string ToString(AbsolutePath path) => path.ToString(Context.PathTable);
    }
}
