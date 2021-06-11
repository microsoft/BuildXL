// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using Xunit;
using BuildXL.Cache.Host;
using BuildXL.Cache.Host.Configuration;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using Newtonsoft.Json.Linq;
using System;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Tracing
{
    public class StringConvertibleSettingTests
    {
        [Fact]
        public void TestStringConvertibleSettings()
        {
            var config = new TestConfig()
            {
                Mode = ContentMetadataStoreMode.WriteBothPreferDistributed,
            };

            var serialized = JsonConvert.SerializeObject(config);
            var jObject = JsonConvert.DeserializeObject<JObject>(serialized);

            jObject[nameof(TestConfig.TimeThreshold)] = "3d5h10m42s";

            var deserialized = JsonConvert.DeserializeObject<TestConfig>(jObject.ToString());
            var deserializedWithNulls = JsonConvert.DeserializeObject<TestConfigWithNulls>(jObject.ToString());

            Assert.Equal(config.Mode.Value, deserialized.Mode.Value);

            var expectedTimeThreshold = TimeSpan.FromDays(3) + TimeSpan.FromHours(5) + TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(42);
            Assert.Equal(expectedTimeThreshold, deserialized.TimeThreshold.Value);

            Assert.Equal(config.Mode.Value, deserializedWithNulls.Mode.Value.Value);
            Assert.Equal(expectedTimeThreshold, deserializedWithNulls.TimeThreshold.Value.Value);
        }

        public class TestConfig
        {
            public EnumSetting<ContentMetadataStoreMode> Mode { get; set; } = ContentMetadataStoreMode.Redis;

            public TimeSpanSetting TimeThreshold { get; set; }
        }

        public class TestConfigWithNulls
        {
            public EnumSetting<ContentMetadataStoreMode>? Mode { get; set; } = ContentMetadataStoreMode.Redis;

            public TimeSpanSetting? TimeThreshold { get; set; }
        }
    }
}
