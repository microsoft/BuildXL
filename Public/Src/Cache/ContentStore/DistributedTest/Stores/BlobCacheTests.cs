// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.Blob;
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
        // These tests assert that the hashes don't change across processes

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
    public void BlobContainerNameTests()
    {
        var container = new BlobCacheContainerName(BlobCacheVersion.V0, BlobCacheContainerPurpose.Content, "universe123", "namespace123");
        container.ContainerName.Should().BeEquivalentTo("contentv0uuniverse123-namespace123");
        var parsed = BlobCacheContainerName.Parse(container.ContainerName);
        parsed.Should().BeEquivalentTo(container);

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new BlobCacheContainerName(BlobCacheVersion.V0, BlobCacheContainerPurpose.Metadata, "UPPERCASE", "namespace");
            });

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new BlobCacheContainerName(BlobCacheVersion.V0, BlobCacheContainerPurpose.Metadata, "good", "Bad");
            });

        Assert.Throws<FormatException>(
            () =>
            {
                var _ = new BlobCacheContainerName(BlobCacheVersion.V0, BlobCacheContainerPurpose.Metadata, "waaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaayyyyyy", "tooooooooooooooooloopooooooooooooooooooooooooooooooooooooooooooooooooooong");
            });
    }
}
