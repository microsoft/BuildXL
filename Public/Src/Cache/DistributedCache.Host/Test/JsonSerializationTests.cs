// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.Serialization;
using System.Text.Json;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.Host.Test
{
    /// <summary>
    /// Set of tests to prove that json serialization/deserialization works for Json.NET and for System.Text.Json
    /// </summary>
    public class JsonSerializationTests
    {
        [Fact]
        public void TestAzureBlobStorageLogPublicConfigurationSerialization()
        {
            var input = new AzureBlobStorageLogPublicConfiguration() {SecretName = "secretName", WriteMaxBatchSize = 1};

            var json = JsonSerializer.Serialize(input);
            var deserialized = JsonSerializer.Deserialize<AzureBlobStorageLogPublicConfiguration>(json);

            deserialized.SecretName.Should().Be(input.SecretName);
            deserialized.WriteMaxBatchSize.Should().Be(input.WriteMaxBatchSize);
        }

        [Fact]
        public void TestLocalCASSettings()
        {
            var input = LocalCasSettings.Default();
            var options = new JsonSerializerOptions() {WriteIndented = true};
            var json = JsonSerializer.Serialize(input, options);

            json.Should().Contain("CasClientSettings");
            json.Should().Contain("CacheSettings");
        }

        [Fact]
        public void TestStringConvertibleSettings()
        {
            var serialized = @"
{
    'Mode': 'B',
    'TimeThreshold': '3d5h10m42s',
    'Bytes': '6pb7t2gb30mb4kb5b',
    'DoP': '0.512x',
    'DoP2': '136t',
}"
                .Replace('\'', '"'); ;

            var deserialized = DeploymentUtilities.JsonDeserialize<TestConfig>(serialized);
            var deserializedWithNulls = DeploymentUtilities.JsonDeserialize<TestConfigWithNulls>(serialized);

            Assert.Equal(TestEnum.B, deserialized.Mode.Value);

            var expectedTimeThreshold = TimeSpan.FromDays(3) + TimeSpan.FromHours(5) + TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(42);
            long expectedBytes =
                6 * ByteSizeSetting.Petabytes +
                7 * ByteSizeSetting.Terabytes +
                2 * ByteSizeSetting.Gigabytes +
                30 * ByteSizeSetting.Megabytes +
                4 * ByteSizeSetting.Kilobytes +
                5;

            Assert.Equal(expectedTimeThreshold, deserialized.TimeThreshold.Value);
            Assert.Equal(expectedBytes, deserialized.Bytes.Value);

            Assert.Equal(TestEnum.B, deserializedWithNulls.Mode.Value.Value);
            Assert.Equal(expectedTimeThreshold, deserializedWithNulls.TimeThreshold.Value.Value);
            Assert.Equal(expectedBytes, deserializedWithNulls.Bytes.Value.Value);

            Assert.Equal((decimal)0.512, (decimal)deserialized.DoP.ProcessorCountMultiplier.Value, 3);
            Assert.Equal(136, deserialized.DoP2.ThreadCount);
        }

        public enum TestEnum
        {
            A,
            B,
        }

        public class TestConfig
        {
            [DataMember]
            public EnumSetting<TestEnum> Mode { get; set; } = TestEnum.A;

            [DataMember]
            public TimeSpanSetting TimeThreshold { get; set; }

            [DataMember]
            public ByteSizeSetting Bytes { get; set; }

            [DataMember]
            public DegreeOfParallelism DoP { get; set; }

            [DataMember]
            public DegreeOfParallelism DoP2 { get; set; }
        }

        public class TestConfigWithNulls
        {
            [DataMember]
            public EnumSetting<TestEnum>? Mode { get; set; } = TestEnum.A;

            [DataMember]
            public TimeSpanSetting? TimeThreshold { get; set; }

            [DataMember]
            public ByteSizeSetting? Bytes { get; set; }
        }
    }
}
