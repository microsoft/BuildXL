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
        public void AddInputFileHandlesAbsolutePaths()
        {
            string absolutePath = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

            var inputFilePredictions = new List<string>();
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildPredictionCollector(inputFilePredictions, outputFolderPredictions, predictionFailures);

            collector.AddInputFile(absolutePath, TemporaryDirectory, "Mock");

            Assert.Equal(1, inputFilePredictions.Count);
            Assert.Contains(absolutePath, inputFilePredictions);

            Assert.Equal(0, outputFolderPredictions.Count);

            Assert.Equal(0, predictionFailures.Count);
        }

        [Fact]
        public void AddInputFileHandlesRelativePaths()
        {
            string relativePath = Path.Combine(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
            string absolutePath = Path.Combine(TemporaryDirectory, relativePath);

            var inputFilePredictions = new List<string>();
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildPredictionCollector(inputFilePredictions, outputFolderPredictions, predictionFailures);

            collector.AddInputFile(relativePath, TemporaryDirectory, "Mock");

            Assert.Equal(1, inputFilePredictions.Count);
            Assert.Contains(absolutePath, inputFilePredictions);

            Assert.Equal(0, outputFolderPredictions.Count);

            Assert.Equal(0, predictionFailures.Count);
        }

        [Fact]
        public void AddInputFileHandlesBadPaths()
        {
            var inputFilePredictions = new List<string>();
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildPredictionCollector(inputFilePredictions, outputFolderPredictions, predictionFailures);

            collector.AddInputFile("!@#$%^&*()", TemporaryDirectory, "Mock");

            Assert.Equal(0, inputFilePredictions.Count);

            Assert.Equal(0, outputFolderPredictions.Count);

            Assert.Equal(1, predictionFailures.Count);
            Assert.Equal("Mock", predictionFailures.Single().predictorName);
            Assert.Contains("!@#$%^&*()", predictionFailures.Single().failure);
        }

        [Fact]
        public void AddInputDirectoryHandlesAbsolutePaths()
        {
            string absoluteDirectoryPath = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
            string absoluteFilePath1 = Path.Combine(absoluteDirectoryPath, Guid.NewGuid().ToString());
            string absoluteFilePath2 = Path.Combine(absoluteDirectoryPath, Guid.NewGuid().ToString());

            Directory.CreateDirectory(absoluteDirectoryPath);
            File.WriteAllText(absoluteFilePath1, Guid.NewGuid().ToString());
            File.WriteAllText(absoluteFilePath2, Guid.NewGuid().ToString());

            var inputFilePredictions = new List<string>();
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildPredictionCollector(inputFilePredictions, outputFolderPredictions, predictionFailures);

            collector.AddInputDirectory(absoluteDirectoryPath, TemporaryDirectory, "Mock");

            Assert.Equal(2, inputFilePredictions.Count);
            Assert.Contains(absoluteFilePath1, inputFilePredictions);
            Assert.Contains(absoluteFilePath2, inputFilePredictions);

            Assert.Equal(0, outputFolderPredictions.Count);

            Assert.Equal(0, predictionFailures.Count);
        }

        [Fact]
        public void AddInputDirectoryHandlesRelativePaths()
        {
            string relativeDirectoryPath = Path.Combine(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
            string absoluteDirectoryPath = Path.Combine(TemporaryDirectory, relativeDirectoryPath);
            string absoluteFilePath1 = Path.Combine(absoluteDirectoryPath, Guid.NewGuid().ToString());
            string absoluteFilePath2 = Path.Combine(absoluteDirectoryPath, Guid.NewGuid().ToString());

            Directory.CreateDirectory(absoluteDirectoryPath);
            File.WriteAllText(absoluteFilePath1, Guid.NewGuid().ToString());
            File.WriteAllText(absoluteFilePath2, Guid.NewGuid().ToString());

            var inputFilePredictions = new List<string>();
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildPredictionCollector(inputFilePredictions, outputFolderPredictions, predictionFailures);

            collector.AddInputDirectory(relativeDirectoryPath, TemporaryDirectory, "Mock");

            Assert.Equal(2, inputFilePredictions.Count);
            Assert.Contains(absoluteFilePath1, inputFilePredictions);
            Assert.Contains(absoluteFilePath2, inputFilePredictions);

            Assert.Equal(0, outputFolderPredictions.Count);

            Assert.Equal(0, predictionFailures.Count);
        }

        [Fact]
        public void AddInputDirectoryHandlesMissingPaths()
        {
            string absoluteDirectoryPath = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());

            var inputFilePredictions = new List<string>();
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildPredictionCollector(inputFilePredictions, outputFolderPredictions, predictionFailures);

            collector.AddInputDirectory(absoluteDirectoryPath, TemporaryDirectory, "Mock");

            // The folder didn't exist, so ignore the prediction
            Assert.Equal(0, inputFilePredictions.Count);
            Assert.Equal(0, outputFolderPredictions.Count);
            Assert.Equal(0, predictionFailures.Count);
        }

        [Fact]
        public void AddInputDirectoryHandlesBadPaths()
        {
            var inputFilePredictions = new List<string>();
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildPredictionCollector(inputFilePredictions, outputFolderPredictions, predictionFailures);

            collector.AddInputDirectory("!@#$%^&*()", TemporaryDirectory, "Mock");

            Assert.Equal(0, inputFilePredictions.Count);

            Assert.Equal(0, outputFolderPredictions.Count);

            Assert.Equal(1, predictionFailures.Count);
            Assert.Equal("Mock", predictionFailures.Single().predictorName);
            Assert.Contains("!@#$%^&*()", predictionFailures.Single().failure);
        }

        [Fact]
        public void AddOutputFileHandlesAbsolutePaths()
        {
            string absoluteDirectoryPath = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
            string absoluteFilePath = Path.Combine(absoluteDirectoryPath, Guid.NewGuid().ToString());

            var inputFilePredictions = new List<string>();
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildPredictionCollector(inputFilePredictions, outputFolderPredictions, predictionFailures);

            collector.AddOutputFile(absoluteFilePath, TemporaryDirectory, "Mock");

            Assert.Equal(0, inputFilePredictions.Count);

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

            var inputFilePredictions = new List<string>();
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildPredictionCollector(inputFilePredictions, outputFolderPredictions, predictionFailures);

            collector.AddOutputFile(relativeFilePath, TemporaryDirectory, "Mock");

            Assert.Equal(0, inputFilePredictions.Count);

            Assert.Equal(1, outputFolderPredictions.Count);
            Assert.Contains(absoluteDirectoryPath, outputFolderPredictions);

            Assert.Equal(0, predictionFailures.Count);
        }

        [Fact]
        public void AddOutputFileHandlesBadPaths()
        {
            var inputFilePredictions = new List<string>();
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildPredictionCollector(inputFilePredictions, outputFolderPredictions, predictionFailures);

            collector.AddOutputFile("!@#$%^&*()", TemporaryDirectory, "Mock");

            Assert.Equal(0, inputFilePredictions.Count);

            Assert.Equal(0, outputFolderPredictions.Count);

            Assert.Equal(1, predictionFailures.Count);
            Assert.Equal("Mock", predictionFailures.Single().predictorName);
            Assert.Contains("!@#$%^&*()", predictionFailures.Single().failure);
        }

        [Fact]
        public void AddOutputDirectoryHandlesAbsolutePaths()
        {
            string absoluteDirectoryPath = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());

            var inputFilePredictions = new List<string>();
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildPredictionCollector(inputFilePredictions, outputFolderPredictions, predictionFailures);

            collector.AddOutputDirectory(absoluteDirectoryPath, TemporaryDirectory, "Mock");

            Assert.Equal(0, inputFilePredictions.Count);

            Assert.Equal(1, outputFolderPredictions.Count);
            Assert.Contains(absoluteDirectoryPath, outputFolderPredictions);

            Assert.Equal(0, predictionFailures.Count);
        }

        [Fact]
        public void AddOutputDirectoryHandlesRelativePaths()
        {
            string relativeDirectoryPath = Path.Combine(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
            string absoluteDirectoryPath = Path.Combine(TemporaryDirectory, relativeDirectoryPath);

            var inputFilePredictions = new List<string>();
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildPredictionCollector(inputFilePredictions, outputFolderPredictions, predictionFailures);

            collector.AddOutputDirectory(relativeDirectoryPath, TemporaryDirectory, "Mock");

            Assert.Equal(0, inputFilePredictions.Count);

            Assert.Equal(1, outputFolderPredictions.Count);
            Assert.Contains(absoluteDirectoryPath, outputFolderPredictions);

            Assert.Equal(0, predictionFailures.Count);
        }

        [Fact]
        public void AddOutputDirectoryHandlesBadPaths()
        {
            var inputFilePredictions = new List<string>();
            var outputFolderPredictions = new List<string>();
            var predictionFailures = new ConcurrentQueue<(string predictorName, string failure)>();
            var collector = new MsBuildPredictionCollector(inputFilePredictions, outputFolderPredictions, predictionFailures);

            collector.AddOutputDirectory("!@#$%^&*()", TemporaryDirectory, "Mock");

            Assert.Equal(0, inputFilePredictions.Count);

            Assert.Equal(0, outputFolderPredictions.Count);

            Assert.Equal(1, predictionFailures.Count);
            Assert.Equal("Mock", predictionFailures.Single().predictorName);
            Assert.Contains("!@#$%^&*()", predictionFailures.Single().failure);
        }
    }
}