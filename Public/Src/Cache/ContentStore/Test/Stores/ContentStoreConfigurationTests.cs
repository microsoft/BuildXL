// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Runtime.Serialization;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Stores
{
    public class ContentStoreConfigurationTests
    {
        private const long GB = 1024 * 1024 * 1024;

        [Fact]
        public void JsonMissingQuotasAreNull()
        {
            const string json = @"{}";
            using (var stream = json.ToUTF8Stream())
            {
                var config = stream.DeserializeFromJSON<ContentStoreConfiguration>();
                config.MaxSizeQuota.Should().BeNull();
                config.DiskFreePercentQuota.Should().NotBeNull();
            }
        }

        [Fact]
        public void JsonEmptyQuotasAreInvalid()
        {
            const string json = @"{""MaxSizeQuota"":{}, ""DiskFreePercentQuota"":{}}";
            using (var stream = json.ToUTF8Stream())
            {
                var config = stream.DeserializeFromJSON<ContentStoreConfiguration>();
                config.IsValid.Should().BeFalse();
                config.MaxSizeQuota.IsValid.Should().BeFalse();
                config.DiskFreePercentQuota.IsValid.Should().BeFalse();
            }
        }

        [Fact]
        public void JsonSpecifiedQuotas()
        {
            const string json = @"{
""MaxSizeQuota"":{""Hard"":""100"", ""Soft"":""90""},
""DiskFreePercentQuota"":{""Hard"":""10"", ""Soft"":""20""}
}";
            using (var stream = json.ToUTF8Stream())
            {
                var config = stream.DeserializeFromJSON<ContentStoreConfiguration>();
                config.IsValid.Should().BeTrue();
                config.MaxSizeQuota.IsValid.Should().BeTrue();
                config.MaxSizeQuota.Hard.Should().Be(100);
                config.MaxSizeQuota.Soft.Should().Be(90);
                config.MaxSizeQuota.Target.Should().Be(89);
                config.DiskFreePercentQuota.IsValid.Should().BeTrue();
                config.DiskFreePercentQuota.Hard.Should().Be(10);
                config.DiskFreePercentQuota.Soft.Should().Be(20);
                config.DiskFreePercentQuota.Target.Should().Be(21);
            }
        }

        [Fact]
        public void JsonMaxSizeInvalid()
        {
            const string json = @"{""MaxSizeQuota"":{""Hard"":""abc"", ""Soft"":""50""}}";
            using (var stream = json.ToUTF8Stream())
            {
                var config = stream.DeserializeFromJSON<ContentStoreConfiguration>();
                config.IsValid.Should().BeFalse();
                config.Error.Should().NotBeNullOrEmpty();
            }
        }

        [Fact]
        public void JsonDiskFreePercentInvalid()
        {
            const string json = @"{""DiskFreePercentQuota"":{""Hard"":""xyz"", ""Soft"":""9""}}";
            using (var stream = json.ToUTF8Stream())
            {
                var config = stream.DeserializeFromJSON<ContentStoreConfiguration>();
                config.IsValid.Should().BeFalse();
                config.Error.Should().NotBeNullOrEmpty();
            }
        }

        [Fact]
        public void NullQuotasYieldsDefault()
        {
            var configuration = new ContentStoreConfiguration();
            configuration.IsValid.Should().BeTrue();
            configuration.MaxSizeQuota.Should().BeNull();
            configuration.DiskFreePercentQuota.Should().NotBeNull();
            configuration.DiskFreePercentQuota.Hard.Should().Be(10);
            configuration.DiskFreePercentQuota.Soft.Should().Be(20);
            configuration.DiskFreePercentQuota.Target.Should().Be(21);
        }

        [Fact]
        public void NullQuotaExpressionsYieldsDefault()
        {
            var configuration = new ContentStoreConfiguration(string.Empty, null);
            configuration.IsValid.Should().BeTrue();
            configuration.MaxSizeQuota.Should().BeNull();
            configuration.DiskFreePercentQuota.Should().NotBeNull();
            configuration.DiskFreePercentQuota.Hard.Should().Be(10);
            configuration.DiskFreePercentQuota.Soft.Should().Be(20);
            configuration.DiskFreePercentQuota.Target.Should().Be(21);
        }

        [Fact]
        public void ConstructWithValidQuotaExpressionsSucceeds()
        {
            var configuration = new ContentStoreConfiguration("3GB:2GB", "12:14");
            configuration.MaxSizeQuota.Hard.Should().Be(3 * GB);
            configuration.MaxSizeQuota.Soft.Should().Be(2 * GB);
            configuration.DiskFreePercentQuota.Hard.Should().Be(12);
            configuration.DiskFreePercentQuota.Soft.Should().Be(14);
        }

        [Fact]
        public void MaxSizeFieldsSet()
        {
            var configuration = CreateMaxSizeOnlyConfiguration("2GB", "1GB");
            configuration.MaxSizeQuota.Hard.Should().Be(2 * GB);
            configuration.MaxSizeQuota.Soft.Should().Be(1 * GB);
            configuration.MaxSizeQuota.Target.Should().Be((1 * GB) - (GB / 10));
        }

        [Theory]
        [InlineData("2", "1")]
        [InlineData("200B", "100B")]
        [InlineData("2KB", "1KB")]
        [InlineData("2MB", "1MB")]
        [InlineData("2GB", "1GB")]
        [InlineData("2TB", "1TB")]
        public void MaxSizeValid(string hard, string soft)
        {
            var configuration = CreateMaxSizeOnlyConfiguration(hard, soft);
            configuration.MaxSizeQuota.Should().NotBeNull();
        }

        [Fact]
        public void MaxSizeSoftDefaultsWhenNotSpecified()
        {
            var configuration = CreateMaxSizeOnlyConfiguration("100");
            configuration.MaxSizeQuota.Soft.Should().Be(90);
        }

        [Fact]
        public void MaxSizeMissingHard()
        {
            VerifyMaxSizeThrows(null, "1GB", "must be provided");
        }

        [Fact]
        public void MaxSizeLimitsReversed()
        {
            VerifyMaxSizeThrows("1GB", "2GB", "must be <");
        }

        [Fact]
        public void DiskFreePercentFieldsSet()
        {
            var configuration = CreateDiskFreePercentOnlyConfiguration("10", "30");
            configuration.DiskFreePercentQuota.Hard.Should().Be(10);
            configuration.DiskFreePercentQuota.Soft.Should().Be(30);
            configuration.DiskFreePercentQuota.Target.Should().Be(32);
        }

        [Theory]
        [InlineData("5", "10")]
        [InlineData("97", "98")]
        [InlineData("1", "2")]
        public void DiskFreePercentValid(string hard, string soft)
        {
            var configuration = CreateDiskFreePercentOnlyConfiguration(hard, soft);
            configuration.DiskFreePercentQuota.Should().NotBeNull();
        }

        [Fact]
        public void DiskFreePercentSoftDefaultsWhenNotSpecified()
        {
            var configuration = CreateDiskFreePercentOnlyConfiguration("5");
            configuration.DiskFreePercentQuota.Soft.Should().Be(15);
        }

        [Fact]
        public void DiskFreePercentMissingHard()
        {
            VerifyDiskFreePercentThrows(null, "5", "must be provided");
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("a", "5")]
        [InlineData("5", "c")]
        public void DiskFreePercentInvalidLimit(string soft, string hard)
        {
            VerifyDiskFreePercentThrows(hard, soft, "cannot be parsed as a positive number");
        }

        [Theory]
        [InlineData("10", "-1")]
        [InlineData("-1", "10")]
        [InlineData("10", "101")]
        [InlineData("101", "10")]
        public void DiskFreePercentOutOfRange(string hard, string soft)
        {
            VerifyDiskFreePercentThrows(hard, soft, "out of range");
        }

        [Fact]
        public void DiskFreePercentageLimitsReversed()
        {
            VerifyDiskFreePercentThrows("90", "80", "must be >");
        }

        [Fact]
        public void DenyWriteAttributesOnContentSettingMissingFromJsonSetsDefault()
        {
            const string json = @"{}";
            using (var stream = json.ToUTF8Stream())
            {
                var config = stream.DeserializeFromJSON<ContentStoreConfiguration>();
                config.DenyWriteAttributesOnContent.Should().Be(DenyWriteAttributesOnContentSetting.Disable);
            }
        }

        [Fact]
        public void DenyWriteAttributesOnContentSettingInvalidJson()
        {
            var json = @"{""DenyWriteAttributesOnContent"":""Invalid""}";
            using (var stream = json.ToUTF8Stream())
            {
                var config = stream.DeserializeFromJSON<ContentStoreConfiguration>();
                config.IsValid.Should().BeFalse();
            }
        }

        [Theory]
        [InlineData(DenyWriteAttributesOnContentSetting.Enable)]
        [InlineData(DenyWriteAttributesOnContentSetting.Disable)]
        public void DenyWriteAttributesOnContentSettingFromJsonSucceeds(DenyWriteAttributesOnContentSetting value)
        {
            var json = @"{""DenyWriteAttributesOnContent"":""" + value + @"""}";
            using (var stream = json.ToUTF8Stream())
            {
                var config = stream.DeserializeFromJSON<ContentStoreConfiguration>();
                config.DenyWriteAttributesOnContent.Should().Be(value);
            }
        }

        [Theory]
        [InlineData(DenyWriteAttributesOnContentSetting.Enable)]
        [InlineData(DenyWriteAttributesOnContentSetting.Disable)]
        public void DenyWriteAttributesOnContentSettingRoundtrip(DenyWriteAttributesOnContentSetting value)
        {
            var configuration1 = new ContentStoreConfiguration(denyWriteAttributesOnContent: value);
            using (var ms = new MemoryStream())
            {
                configuration1.SerializeToJSON(ms);
                ms.Position = 0;
                var configuration2 = ms.DeserializeFromJSON<ContentStoreConfiguration>();
                configuration2.DenyWriteAttributesOnContent.Should().Be(value);
            }
        }

        [Fact]
        public void SingleInstanceTimeoutSecondsMissingFromJsonSetsDefault()
        {
            const string json = @"{}";
            using (var stream = json.ToUTF8Stream())
            {
                var config = stream.DeserializeFromJSON<ContentStoreConfiguration>();
                config.SingleInstanceTimeoutSeconds.Should().Be(ContentStoreConfiguration.DefaultSingleInstanceTimeoutSeconds);
            }
        }

        [Fact]
        public void SingleInstanceTimeoutSecondsInvalidJson()
        {
            var json = @"{""SingleInstanceTimeoutSeconds"":""Invalid""}";
            using (var stream = json.ToUTF8Stream())
            {
                Action a = () => stream.DeserializeFromJSON<ContentStoreConfiguration>();
                a.Should().Throw<SerializationException>().Where(e => e.Message.Contains("cannot be parsed as the type 'Int32'"));
            }
        }

        [Fact]
        public void SingleInstanceTimeoutSecondsWithJsonNegativeValue()
        {
            var json = @"{""SingleInstanceTimeoutSeconds"":-1}";
            using (var stream = json.ToUTF8Stream())
            {
                var config = stream.DeserializeFromJSON<ContentStoreConfiguration>();
                config.IsValid.Should().BeFalse();
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(10000)]
        [InlineData(100000)]
        [InlineData(1000000)]
        [InlineData(int.MaxValue)]
        public void SingleInstanceTimeoutSecondsWithJsonSucceeds(int value)
        {
            var json = @"{""SingleInstanceTimeoutSeconds"":""" + value + @"""}";
            using (var stream = json.ToUTF8Stream())
            {
                var config = stream.DeserializeFromJSON<ContentStoreConfiguration>();
                config.SingleInstanceTimeoutSeconds.Should().Be(value);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(int.MaxValue)]
        public void SingleInstanceTimeoutSecondsRoundtrip(int value)
        {
            var configuration1 = new ContentStoreConfiguration(singleInstanceTimeoutSeconds: value);
            using (var ms = new MemoryStream())
            {
                configuration1.SerializeToJSON(ms);
                ms.Position = 0;
                var configuration2 = ms.DeserializeFromJSON<ContentStoreConfiguration>();
                configuration2.SingleInstanceTimeoutSeconds.Should().Be(value);
            }
        }

        [Fact]
        public void ToStringGivesExpected()
        {
            var configuration = new ContentStoreConfiguration
            (
                new MaxSizeQuota("2GB", "1GB"),
                new DiskFreePercentQuota("5", "10")
            );
            configuration.ToString().Should().Be(
                "MaxSizeQuota=[Hard=[2147483648], Soft=[1073741824], Target=[966367642]], DiskFreePercentQuota=[Hard=[5], Soft=[10], Target=[11]]" +
                ", DenyWriteAttributesOnContent=Disable" +
                ", SingleInstanceTimeoutSeconds=1800");
        }

        private static ContentStoreConfiguration CreateMaxSizeOnlyConfiguration(string hard, string soft = null)
        {
            return new ContentStoreConfiguration(new MaxSizeQuota(hard, soft));
        }

        private static void VerifyMaxSizeThrows(string hard, string soft, string messageFragment)
        {
            ContentStoreConfiguration configuration = null;
            Action a = () => configuration = new ContentStoreConfiguration(new MaxSizeQuota(hard, soft));
            a.Should().Throw<CacheException>().Where(e => e.Message.Contains(messageFragment));
            configuration.Should().BeNull();
        }

        private static ContentStoreConfiguration CreateDiskFreePercentOnlyConfiguration(string hard, string soft = null)
        {
            return new ContentStoreConfiguration(null, new DiskFreePercentQuota(hard, soft));
        }

        private static void VerifyDiskFreePercentThrows(string hard, string soft, string messageFragment)
        {
            ContentStoreConfiguration configuration = null;
            Action a = () => configuration = new ContentStoreConfiguration(null, new DiskFreePercentQuota(hard, soft));
            a.Should().Throw<CacheException>().Where(e => e.Message.Contains(messageFragment));
            configuration.Should().BeNull();
        }
    }
}
