// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using Microsoft.Build.Prediction.StandardPredictors.CopyTask;
using Xunit;

namespace Microsoft.Build.Prediction.Tests.StandardPredictors
{
    public class CopyTaskPredictorTests : TestBase
    {
        internal const string CopyTestsDirectoryPath = @"TestsData\Copy\";

        private readonly BuildInput _copy1Dll = new BuildInput("copy1.dll", false);
        private readonly BuildInput _copy2Dll = new BuildInput("copy2.dll", false);
        private readonly BuildInput _copy3Dll = new BuildInput(@"Copy\copy3.dll", false);

        protected override string TestsDirectoryPath => CopyTestsDirectoryPath;

        [Fact]
        public void TestDefaultTargetDestinationFilesCopyProject()
        {
            BuildInput[] expectedInputs =
            {
                _copy1Dll,
                _copy2Dll,
            };

            BuildOutputDirectory[] expectedOutputs =
            {
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder1")),
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder2")),
            };

            var predictor = new CopyTaskPredictor();
            ParseAndVerifyProject("destinationFilesCopy.csproj", predictor, expectedInputs, expectedOutputs);
        }

        [Fact]
        public void TestCustomTargetFilesCopy()
        {
            BuildInput[] expectedInputs =
            {
                _copy1Dll,
                _copy2Dll,
            };

            BuildOutputDirectory[] expectedOutputs =
            {
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder1")),
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder2")),
            };

            var predictor = new CopyTaskPredictor();
            ParseAndVerifyProject("customTargetWithCopy.csproj", predictor, expectedInputs, expectedOutputs);
        }

        /// <summary>
        /// Tests copy parsing for non-standard default targets.
        /// Makes sure that when DefaultTargets/InitialTargets specified differ from the default Build target,
        /// we ignore the default and only consider the Build target.
        /// </summary>
        [Fact]
        public void TestCopyParseNonStandardDefaultTargets()
        {
            BuildInput[] expectedInputs =
            {
                _copy1Dll,
            };

            BuildOutputDirectory[] expectedOutputs =
            {
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder1")),
            };

            var predictor = new CopyTaskPredictor();
            ParseAndVerifyProject("CopyDefaultCustomTargets.csproj", predictor, expectedInputs, expectedOutputs);
        }

        /// <summary>
        /// Tests the copy inputs before after targets.
        /// Scenario: With MSBuild v4.0, Target Synchronization can happen with DependsOnTargets
        /// and Before/After targets on the downstream target. This ensures that inputs from those copy tasks are captured in the predictions.
        /// We need to use a custom targets file since all targets in the project are automatically added to be parsed and do not test the logic
        /// of target synchronizations.
        /// </summary>
        [Fact]
        public void TestCopyInputsBeforeAfterTargets()
        {
            BuildInput[] expectedInputs =
            {
                _copy1Dll,
                _copy2Dll,
            };

            BuildOutputDirectory[] expectedOutputs =
            {
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder1")),
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder3")),
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder4")),
            };

            var predictor = new CopyTaskPredictor();
            ParseAndVerifyProject("CopyCustomImportedTargets.csproj", predictor, expectedInputs, expectedOutputs);
        }

        /// <summary>
        /// Tests the copy batched items with this file macros.
        /// Scenario: Copy tasks are allowed to declare inputs with MSBuild batching. To capture more dependency
        /// closure to get a more complete DGG, parsing these batched inputs is recommended. In the absence of such
        /// parsing, users would have to declare QCustomInput/Outputs for each of the batched copies.
        /// Additionally, copy tasks in targets that exist in other folders can use $(MSBuildThisFile) macros that evaluate
        /// to something outside the Project's context. These need to be evaluated correctly as well.
        /// </summary>
        [Fact]
        public void TestCopyBatchedItemsWithThisFileMacros()
        {
            BuildInput[] expectedInputs =
            {
                _copy1Dll,
                _copy2Dll,

                // TODO: Note double backslash in test - add path normalization and canonicalization to input and output paths.
                new BuildInput(Path.Combine(Environment.CurrentDirectory, CopyTestsDirectoryPath) + @"Copy\\copy3.dll", false),
            };

            BuildOutputDirectory[] expectedOutputs =
            {
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder1")),
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder2")),
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder3")),
            };

            var predictor = new CopyTaskPredictor();
            ParseAndVerifyProject("CopyTestBatchingInputs.csproj", predictor, expectedInputs, expectedOutputs);
        }

        /// <summary>
        /// Test that copy tasks with batch inputs work.
        /// </summary>
        [Fact]
        public void TestCopyBatchingDestinationFolder()
        {
            BuildInput[] expectedInputs =
            {
                _copy1Dll,
                _copy2Dll,
                new BuildInput("SomeFile.cs", false),
            };

            BuildOutputDirectory[] expectedOutputs =
            {
                // TODO: Note trailing backslash in test - add path normalization and canonicalization to input and output paths.
                new BuildOutputDirectory(CreateAbsolutePath(@"debug\amd64\")),
            };

            var predictor = new CopyTaskPredictor();
            ParseAndVerifyProject("CopyTestBatchingDestinationFolder.csproj", predictor, expectedInputs, expectedOutputs);
        }

        [Fact]
        public void TestCopyParseTimeNotExistFilesCopyProject()
        {
            BuildInput[] expectedInputs =
            {
                new BuildInput("NotExist1.dll", false),
                new BuildInput("NotExist2.dll", false),
            };

            BuildOutputDirectory[] expectedOutputs =
            {
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder1")),
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder2")),
            };

            var predictor = new CopyTaskPredictor();
            ParseAndVerifyProject("copyparsetimenotexistfile.csproj", predictor, expectedInputs, expectedOutputs);
        }

        [Fact]
        public void TestWildcardsInIncludeCopyProject()
        {
            BuildInput[] expectedInputs =
            {
                _copy1Dll,
                _copy2Dll,
                _copy3Dll,
            };

            BuildOutputDirectory[] expectedOutputs =
            {
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder1")),
            };

            var predictor = new CopyTaskPredictor();
            ParseAndVerifyProject("wildcardsInIncludeCopy.csproj", predictor, expectedInputs, expectedOutputs);
        }

        [Fact]
        public void TestIncludeViaItemGroupCopyProject()
        {
            BuildInput[] expectedInputs =
            {
                _copy1Dll,
                _copy2Dll,
            };

            BuildOutputDirectory[] expectedOutputs =
            {
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder1")),
            };

            var predictor = new CopyTaskPredictor();
            ParseAndVerifyProject("IncludeViaItemGroupCopy.csproj", predictor, expectedInputs, expectedOutputs);
        }

        [Fact]
        public void TestDestinationFilesItemTransformationCopyProject()
        {
            BuildInput[] expectedInputs =
            {
                _copy1Dll,
                _copy2Dll,
            };

            BuildOutputDirectory[] expectedOutputs =
            {
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder1")),
            };

            var predictor = new CopyTaskPredictor();
            ParseAndVerifyProject("DestinationFilesItemTransformation.csproj", predictor, expectedInputs, expectedOutputs);
        }

        [Fact]
        public void TestMultipleTargetsDestinationFolderCopyProject()
        {
            BuildInput[] expectedInputs =
            {
                _copy1Dll,
                _copy2Dll,
            };

            BuildOutputDirectory[] expectedOutputs =
            {
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder1")),
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder2")),
            };

            var predictor = new CopyTaskPredictor();
            ParseAndVerifyProject("destinationFolderMultipleTargetsCopy.csproj", predictor, expectedInputs, expectedOutputs);
        }

        [Fact]
        public void TestTargetDependsOnCopyProject()
        {
            BuildInput[] expectedInputs =
            {
                _copy1Dll,
                _copy2Dll,
            };

            BuildOutputDirectory[] expectedOutputs =
            {
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder1")),
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder2")),
            };

            var predictor = new CopyTaskPredictor();
            ParseAndVerifyProject("TargetDependsOnCopy.csproj", predictor, expectedInputs, expectedOutputs);
        }

        [Fact]
        public void TestTargetConditionInCopyProject()
        {
            BuildInput[] expectedInputs =
            {
                _copy1Dll,
            };

            BuildOutputDirectory[] expectedOutputs =
            {
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder1")),
            };

            var predictor = new CopyTaskPredictor();
            ParseAndVerifyProject("TargetConditionInCopy.csproj", predictor, expectedInputs, expectedOutputs);
        }

        [Fact]
        public void TestTaskConditionInCopyProject()
        {
            BuildInput[] expectedInputs =
            {
                _copy1Dll,
            };

            BuildOutputDirectory[] expectedOutputs =
            {
                new BuildOutputDirectory(CreateAbsolutePath(@"target\debug\amd64\folder1")),
            };

            var predictor = new CopyTaskPredictor();
            ParseAndVerifyProject("TaskConditionInCopy.csproj", predictor, expectedInputs, expectedOutputs);
        }
    }
}
