// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BuildXL.ML.PipUsage;
using BuildXL.ML.Runtime;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    public sealed class PipUsageModelTests
    {
        [Fact]
        public void RejectsIpcPipsWithInvalidExecutablePath()
        {
            XAssert.IsFalse(PipUsageModel.CanEvaluate(PipType.Ipc, AbsolutePath.Invalid));
        }

        [Fact]
        public void RejectsProcessPipsWithInvalidExecutablePath()
        {
            XAssert.IsFalse(PipUsageModel.CanEvaluate(PipType.Process, AbsolutePath.Invalid));
        }

        [Fact]
        public void AcceptsProcessPipsWithValidExecutablePath()
        {
            var pathTable = new PathTable();
            var executablePath = AbsolutePath.Create(pathTable, OperatingSystemHelper.IsWindowsOS ? @"C:\tool.exe" : "/tool");

            XAssert.IsTrue(PipUsageModel.CanEvaluate(PipType.Process, executablePath));
        }

        [Fact]
        public void ColdDetectionRequiresMissingCpuAndDuration()
        {
            XAssert.IsTrue(PipUsageModel.IsCold(0, 0));
            XAssert.IsFalse(PipUsageModel.IsCold(1, 0));
            XAssert.IsFalse(PipUsageModel.IsCold(0, 1));
        }

        [Fact]
        public void EvaluationModeDistinguishesColdAndWarmPips()
        {
            XAssert.IsTrue(PipUsageModel.ShouldEvaluate(PipUsageMLMode.Cold, isCold: true, historicDataUnavailable: false));
            XAssert.IsFalse(PipUsageModel.ShouldEvaluate(PipUsageMLMode.Cold, isCold: false, historicDataUnavailable: false));
            XAssert.IsTrue(PipUsageModel.ShouldEvaluate(PipUsageMLMode.ColdAndWarm, isCold: true, historicDataUnavailable: false));
            XAssert.IsTrue(PipUsageModel.ShouldEvaluate(PipUsageMLMode.ColdAndWarm, isCold: false, historicDataUnavailable: false));
            XAssert.IsFalse(PipUsageModel.ShouldEvaluate(PipUsageMLMode.Disabled, isCold: true, historicDataUnavailable: true));
        }

        [Fact]
        public void EvaluationModeDistinguishesUnavailableHistoricTable()
        {
            XAssert.IsFalse(PipUsageModel.ShouldEvaluate(PipUsageMLMode.HistoricDataUnavailable, isCold: true, historicDataUnavailable: false));
            XAssert.IsTrue(PipUsageModel.ShouldEvaluate(PipUsageMLMode.HistoricDataUnavailable, isCold: false, historicDataUnavailable: true));
            XAssert.IsTrue(PipUsageModel.ShouldEvaluate(PipUsageMLMode.Cold, isCold: true, historicDataUnavailable: false));
            XAssert.IsTrue(PipUsageModel.ShouldEvaluate(PipUsageMLMode.Cold, isCold: true, historicDataUnavailable: true));
        }

        [Theory]
        [InlineData(42.0, true, 42.0)]
        [InlineData(-42.0, true, 0.0)]
        [InlineData(double.NaN, false, 0.0)]
        [InlineData(double.PositiveInfinity, false, 0.0)]
        [InlineData(double.NegativeInfinity, false, 0.0)]
        public void PredictionNormalizationRejectsNonFiniteValues(double value, bool expectedSuccess, double expectedPrediction)
        {
            bool success = PipUsageModel.TryNormalizePrediction(value, out double prediction);

            XAssert.AreEqual(expectedSuccess, success);
            XAssert.AreEqual(expectedPrediction, prediction);
        }

        [Theory]
        [InlineData(42.1, 43)]
        [InlineData(101.0, 101)]
        [InlineData(250.0, 250)]
        [InlineData(-1.0, 0)]
        [InlineData(double.NaN, 0)]
        [InlineData(double.PositiveInfinity, int.MaxValue)]
        [InlineData(double.MaxValue, int.MaxValue)]
        public void CpuPredictionPreservesMultiCorePercent(double value, int expected)
        {
            var prediction = new PipUsagePrediction(value, 0, 0, 0);
            XAssert.AreEqual(expected, prediction.CpuPercent);
        }

        [Theory]
        [InlineData(1.0001, 1001u)]
        [InlineData(-1.0, 0u)]
        [InlineData(double.NaN, 0u)]
        [InlineData(double.PositiveInfinity, uint.MaxValue)]
        [InlineData(double.MaxValue, uint.MaxValue)]
        public void DurationConversionSaturates(double seconds, uint expectedMilliseconds)
        {
            var prediction = new PipUsagePrediction(0, 0, 0, seconds);
            XAssert.AreEqual(expectedMilliseconds, prediction.DurationMilliseconds);
        }

        [Fact]
        public void ModelSpecRejectsMissingCategoricalVocabulary()
        {
            PipUsageModelSpec spec = CreateValidSpec();
            spec.Vocabularies = new Dictionary<string, IReadOnlyList<string>>();

            Assert.Contains("missing a vocabulary", spec.Validate());
        }

        [Fact]
        public void ModelSpecRejectsDuplicateCategoricalVocabularyValue()
        {
            PipUsageModelSpec spec = CreateValidSpec();
            spec.Vocabularies = new Dictionary<string, IReadOnlyList<string>>
            {
                [PipUsageFeatureNames.Tool] = new[] { "rare", "rare" },
            };

            Assert.Contains("empty or duplicate", spec.Validate());
        }

        [Fact]
        public void LightGbmModelRejectsOutOfRangeSplitFeature()
        {
            const string Json = @"{
                ""feature_names"": [""Weight""],
                ""tree_info"": [{
                    ""tree_structure"": {
                        ""split_feature"": 1,
                        ""decision_type"": ""<="",
                        ""threshold"": 0,
                        ""left_child"": { ""leaf_value"": 0 },
                        ""right_child"": { ""leaf_value"": 1 }
                    }
                }]
            }";

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json)))
            {
                Assert.Throws<FormatException>(() => LightGbmModel.Parse(stream));
            }
        }

#if MICROSOFT_INTERNAL
        [Fact]
        public void EmbeddedModelLoads()
        {
            PipUsageModel model = PipUsageModel.TryLoadEmbedded(out string error);
            XAssert.IsNotNull(model, error);
            Assert.Null(error);
        }

        [Fact]
        public void EmbeddedModelMatchesPythonParityCases()
        {
            PipUsageModel model = PipUsageModel.TryLoadEmbedded(out string error);
            XAssert.IsNotNull(model, error);

            Assembly assembly = typeof(PipUsageModelTests).GetTypeInfo().Assembly;
            string resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith("pipUsageTest.test_cases.json", StringComparison.Ordinal));
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (JsonDocument document = JsonDocument.Parse(stream))
            {
                JsonElement predictions = document.RootElement.GetProperty("predictions");
                ValidateTarget(model, predictions, PipUsageTarget.Cpu);
                ValidateTarget(model, predictions, PipUsageTarget.Memory);
                ValidateTarget(model, predictions, PipUsageTarget.AverageMemory);
                ValidateTarget(model, predictions, PipUsageTarget.Duration);
            }
        }

        [Fact]
        public void EmbeddedModelRejectsMissingInputFeatures()
        {
            PipUsageModel model = PipUsageModel.TryLoadEmbedded(out string error);
            XAssert.IsNotNull(model, error);

            PipUsagePrediction? prediction = model.TryEvaluate(
                new Dictionary<string, string>(),
                new Dictionary<string, double>(),
                out error);

            XAssert.IsFalse(prediction.HasValue);
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.Contains(nameof(InvalidOperationException), error);
            Assert.Contains("Required categorical feature", error);
        }

        [Fact]
        public void EmbeddedModelReportsInputExceptionsWithoutThrowing()
        {
            PipUsageModel model = PipUsageModel.TryLoadEmbedded(out string error);
            XAssert.IsNotNull(model, error);

            PipUsagePrediction? prediction = model.TryEvaluate(null, null, out error);

            XAssert.IsFalse(prediction.HasValue);
            Assert.Contains(nameof(NullReferenceException), error);
        }

        [Fact]
        public void EmbeddedModelReportsMetadataExceptionsWithoutThrowing()
        {
            PipUsageModel model = PipUsageModel.TryLoadEmbedded(out string error);
            XAssert.IsNotNull(model, error);

            // Access to pip metadata is inside the best-effort evaluation boundary as well.
            PipUsagePrediction? prediction = model.TryEvaluate(null, default, null, null, default, out error);

            XAssert.IsFalse(prediction.HasValue);
            Assert.Contains(nameof(NullReferenceException), error);
        }

        [Fact]
        public void NumericFixtureNullRepresentsMissingValue()
        {
            using (JsonDocument document = JsonDocument.Parse(@"{ ""ExpectedDurationSec"": null, ""Weight"": 1 }"))
            {
                Dictionary<string, double> numeric = DeserializeNumericFeatures(document.RootElement);
                Assert.True(double.IsNaN(numeric["ExpectedDurationSec"]));
                XAssert.AreEqual(1.0, numeric["Weight"]);
            }
        }

        private static void ValidateTarget(PipUsageModel model, JsonElement predictions, PipUsageTarget target)
        {
            string targetName = JsonNamingPolicy.SnakeCaseLower.ConvertName(target.ToString());
            foreach (JsonElement testCase in predictions.GetProperty(targetName).EnumerateArray())
            {
                var categorical = JsonSerializer.Deserialize<Dictionary<string, string>>(testCase.GetProperty("categorical").GetRawText());
                var numeric = DeserializeNumericFeatures(testCase.GetProperty("numeric"));
                PipUsagePrediction? prediction = model.TryEvaluate(categorical, numeric, out string error);
                XAssert.IsTrue(prediction.HasValue, error);
                Assert.Null(error);
                double expected = Math.Max(0.0, testCase.GetProperty("prediction").GetDouble());
                Assert.True(Math.Abs(expected - prediction.Value.GetRawValue(target)) <= 1e-6, $"{targetName} prediction mismatch.");
            }
        }

        private static Dictionary<string, double> DeserializeNumericFeatures(JsonElement element)
        {
            var numeric = new Dictionary<string, double>();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                numeric[property.Name] = property.Value.ValueKind == JsonValueKind.Null
                    ? double.NaN
                    : property.Value.GetDouble();
            }

            return numeric;
        }
#endif

        private static PipUsageModelSpec CreateValidSpec()
        {
            return new PipUsageModelSpec
            {
                SchemaVersion = 1,
                ModelKind = "pipUsage",
                Features = new[] { PipUsageFeatureNames.Tool, PipUsageFeatureNames.Weight },
                CategoricalFeatures = new[] { PipUsageFeatureNames.Tool },
                Vocabularies = new Dictionary<string, IReadOnlyList<string>>
                {
                    [PipUsageFeatureNames.Tool] = new[] { "rare" },
                },
                RareBucket = "rare",
                ModelFiles = new ModelFiles
                {
                    Cpu = "cpu.json",
                    Memory = "memory.json",
                    AverageMemory = "average_memory.json",
                    Duration = "duration.json",
                },
                OutputTransform = "expm1",
            };
        }
    }
}
