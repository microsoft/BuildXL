// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using Xunit;

namespace ContentStoreTest.Stores
{
    public class PinSizeHistoryTests : TestBase
    {
        private readonly ITestClock _clock = new MemoryClock();

        public PinSizeHistoryTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        [Fact]
        public async Task NoHistoryInitially()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var pinSizeHistory = await PinSizeHistory.LoadOrCreateNewAsync(FileSystem, _clock, testDirectory.Path);
                var history = pinSizeHistory.ReadHistory(1);
                AssertEmptyHistory(history.Window);
            }
        }

        [Fact]
        public async Task ReadHistoryWithDifferentSizesOfWindow()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var pinSizeHistory = await PinSizeHistory.LoadOrCreateNewAsync(FileSystem, _clock, testDirectory.Path);
                AddIntoPinSizeHistory(pinSizeHistory, 7, 2, 7, 13, 11);

                var history3 = pinSizeHistory.ReadHistory(3);
                AssertEqualHistory(new long[] {11, 13, 7}, history3.Window);

                var history5 = pinSizeHistory.ReadHistory(5);
                AssertEqualHistory(new long[] {11, 13, 7, 2, 7}, history5.Window);

                var history7 = pinSizeHistory.ReadHistory(7);
                AssertEqualHistory(new long[] {11, 13, 7, 2, 7}, history7.Window);
            }
        }

        [Fact]
        public async Task ReadSmallerHistoryWithDifferentSizesOfWindow()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var pinSizeHistory = await PinSizeHistory.LoadOrCreateNewAsync(FileSystem, _clock, testDirectory.Path, 5);
                AddIntoPinSizeHistory(pinSizeHistory, 1, 1, 1, 7, 2, 7, 13, 11);

                var history3 = pinSizeHistory.ReadHistory(3);
                AssertEqualHistory(new long[] {11, 13, 7}, history3.Window);

                var history5 = pinSizeHistory.ReadHistory(5);
                AssertEqualHistory(new long[] {11, 13, 7, 2, 7}, history5.Window);

                var history7 = pinSizeHistory.ReadHistory(7);
                AssertEqualHistory(new long[] {11, 13, 7, 2, 7}, history7.Window);
            }
        }

        [Fact]
        public async Task SaveAndReadHistoryWithDifferentSizesOfWindow()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var pinSizeHistory = await PinSizeHistory.LoadOrCreateNewAsync(FileSystem, _clock, testDirectory.Path);
                AddIntoPinSizeHistory(pinSizeHistory, 7, 2, 7, 13, 11);

                await pinSizeHistory.SaveAsync(FileSystem);
                pinSizeHistory = await PinSizeHistory.LoadOrCreateNewAsync(FileSystem, _clock, testDirectory.Path);

                var history3 = pinSizeHistory.ReadHistory(3);
                AssertEqualHistory(new long[] {11, 13, 7}, history3.Window);

                var history5 = pinSizeHistory.ReadHistory(5);
                AssertEqualHistory(new long[] {11, 13, 7, 2, 7}, history5.Window);

                var history7 = pinSizeHistory.ReadHistory(7);
                AssertEqualHistory(new long[] {11, 13, 7, 2, 7}, history7.Window);
            }
        }

        [Fact]
        public async Task SaveReadSmallerHistoryWithDifferentSizesOfWindow()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var pinSizeHistory = await PinSizeHistory.LoadOrCreateNewAsync(FileSystem, _clock, testDirectory.Path);
                AddIntoPinSizeHistory(pinSizeHistory, 1, 1, 1, 7, 2, 7, 13, 11);

                await pinSizeHistory.SaveAsync(FileSystem);
                pinSizeHistory = await PinSizeHistory.LoadOrCreateNewAsync(FileSystem, _clock, testDirectory.Path, 5);

                var history3 = pinSizeHistory.ReadHistory(3);
                AssertEqualHistory(new long[] {11, 13, 7}, history3.Window);

                var history5 = pinSizeHistory.ReadHistory(5);
                AssertEqualHistory(new long[] {11, 13, 7, 2, 7}, history5.Window);

                var history7 = pinSizeHistory.ReadHistory(7);
                AssertEqualHistory(new long[] {11, 13, 7, 2, 7}, history7.Window);

                await pinSizeHistory.SaveAsync(FileSystem);
                pinSizeHistory = await PinSizeHistory.LoadOrCreateNewAsync(FileSystem, _clock, testDirectory.Path, 8);

                history3 = pinSizeHistory.ReadHistory(3);
                AssertEqualHistory(new long[] {11, 13, 7}, history3.Window);

                history5 = pinSizeHistory.ReadHistory(5);
                AssertEqualHistory(new long[] {11, 13, 7, 2, 7}, history5.Window);

                history7 = pinSizeHistory.ReadHistory(7);
                AssertEqualHistory(new long[] {11, 13, 7, 2, 7}, history7.Window);
            }
        }

        [Fact]
        public async Task HistoryTimestampIsIncreasingMonotonically()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var pinSizeHistory = await PinSizeHistory.LoadOrCreateNewAsync(FileSystem, _clock, testDirectory.Path);
                var emptyHistory = pinSizeHistory.ReadHistory(1);
                AssertEmptyHistory(emptyHistory.Window);

                AddIntoPinSizeHistory(pinSizeHistory, 7);

                var history1 = pinSizeHistory.ReadHistory(1);
                AssertEqualHistory(new long[] {7}, history1.Window);
                Assert.True(history1.TimestampInTick > emptyHistory.TimestampInTick);

                await pinSizeHistory.SaveAsync(FileSystem);

                pinSizeHistory = await PinSizeHistory.LoadOrCreateNewAsync(FileSystem, _clock, testDirectory.Path);

                var history1AfterSave = pinSizeHistory.ReadHistory(1);
                AssertEqualHistory(new long[] {7}, history1AfterSave.Window);
                Assert.Equal(history1.TimestampInTick, history1AfterSave.TimestampInTick);
            }
        }

        // ReSharper disable once UnusedParameter.Local
        private static void AssertEqualHistory(IReadOnlyList<long> expected, IReadOnlyList<long> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; ++i)
            {
                Assert.Equal(expected[i], actual[i]);
            }
        }

        // ReSharper disable once UnusedParameter.Local
        private static void AssertEmptyHistory(IReadOnlyList<long> actual)
        {
            Assert.Empty(actual);
        }

        private void AddIntoPinSizeHistory(PinSizeHistory pinSizeHistory, params long[] values)
        {
            foreach (var value in values)
            {
                _clock.Increment();
                pinSizeHistory.Add(value);
            }
        }
    }
}
