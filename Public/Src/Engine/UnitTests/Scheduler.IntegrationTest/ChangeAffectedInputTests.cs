// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class ChangeAffectedInputTests : SchedulerIntegrationTestBase
    {
        public ChangeAffectedInputTests(ITestOutputHelper output) : base(output)
        {
            Environment.SetEnvironmentVariable("[Sdk.BuildXL]qCodeCoverageEnumType", "DynamicCodeCov");
        }

        [Fact]
        public void DirectAffectedFileInputTest()
        {
            // aInput->(pipA)->aOutput->(pipB)->bOutput
            // Execepted change affected input for pipB is aOutput.

            // Process A.
            var dir = Path.Combine(ObjectRoot, "Dir");
            var dirPath = AbsolutePath.Create(Context.PathTable, dir);
            FileArtifact aInput = CreateSourceFile(root: dirPath, prefix: "pip-a-input-file");
            FileArtifact aOutput = CreateOutputFileArtifact(root: dirPath, prefix: "pip-a-out-file");

            var pipBuilderA= CreatePipBuilder(new[] { Operation.ReadFile(aInput), Operation.WriteFile(aOutput) });
            SchedulePipBuilder(pipBuilderA);

            // Process B.
            FileArtifact bOutput = CreateOutputFileArtifact(root: dirPath, prefix: "pip-b-out-file");
            FileArtifact changeAffectedWrittenFile = CreateOutputFileArtifact();
            var pipBuilderB = CreatePipBuilder(new[] { Operation.ReadFile(aOutput), Operation.WriteFile(bOutput) });
            pipBuilderB.SetEnvironmentVariable(StringId.Create(Context.StringTable, "[Sdk.BuildXL]qCodeCoverageEnumType"), "");
            SchedulePipBuilder(pipBuilderB);

            RunScheduler().AssertSuccess();

            ResetPipGraphBuilder();
            SchedulePipBuilder(pipBuilderA);
            SchedulePipBuilder(pipBuilderB);

            RunScheduler().AssertCacheHit();

            ResetPipGraphBuilder();
            SchedulePipBuilder(pipBuilderA);
            pipBuilderB.SetEnvironmentVariable(StringId.Create(Context.StringTable, "[Sdk.BuildXL]qCodeCoverageEnumType"), "DynamicCodeCov");
            pipBuilderB.SetChangeAffectedInputListWrittenFilePath(changeAffectedWrittenFile);
            SchedulePipBuilder(pipBuilderB);


            var inputChangesFile = CreateOutputFileArtifact();
            File.WriteAllText(ArtifactToString(inputChangesFile), ArtifactToString(aInput));
            Configuration.Schedule.InputChanges = inputChangesFile.Path;
            Environment.SetEnvironmentVariable("[Sdk.BuildXL]qCodeCoverageEnumType", "DynamicCodeCov");

            RunScheduler().AssertCacheMiss();

            var actualAffectedInput = File.ReadAllText(ArtifactToString(changeAffectedWrittenFile));
            var expectedAffectedInput = aOutput.Path.GetName(Context.PathTable).ToString(Context.PathTable.StringTable);
            XAssert.AreEqual(expectedAffectedInput, actualAffectedInput);
        }

        [Fact]
        public void DirectAffectedDiretoryInputTest()
        {
            // aInputDir -> (pipA) -> aOutputDir -> (PipB) -> bOutputDir
            //  |- pip-a-input-file   |- pip-a-out-file       |- pip-b-out-file           
            //                        |- aSubOutputDir
            //                         |- pip-a-out-in-sub-file
            // Execepted change affected input for pipB is pip-a-out-file and pip-a-out-in-sub-file

            var aInputDir = Path.Combine(ObjectRoot, "input");
            var aInputDirPath = AbsolutePath.Create(Context.PathTable, aInputDir);
            var aInputDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(aInputDirPath);
            var aInputFile = CreateSourceFile(root: aInputDirPath, prefix: "pip-a-input-file");
            File.WriteAllText(ArtifactToString(aInputFile), "pipABuild1");
            var sealInputDir = SealDirectory(aInputDirPath, SealDirectoryKind.SourceAllDirectories);


            var aOutputDir = Path.Combine(ObjectRoot, "aOutputDir");
            var aOutputDirPath = AbsolutePath.Create(Context.PathTable, aOutputDir);
            var aOutputDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(aOutputDirPath);
            var aOutputFileInOutputeDir = CreateOutputFileArtifact(root: aOutputDir, prefix: "pip-a-out-file");
            var aOutputSubDir = Path.Combine(aOutputDir, "aSubOutputDir");
            var aOutputSubDirPath = AbsolutePath.Create(Context.PathTable, aOutputSubDir);
            var aOutputSubDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(aOutputSubDirPath);
            var aOutputFileInOutputSubDir = CreateOutputFileArtifact(root: aOutputSubDir, prefix: "pip-a-out-in-sub-file");

            var bOutDir = Path.Combine(ObjectRoot, "bOutputDir");
            var bOutDirPath = AbsolutePath.Create(Context.PathTable, bOutDir);
            var bOutDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(bOutDirPath);
            var bOutFileArtifact = CreateOutputFileArtifact(root: bOutDirArtifact, prefix: "pip-b-out-file");

            var pipBuilderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(aInputFile, doNotInfer: true),
                Operation.WriteFile(aOutputFileInOutputeDir, doNotInfer: true),
                Operation.CreateDir(aOutputSubDirArtifact, doNotInfer:true),
                Operation.WriteFile(aOutputFileInOutputSubDir, doNotInfer: true),
            });
            pipBuilderA.AddInputDirectory(sealInputDir);
            pipBuilderA.AddOutputDirectory(aOutputDirArtifact, SealDirectoryKind.Opaque);
            var pipA = SchedulePipBuilder(pipBuilderA);

            var changeAffectedWrittenFile = CreateOutputFileArtifact();
            var pipBuilderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(aOutputFileInOutputSubDir, doNotInfer: true),
                Operation.WriteFile(bOutFileArtifact, doNotInfer: true),
            });
            pipBuilderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(aOutputDirPath));
            pipBuilderB.AddOutputDirectory(bOutDirArtifact, SealDirectoryKind.Opaque);
            pipBuilderB.SetChangeAffectedInputListWrittenFilePath(changeAffectedWrittenFile);
            var pipB = SchedulePipBuilder(pipBuilderB);

            var inputChangesFile = CreateOutputFileArtifact();
            File.WriteAllText(ArtifactToString(inputChangesFile), ArtifactToString(aInputFile));
            Configuration.Schedule.InputChanges = inputChangesFile.Path;

            RunScheduler().AssertSuccess();

            string[] actualAffectedSortedInputs = File.ReadAllLines(ArtifactToString(changeAffectedWrittenFile)).OrderBy(p => p, StringComparer.InvariantCultureIgnoreCase).ToArray();
            string[] expectedAffectedInputs = { aOutputFileInOutputSubDir.Path.GetName(Context.PathTable).ToString(Context.PathTable.StringTable),
                                                aOutputFileInOutputeDir.Path.GetName(Context.PathTable).ToString(Context.PathTable.StringTable) };
            string[] expectedAffectedSortedInputs = expectedAffectedInputs.OrderBy(p => p, StringComparer.InvariantCultureIgnoreCase).ToArray();
            XAssert.AreArraysEqual(expectedAffectedSortedInputs, actualAffectedSortedInputs, true);
        }

        public enum InputAccessType
        {
            DynamicFileAccess,
            DirectoryInput,
        }

        [Theory]
        [InlineData(InputAccessType.DynamicFileAccess)]
        [InlineData(InputAccessType.DirectoryInput)]
        public void TransitiveAffectedDirectoryInputTest(InputAccessType pipBInputAccessType)
        {
            // aInputDir -> (pipA) -> aOutputDir -> (pipB) -> bOutputDir -> (pipC) -> pip-c-out-file           
            //  |- pip-a-input-file    |- pip-a-out-file       |- pip-b-out-file   
            //                         |- aSubOutputDir
            //                            |- pip-a-out-in-sub-file
            // Execepted change affected input for pipC is pip-b-out-file

            var aInputDir = Path.Combine(ObjectRoot, "input");
            var aInputDirPath = AbsolutePath.Create(Context.PathTable, aInputDir);
            var aInputDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(aInputDirPath);
            var aInputFile = CreateSourceFile(root: aInputDirPath, prefix: "pip-a-input-file");
            File.WriteAllText(ArtifactToString(aInputFile), "pipABuild1");
            var sealInputDir = SealDirectory(aInputDirPath, SealDirectoryKind.SourceAllDirectories);

            var aOutputDir = Path.Combine(ObjectRoot, "aOutputDir");
            var aOutputDirPath = AbsolutePath.Create(Context.PathTable, aOutputDir);
            var aOutputDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(aOutputDirPath);

            var aOutputFileInOutputeDir = CreateOutputFileArtifact(root: aOutputDir, prefix: "pip-a-out-file");

            var aOutputSubDir = Path.Combine(aOutputDir, "aSubOutputDir");
            var aOutputSubDirPath = AbsolutePath.Create(Context.PathTable, aOutputSubDir);
            var aOutputSubDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(aOutputSubDirPath);
            var aOutputFileInOutputSubDir = CreateOutputFileArtifact(root: aOutputSubDir, prefix: "pip-a-out-in-sub-file");

            var bOutDir = Path.Combine(ObjectRoot, "bOutputDir");
            var bOutDirPath = AbsolutePath.Create(Context.PathTable, bOutDir);
            var bOutDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(bOutDirPath);
            var bOutFileArtifact = CreateOutputFileArtifact(root: bOutDirArtifact, prefix: "pip-b-out-file");

            var cOutFileArtifact = CreateOutputFileArtifact(prefix: "pip-c-out-file");
            var expectedAffectedInput = "";

            var pipBuilderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(aInputFile, doNotInfer: true),
                Operation.WriteFile(aOutputFileInOutputeDir, doNotInfer: true),
                Operation.CreateDir(aOutputSubDirArtifact, doNotInfer:true),
                Operation.WriteFile(aOutputFileInOutputSubDir, doNotInfer: true),
            });
            pipBuilderA.AddInputDirectory(sealInputDir);
            pipBuilderA.AddOutputDirectory(aOutputDirArtifact, SealDirectoryKind.Opaque); 
            var pipA = SchedulePipBuilder(pipBuilderA);

            var operations = new List<Operation>() { };
            if (pipBInputAccessType == InputAccessType.DynamicFileAccess)
            {
                operations.Add(Operation.ReadFile(aOutputFileInOutputeDir, doNotInfer: true));
                expectedAffectedInput = bOutFileArtifact.Path.GetName(Context.PathTable).ToString(Context.PathTable.StringTable);
            }
            operations.Add(Operation.WriteFile(bOutFileArtifact, doNotInfer: true));
            var pipBuilderB = CreatePipBuilder(operations);
            pipBuilderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(aOutputDirPath));
            pipBuilderB.AddOutputDirectory(bOutDirArtifact, SealDirectoryKind.Opaque);            
            var pipB = SchedulePipBuilder(pipBuilderB);

            var changeAffectedWrittenFile = CreateOutputFileArtifact();
            var pipBuilderC = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(bOutFileArtifact, doNotInfer: true),
                Operation.WriteFile(cOutFileArtifact),
            });
            pipBuilderC.AddInputDirectory(bOutDirArtifact);
            pipBuilderC.SetChangeAffectedInputListWrittenFilePath(changeAffectedWrittenFile);
            var pipC = SchedulePipBuilder(pipBuilderC);

            var inputChangesFile = CreateOutputFileArtifact();
            File.WriteAllText(ArtifactToString(inputChangesFile), ArtifactToString(aInputFile));
            Configuration.Schedule.InputChanges = inputChangesFile.Path;

            RunScheduler().AssertSuccess();
              
            var actualAffectedInput = File.ReadAllText(ArtifactToString(changeAffectedWrittenFile));
            XAssert.AreEqual(expectedAffectedInput, actualAffectedInput);  
        }

        [Theory]
        [InlineData(InputAccessType.DynamicFileAccess, true)]
        [InlineData(InputAccessType.DynamicFileAccess, false)]
        [InlineData(InputAccessType.DirectoryInput)]
        public void TransitiveAffectedDirectoryInputWithSealTest(InputAccessType pipBInputAccessType, bool accessExistingFile = false)
        {
            // aInputDir -> (pipA) ->pip-a-out-file-> (copy) ->pip-a-output-file-copy -> (seal) -> copyDir -> (pipB)     ->   bOutputDir -> (pipC) -> pip-c-out-file           
            //  |- pip-a-input-file                                                 /               |- pip-a-output-file-copy  |- pip-b-out-file   
            //                                                         existing-file                |- existing-file
            //                            
            // Execepted change affected input for pipC is pip-b-out-file or ""      

            var aInputDir = Path.Combine(ObjectRoot, "input");
            var aInputDirPath = AbsolutePath.Create(Context.PathTable, aInputDir);
            var aInputDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(aInputDirPath);
            var aInputFile = CreateSourceFile(root: aInputDirPath, prefix: "pip-a-input-file");
            File.WriteAllText(ArtifactToString(aInputFile), "pipABuild1");
            var sealInputDir = SealDirectory(aInputDirPath, SealDirectoryKind.SourceAllDirectories);

            var aOutputDir = Path.Combine(ObjectRoot, "aOutputDir");
            var aOutputDirPath = AbsolutePath.Create(Context.PathTable, aOutputDir);
            var aOutputDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(aOutputDirPath);
            var aOutputFileInOutputeDir = CreateOutputFileArtifact(root: aOutputDir, prefix: "pip-a-out-file");

            var copyDir = Path.Combine(ObjectRoot, "copyDir");
            var copyDirPath = AbsolutePath.Create(Context.PathTable, copyDir);
            var copyFilePath = CreateSourceFile(root: copyDirPath, prefix: "pip-a-output-file-copy").Path;

            var aExistingFileInOutputeDir = CreateSourceFile(root: copyDirPath, prefix: "existing-file");
            File.WriteAllText(ArtifactToString(aExistingFileInOutputeDir), "This is an existing file");

            var bOutDir = Path.Combine(ObjectRoot, "bOutputDir");
            var bOutDirPath = AbsolutePath.Create(Context.PathTable, bOutDir);
            var bOutDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(bOutDirPath);
            var bOutFileArtifact = CreateOutputFileArtifact(root: bOutDirArtifact, prefix: "pip-b-out-file");

            var cOutFileArtifact = CreateOutputFileArtifact(prefix: "pip-c-out-file");

            var expectedAffectedInput = "";

            var pipBuilderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(aInputFile, doNotInfer: true),
                Operation.WriteFile(aOutputFileInOutputeDir),
            });
            pipBuilderA.AddInputDirectory(sealInputDir);
            var pipA = SchedulePipBuilder(pipBuilderA);

            var copiedFile = CopyFile(aOutputFileInOutputeDir, copyFilePath);
            var sealedaOutput = SealDirectory(copyDirPath, SealDirectoryKind.Full, aExistingFileInOutputeDir, copiedFile);

            var operations = new List<Operation>() { };
            if (pipBInputAccessType == InputAccessType.DynamicFileAccess)
            {
                if (accessExistingFile)
                {
                    operations.Add(Operation.ReadFile(aExistingFileInOutputeDir, doNotInfer: true));
                }
                else
                {
                    operations.Add(Operation.ReadFile(copiedFile, doNotInfer: true));
                    expectedAffectedInput = bOutFileArtifact.Path.GetName(Context.PathTable).ToString(Context.PathTable.StringTable);
                }
            }
            operations.Add(Operation.WriteFile(bOutFileArtifact, doNotInfer: true));
            var pipBuilderB = CreatePipBuilder(operations);
            pipBuilderB.AddInputDirectory(sealedaOutput);
            pipBuilderB.AddOutputDirectory(bOutDirArtifact, SealDirectoryKind.Opaque);
            var pipB = SchedulePipBuilder(pipBuilderB);


            var changeAffectedWrittenFile = CreateOutputFileArtifact();
            var pipBuilderC = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(bOutFileArtifact, doNotInfer: true),
                Operation.WriteFile(cOutFileArtifact),
            });
            pipBuilderC.AddInputDirectory(bOutDirArtifact);
            pipBuilderC.SetChangeAffectedInputListWrittenFilePath(changeAffectedWrittenFile);
            var pipC = SchedulePipBuilder(pipBuilderC);

            var inputChangesFile = CreateOutputFileArtifact();
            File.WriteAllText(ArtifactToString(inputChangesFile), ArtifactToString(aInputFile));
            Configuration.Schedule.InputChanges = inputChangesFile.Path;

            RunScheduler().AssertSuccess();

            var actualAffectedInput = File.ReadAllText(ArtifactToString(changeAffectedWrittenFile));
            XAssert.AreEqual(expectedAffectedInput, actualAffectedInput);
        }
    }
}
