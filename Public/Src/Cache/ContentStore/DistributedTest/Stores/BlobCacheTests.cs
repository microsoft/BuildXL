// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
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
    public void BlobContainerNameTests()
    {
        var container = new BlobCacheContainerName(BlobCacheVersion.V0, BlobCacheContainerPurpose.Content, "default", "universe123", "namespace123");
        container.ContainerName.Should().BeEquivalentTo("contentv0-default-universe123-namespace123");
        var parsed = BlobCacheContainerName.Parse(container.ContainerName);
        parsed.Should().BeEquivalentTo(container);

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new BlobCacheContainerName(BlobCacheVersion.V0, BlobCacheContainerPurpose.Metadata, "default", "UPPERCASE", "namespace");
            });

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new BlobCacheContainerName(BlobCacheVersion.V0, BlobCacheContainerPurpose.Metadata, "default", "good", "Bad");
            });

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new BlobCacheContainerName(BlobCacheVersion.V0, BlobCacheContainerPurpose.Metadata, "default", "waaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaayyyyyy", "tooooooooooooooooloopooooooooooooooooooooooooooooooooooooooooooooooooooong");
            });

        Assert.Throws<ContractException>(
            () =>
            {
                var _ = new BlobCacheContainerName(BlobCacheVersion.V0, BlobCacheContainerPurpose.Metadata, "defaultooooo", "wupp", "wopp");
            });
    }

    [Fact]
    public void MalyTest()
    {
        var account = "malystgacctfortesting";
        var universe = "playground";
        var @namespace = "default";

        var scheme = new ShardingScheme(
            ShardingAlgorithm.SingleShard,
            new List<BlobCacheStorageAccountName>() { BlobCacheStorageAccountName.Parse(account) });
        var matrix = ShardedBlobCacheTopology.GenerateMatrix(scheme);
        var containers = ShardedBlobCacheTopology.GenerateContainerNames(universe, @namespace, scheme);
        matrix.Content.Should().Be("4752270493");
        matrix.Metadata.Should().Be("4752270493");
        containers.Length.Should().Be(2);
    }

    [Fact]
    public void BlobCacheContructionCanHandleDictionarySecret()
    {
        var secrets = new Dictionary<string, string>
                      {
                          { "someName", "someSecret" },
                          { "someName2", "someSecret2" }
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
