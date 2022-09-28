// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using BuildXL.Cache.Host.Configuration;
using Newtonsoft.Json;
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
                Mode = TestEnum.B,
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

        public enum TestEnum
        {
            A,
            B,
        }

        public class TestConfig
        {
            public EnumSetting<TestEnum> Mode { get; set; }

            public TimeSpanSetting TimeThreshold { get; set; }
        }

        public class TestConfigWithNulls
        {
            public EnumSetting<TestEnum>? Mode { get; set; }

            public TimeSpanSetting? TimeThreshold { get; set; }
        }
    }
}
