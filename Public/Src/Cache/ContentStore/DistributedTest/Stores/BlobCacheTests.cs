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

        var wf = Fingerprint.Random();
        var k3 = BlobCacheShardingKey.FromWeakFingerprint(wf);
        var k4 = BlobCacheShardingKey.FromWeakFingerprint(wf);
        k3.Should().BeEquivalentTo(k4);
        k3.Purpose.Should().BeEquivalentTo(BlobCacheContainerPurpose.Metadata);
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
