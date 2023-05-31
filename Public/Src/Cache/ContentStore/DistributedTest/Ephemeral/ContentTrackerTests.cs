// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Ephemeral;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStoreTest.Test;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Ephemeral;

public class ContentTrackerTests
{
    public Task RunTest(Func<OperationContext, IContentTracker, Task> runTest)
    {
        var context = new OperationContext(new Context(TestGlobal.Logger));
        var contentTracker = new ContentTracker();
        return runTest(context, contentTracker);
    }

    [Fact]
    public Task SimpleUpdateTest()
    {
        return RunTest(
            async (context, contentTracker) =>
            {
                var m1 = new MachineId(1);
                var m2 = new MachineId(2);
                var m3 = new MachineId(3);

                var r1 = new UpdateLocationsRequest
                         {
                             Entries = new List<ContentEntry>
                                       {
                                           new()
                                           {
                                               Hash = ContentHash.Random(),
                                               Size = 100,
                                               Operations = new List<Stamped<MachineId>>
                                                            {
                                                                new(
                                                                    ChangeStamp.Create(
                                                                        new SequenceNumber(1),
                                                                        DateTime.UtcNow,
                                                                        ChangeStampOperation.Add),
                                                                    m1),
                                                                new(
                                                                    ChangeStamp.Create(
                                                                        new SequenceNumber(1),
                                                                        DateTime.UtcNow,
                                                                        ChangeStampOperation.Add),
                                                                    m2)
                                                            }
                                           }
                                       }
                         };

                var r2 = new UpdateLocationsRequest()
                         {
                             Entries = new []
                                       {
                                           new ContentEntry()
                                           {
                                               Hash = r1.Entries[0].Hash,
                                               Size = 100,
                                               Operations = new List<Stamped<MachineId>>
                                                            {
                                                                new(ChangeStamp.Create(new SequenceNumber(2), DateTime.UtcNow, ChangeStampOperation.Delete), m1),
                                                                new(ChangeStamp.Create(new SequenceNumber(2), DateTime.UtcNow, ChangeStampOperation.Add), m3)
                                                            }
                                           }
                                       }
                         };

                await contentTracker.UpdateLocationsAsync(context,  r1 ).ThrowIfFailureAsync();

                var sequenceNumber1 = contentTracker.GetSequenceNumber(r1.Entries[0].Hash, m1);
                Assert.Equal<uint>(1, sequenceNumber1);

                var locations = (await contentTracker.GetLocationsAsync(context, new GetLocationsRequest() { Hashes = new List<ShortHash> { r1.Entries[0].Hash } }).ThrowIfFailureAsync()).Results.First();
                Assert.True(locations.Contains(m1) && !locations.Tombstone(m1));
                Assert.True(locations.Contains(m2) && !locations.Tombstone(m2));
                Assert.True(!locations.Contains(m3) && !locations.Tombstone(m3));

                await contentTracker.UpdateLocationsAsync(context, r2).ThrowIfFailureAsync();

                var sequenceNumber2 = contentTracker.GetSequenceNumber(r2.Entries[0].Hash, m1);
                Assert.Equal<uint>(2, sequenceNumber2);

                locations = (await contentTracker.GetLocationsAsync(context, new GetLocationsRequest() { Hashes = new List<ShortHash> { r1.Entries[0].Hash } }).ThrowIfFailureAsync()).Results.First();
                Assert.True(!locations.Contains(m1) && locations.Tombstone(m1));
                Assert.True(locations.Contains(m2) && !locations.Tombstone(m2));
                Assert.True(locations.Contains(m3) && !locations.Tombstone(m3));
            });
    }
}
