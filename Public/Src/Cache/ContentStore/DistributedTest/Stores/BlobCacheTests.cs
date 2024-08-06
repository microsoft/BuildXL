// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Stores;

public class BlobCacheTests : TestWithOutput
{
    public BlobCacheTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Fact]
    public void VersionStartsWithV()
    {
        // WARNING: this is related to serialization formats. DO NOT IGNORE THIS TEST FAILING.
        foreach (var value in Enum.GetValues(typeof(BlobCacheVersion)).Cast<BlobCacheVersion>())
        {
            var serialized = value.ToString();
            serialized.Should().StartWith("V", $"This is an assumption made in {nameof(BlobCacheContainerName)}");
            serialized.Length.Should().BeLessOrEqualTo(BlobCacheContainerName.VersionReservedLength, $"This is an assumption made in {nameof(BlobCacheContainerName)}");
        }
    }

    [Fact]
    public void PurposeLengthIsAtMost10()
    {
        // WARNING: this is related to serialization formats. DO NOT IGNORE THIS TEST FAILING.
        foreach (var value in Enum.GetValues(typeof(BlobCacheContainerPurpose)).Cast<BlobCacheContainerPurpose>())
        {
            var serialized = value.ToString();
            serialized.Length.Should().BeLessOrEqualTo(BlobCacheContainerName.PurposeReservedLength, $"This is an assumption made in {nameof(BlobCacheContainerName)}");
        }
    }

    [Fact]
    public void BlobCacheShardingKeyTests()
    {
        var ch = ContentHash.Random();

        // Should be equal after rehashing
        var k1 = BlobCacheShardingKey.FromContentHash(ch);
        var k2 = BlobCacheShardingKey.FromContentHash(ch);
        k1.Should().BeEquivalentTo(k2);
        // Purpose should be correct
        k1.Purpose.Should().BeEquivalentTo(BlobCacheContainerPurpose.Content);

        var sh = new ShortHash(ch);
        var k5 = BlobCacheShardingKey.FromShortHash(sh);
        var k6 = BlobCacheShardingKey.FromShortHash(sh);
        k5.Should().BeEquivalentTo(k1);
        k5.Should().BeEquivalentTo(k6);
        // Purpose should be correct
        k5.Purpose.Should().BeEquivalentTo(BlobCacheContainerPurpose.Content);

        var wf = Fingerprint.Random();
        var k3 = BlobCacheShardingKey.FromWeakFingerprint(wf);
        var k4 = BlobCacheShardingKey.FromWeakFingerprint(wf);
        k3.Should().BeEquivalentTo(k4);
        k3.Purpose.Should().BeEquivalentTo(BlobCacheContainerPurpose.Metadata);
    }

    [Fact]
    public void BlobCacheShardingKeyStabilityTests()
    {
        // These tests assert that the hashes don't change across processes.
        // WARNING: IF THIS TEST FAILS IT MEANS YOU HAVE ROYALLY SCREWED UP THE HASHING CODE. DO NOT IGNORE IT.

        // Chosen by fair dice roll. Guaranteed to be random.
        var bytes = new byte[] { 0x21, 0xfc, 0x81, 0x32, 0xdd, 0xfd, 0x24, 0x24, 0x0f, 0xbc, 0xb9, 0xdc, 0xfe, 0x7b, 0x85, 0xd3,
0x26, 0xe6, 0x0e, 0xac, 0xfb, 0xe8, 0xe6, 0xcd, 0x94, 0xf8, 0xe5, 0x66, 0xf5, 0xdf, 0xa0, 0x60,
0xb0 };
        var ch = new ContentHash(HashType.Vso0, bytes);

        var k1 = BlobCacheShardingKey.FromContentHash(ch);
        k1.Key.Should().Be(-1294787154);

        var wf = new Fingerprint(bytes, bytes.Length);
        var k2 = BlobCacheShardingKey.FromWeakFingerprint(wf);
        k2.Key.Should().Be(-138440390);
    }

    [Fact]
    public void BlobCacheAccountNameTests()
    {
        var unique = ThreadSafeRandom.LowercaseAlphanumeric(10);
        var expected = $"{unique}00001test";
        var account = new BlobCacheStorageShardingAccountName(unique, 1, "test");
        account.AccountName.Should().BeEquivalentTo(expected);
        var parsed = BlobCacheStorageShardingAccountName.Parse(account.AccountName);
        parsed.Should().BeEquivalentTo(account);

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new BlobCacheStorageShardingAccountName("short", 1, "test");
            });

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new BlobCacheStorageShardingAccountName("waaaaaaaaaaaaaayytoolong", 1, "test");
            });

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new BlobCacheStorageShardingAccountName(unique, 100000, "test");
            });

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new BlobCacheStorageShardingAccountName(unique, -1, "test");
            });

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new BlobCacheStorageShardingAccountName(unique, 0, "waaaaaaaaaaaaaayytoolong");
            });

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new BlobCacheStorageShardingAccountName("MUSTbelower", 0, "test");
            });

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new BlobCacheStorageShardingAccountName(unique, 0, "TEST");
            });
    }

    [Fact]
    [Trait("DisableFailFast", "true")]
    public void LegacyBlobContainerNameTests()
    {
        var container = new LegacyBlobCacheContainerName(BlobCacheContainerPurpose.Content, "default", "universe123", "namespace123");
        container.ContainerName.Should().BeEquivalentTo("contentv0-default-universe123-namespace123");
        var parsed = LegacyBlobCacheContainerName.Parse(container.ContainerName);
        parsed.Should().BeEquivalentTo(container);

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new LegacyBlobCacheContainerName(BlobCacheContainerPurpose.Metadata, "default", "UPPERCASE", "namespace");
            });

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new LegacyBlobCacheContainerName(BlobCacheContainerPurpose.Metadata, "default", "good", "Bad");
            });

        Assert.Throws<ContractException>(
            () =>
            {
                var _ = new LegacyBlobCacheContainerName(BlobCacheContainerPurpose.Metadata, "default", "waaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaayyyyyy", "tooooooooooooooooloopooooooooooooooooooooooooooooooooooooooooooooooooooong");
            });

        Assert.Throws<ContractException>(
            () =>
            {
                var _ = new LegacyBlobCacheContainerName(BlobCacheContainerPurpose.Metadata, "defaultooooo", "wupp", "wopp");
            });
    }

    [Fact]
    public void AdoBuildCacheBlobContainerNameTests()
    {
        var container = new FixedCacheBlobContainerName("arbitrary-content", BlobCacheContainerPurpose.Content);

        container.Universe.Should().Be("default");
        container.Namespace.Should().Be("default");
        container.Matrix.Should().Be("default");
        container.ContainerName.Should().BeEquivalentTo("arbitrary-content");

        var container2 = new FixedCacheBlobContainerName("arbitrary-metadata", BlobCacheContainerPurpose.Metadata);

        container2.Universe.Should().Be("default");
        container2.Namespace.Should().Be("default");
        container2.Matrix.Should().Be("default");
        container2.ContainerName.Should().BeEquivalentTo("arbitrary-metadata");
    }

    [Fact]
    public void SimpleMatrixTest()
    {
        var account = "malystgacctfortesting";
        var universe = "playground";
        var @namespace = "default";

        var blobCacheStorageAccountName = BlobCacheStorageAccountName.Parse(account);

        var scheme = new ShardingScheme(
            ShardingAlgorithm.SingleShard,
            new List<BlobCacheStorageAccountName>() { blobCacheStorageAccountName });
        var matrix = scheme.GenerateMatrix();
        var naming = new LegacyContainerNamingScheme(scheme, universe, @namespace);
        var containerSelector = naming.GenerateContainerNameMapping();

        matrix.Content.Should().Be("4752270493");
        matrix.Metadata.Should().Be("4752270493");
        containerSelector[blobCacheStorageAccountName].Length.Should().Be(3);

        containerSelector[blobCacheStorageAccountName][0].ContainerName.Should().BeEquivalentTo("contentv0-4752270493-playground-default");
        containerSelector[blobCacheStorageAccountName][1].ContainerName.Should().BeEquivalentTo("metadatav0-4752270493-playground-default");
        containerSelector[blobCacheStorageAccountName][2].ContainerName.Should().BeEquivalentTo("checkpointv0-checkpoint-playground-default");
    }

    [Theory]
    [InlineData("https://dhtqcftmrg00000test.blob.core.windows.net/", "dhtqcftmrg00000test")]
    [InlineData("https://dhtqcftmrg00000test.z3221.blob.core.windows.net/", "dhtqcftmrg00000test")]
    [InlineData("http://127.0.0.1:1099/dhtqcftmrg00000test", "dhtqcftmrg00000test")]
    [InlineData("https://skbf6flucp00000blobl3.z25.blob.storage.azure.net/", "skbf6flucp00000blobl3")]
    public void CanParseUrls(string uri, string accountName)
    {
        AzureStorageUtilities.GetAccountName(new Uri(uri)).Should().Be(accountName);
    }

    [Fact]
    public void BuildCacheContainerNamingTest()
    {
        var account = new Uri("https://foo.blob.core.windows.net/");

        var blobCacheStorageAccountName = BlobCacheStorageAccountName.Parse(AzureStorageUtilities.GetAccountName(account));

        var scheme = new ShardingScheme(
            ShardingAlgorithm.SingleShard,
            new List<BlobCacheStorageAccountName>() { blobCacheStorageAccountName });

        var content = new BuildCacheContainer() { Name = "content", Signature = "?this=is&some=signature", Type = BuildCacheContainerType.Content };
        var metadata = new BuildCacheContainer() { Name = "metadata", Signature = "?this=is&some=signature", Type = BuildCacheContainerType.Metadata };
        var checkpoint = new BuildCacheContainer() { Name = "checkpoint", Signature = "?this=is&some=signature", Type = BuildCacheContainerType.Checkpoint };

        var shard = new BuildCacheShard() { StorageUrl = account, Containers = new List<BuildCacheContainer> { content, metadata, checkpoint } };
        BuildCacheConfiguration buildCacheConfiguration = new BuildCacheConfiguration() { Name = "MyCache", RetentionDays = 5, Shards = new List<BuildCacheShard> { shard } };

        var naming = new BuildCacheContainerNamingScheme(buildCacheConfiguration);
        var containerSelector = naming.GenerateContainerNameMapping();

        containerSelector[blobCacheStorageAccountName].Length.Should().Be(3);
        containerSelector[blobCacheStorageAccountName][0].ContainerName.Should().BeEquivalentTo("content");
        containerSelector[blobCacheStorageAccountName][1].ContainerName.Should().BeEquivalentTo("metadata");
        containerSelector[blobCacheStorageAccountName][2].ContainerName.Should().BeEquivalentTo("checkpoint");
    }

    [Fact]
    public void BlobCacheContructionCanHandleDictionarySecret()
    {
        var secrets = new Dictionary<string, string>
                      {
                          { "somename", "someSecret" },
                          { "somename2", "someSecret2" }
                      };


        var secretString = JsonUtilities.JsonSerialize(secrets);

        // As long as this doesn't throw, this is a success.
        BlobCacheCredentialsHelper.ParseFromFileFormat(secretString);
    }

    [Fact]
    public void BlobCacheContructionCanHandleConnectionStringSecret()
    {
        var secrets = new List<string>
                      {
                          { "someSecret1;https://accountName.domain.blob.storage.azure.net/" },
                          { "someSecret2;https://accountName2.domain2.blob.storage.azure.net/" },
                      };

        var secretString = JsonUtilities.JsonSerialize(secrets);

        // As long as this doesn't throw, this is a success.
        BlobCacheCredentialsHelper.ParseFromFileFormat(secretString);
    }

}
