// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MsBuildGraphBuilderTool;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.ProjectGraphBuilder
{
    public class MsBuildPredictionCollectorTests : TemporaryStorageTestBase
    {
        public MsBuildPredictionCollectorTests(ITestOutputHelper output): base(output)
        {
        }

        [Fact]
        public void AddOutputFileHandlesAbsolutePaths()
        {
            string absoluteDirectoryPath = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
            string absoluteFilePath = Path.Combine(absoluteDirectoryPath, Guid.NewGuid().ToString());

            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildOutputPredictionCollector(outputFolderPredictions, predictionFailures);

            collector.AddOutputFile(absoluteFilePath, TemporaryDirectory, "Mock");

            Assert.Equal(1, outputFolderPredictions.Count);
            Assert.Contains(absoluteDirectoryPath, outputFolderPredictions);

            Assert.Equal(0, predictionFailures.Count);
        }

        [Fact]
        public void AddOutputFileHandlesRelativePaths()
        {
            string relativeDirectoryPath = Guid.NewGuid().ToString();
            string relativeFilePath = Path.Combine(relativeDirectoryPath, Guid.NewGuid().ToString());
            string absoluteDirectoryPath = Path.Combine(TemporaryDirectory, relativeDirectoryPath);

            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildOutputPredictionCollector(outputFolderPredictions, predictionFailures);

            collector.AddOutputFile(relativeFilePath, TemporaryDirectory, "Mock");

            Assert.Equal(1, outputFolderPredictions.Count);
            Assert.Contains(absoluteDirectoryPath, outputFolderPredictions);

            Assert.Equal(0, predictionFailures.Count);
        }

        [Fact]
        public void AddOutputFileHandlesBadPaths()
        {
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildOutputPredictionCollector(outputFolderPredictions, predictionFailures);

            collector.AddOutputFile("!@#$%^&*()\0", TemporaryDirectory, "Mock");

            Assert.Equal(0, outputFolderPredictions.Count);

            Assert.Equal(1, predictionFailures.Count);
            Assert.Equal("Mock", predictionFailures.Single().predictorName);
            Assert.Contains("!@#$%^&*()\0", predictionFailures.Single().failure);
        }

        [Fact]
        public void AddOutputDirectoryHandlesAbsolutePaths()
        {
            string absoluteDirectoryPath = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());

            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildOutputPredictionCollector(outputFolderPredictions, predictionFailures);

            collector.AddOutputDirectory(absoluteDirectoryPath, TemporaryDirectory, "Mock");

            Assert.Equal(1, outputFolderPredictions.Count);
            Assert.Contains(absoluteDirectoryPath, outputFolderPredictions);

            Assert.Equal(0, predictionFailures.Count);
        }

        [Fact]
        public void AddOutputDirectoryHandlesRelativePaths()
        {
            string relativeDirectoryPath = Path.Combine(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
            string absoluteDirectoryPath = Path.Combine(TemporaryDirectory, relativeDirectoryPath);

            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildOutputPredictionCollector(outputFolderPredictions, predictionFailures);

            collector.AddOutputDirectory(relativeDirectoryPath, TemporaryDirectory, "Mock");

            Assert.Equal(1, outputFolderPredictions.Count);
            Assert.Contains(absoluteDirectoryPath, outputFolderPredictions);

            Assert.Equal(0, predictionFailures.Count);
        }

        [Fact]
        public void AddOutputDirectoryHandlesBadPaths()
        {
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildOutputPredictionCollector(outputFolderPredictions, predictionFailures);

            collector.AddOutputDirectory("!@#$%^&*()\0", TemporaryDirectory, "Mock");

            Assert.Equal(0, outputFolderPredictions.Count);

            Assert.Equal(1, predictionFailures.Count);
            Assert.Equal("Mock", predictionFailures.Single().predictorName);
            Assert.Contains("!@#$%^&*()", predictionFailures.Single().failure);
        }
    }
}