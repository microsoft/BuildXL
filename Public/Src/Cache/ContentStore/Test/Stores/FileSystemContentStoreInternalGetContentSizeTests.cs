// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Stores
{
    public class FileSystemContentStoreInternalGetContentSizeTests : FileSystemContentStoreInternalTestBase
    {
        private readonly MemoryClock _clock;

        public FileSystemContentStoreInternalGetContentSizeTests()
            : base(() => new MemoryFileSystem(new MemoryClock()), TestGlobal.Logger)
        {
            _clock = (MemoryClock)((MemoryFileSystem)FileSystem).Clock;
        }

        [Fact]
        public Task GetContentSizePins()
        {
            var context = new Context(Logger);
            return TestStore(context, _clock, async store =>
            {
                var putContentSizeInBytes = (long)MaxSizeHard / 3;
                var r1 = await store.PutRandomAsync(context, (int)putContentSizeInBytes);
                _clock.Increment();
                using (var pinContext = store.CreatePinContext())
                {
                    var r2 = await store.GetContentSizeAndCheckPinnedAsync(context, r1.ContentHash, new PinRequest(pinContext));
                    r2.Exists.Should().BeTrue();
                    r2.Size.Should().Be(putContentSizeInBytes);
                    r2.WasPinned.Should().BeFalse();
                    await store.EnsureContentIsPinned(context, _clock, r1.ContentHash);
                    pinContext.Contains(r1.ContentHash).Should().BeTrue();
                }

                await store.EnsureContentIsNotPinned(context, _clock, r1.ContentHash);
            });
        }
    }
}
