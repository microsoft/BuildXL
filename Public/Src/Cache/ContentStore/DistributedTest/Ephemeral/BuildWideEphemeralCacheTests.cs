// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Distributed.Redis;
using FluentAssertions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Ephemeral;

[TestClassIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
public class BuildWideEphemeralCacheTests : EphemeralCacheTestsBase
{
    protected override Mode TestMode => Mode.BuildWide;

    public BuildWideEphemeralCacheTests(LocalRedisFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }

    [Fact]
    public Task MachinesFallbackToStorageWhenCopyingOutsideTheRing()
    {
        return RunTestAsync(
            async (context, silentContext, host) =>
            {
                var r1 = host.Ring(0);
                var r1l = host.Instance(r1.Leader);
                var r1w = host.Instance(r1.Builders[1]);

                var r2 = host.Ring(1);
                var r2l = host.Instance(r2.Leader);
                var r2w = host.Instance(r2.Builders[1]);

                var putResult = await r1w.Session!.PutRandomAsync(context, HashType.Vso0, provideHash: true, size: 100, context.Token)
                    .ThrowIfFailureAsync();
                putResult.ShouldBeSuccess();

                var placeResult = await r2w.Session!.PlaceFileAsync(
                    context,
                    putResult.ContentHash,
                    host.TestDirectory.CreateRandomFileName(),
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Any,
                    context.Token);
                placeResult.ShouldBeSuccess();
                placeResult.MaterializationSource.Should().Be(PlaceFileResult.Source.BackingStore);
            }, numRings: 2, instancesPerRing: 2);
    }
}
