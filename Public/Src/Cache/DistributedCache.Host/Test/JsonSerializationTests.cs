// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using BuildXL.Cache.Host.Configuration;
using FluentAssertions;
using System.Text.Json;
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

    }
}
