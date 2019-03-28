// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Synchronization;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Synchronization
{
    public sealed class AsyncReaderWriterLockTests : TestBase
    {
        public AsyncReaderWriterLockTests()
            : base(TestGlobal.Logger)
        {
        }

        // Failed:
        // ContentStoreTest.Synchronization.AsyncReaderWriterLockTests.MultipleReadersFollowedBySingleWriter [FAIL]
        // Assert.Equal() Failure
        //   Expected: 2
        //   Actual:   0
        // \.\CloudStore\src\ContentStore\Test\Synchronization\AsyncReaderWriterLockTests.cs(28,0): at ContentStoreTest.Synchronization.AsyncReaderWriterLockTests.MultipleReadersFollowedBySingleWriter()
        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public void MultipleReadersFollowedBySingleWriter()
        {
            var locker = new AsyncReaderWriterLock();
            var cs = new CriticalSection();
            var read1 = locker.WithReadLockAsync(() => cs.ReadAsync(500));
            var read2 = locker.WithReadLockAsync(() => cs.ReadAsync(500));
            var write1 = locker.WithWriteLockAsync(() => cs.WriteAsync(5));
            Assert.Equal(2, cs.ReaderCount);
            Assert.Equal(0, cs.WriterCount);

            Task.WhenAll(read1, read2, write1).Wait();
        }

        [Fact]
        public void SingleWriterFollowedByMultipleReaders()
        {
            var locker = new AsyncReaderWriterLock();
            var cs = new CriticalSection();
            var write1 = locker.WithWriteLockAsync(() => cs.WriteAsync(500));
            var read1 = locker.WithReadLockAsync(() => cs.ReadAsync(5));
            var read2 = locker.WithReadLockAsync(() => cs.ReadAsync(5));
            Assert.Equal(1, cs.WriterCount);
            Assert.Equal(0, cs.ReaderCount);

            Task.WhenAll(read1, read2, write1).Wait();
        }

        [Fact]
        public void MultipleReadersMultipleWriters()
        {
            var locker = new AsyncReaderWriterLock();
            var cs = new CriticalSection();

            var readersWriters = new[]
            {false, true, false, false, true, false, false, false, true, true, false, false, false, true, false, true, true, false, false, true};

            var readerWriterTasks = new Task[readersWriters.Length];

            Parallel.For(0, readersWriters.Length, index =>
            {
                readerWriterTasks[index] = readersWriters[index]
                    ? locker.WithWriteLockAsync(() => cs.WriteAsync(10))
                    : locker.WithReadLockAsync(() => cs.ReadAsync(10));
            });

            Task.WhenAll(readerWriterTasks).Wait();
        }

        private class CriticalSection
        {
            private int _readerCount;
            private int _writerCount;

            public int ReaderCount => Volatile.Read(ref _readerCount);

            public int WriterCount => Volatile.Read(ref _writerCount);

            public async Task ReadAsync(int readTime)
            {
                Interlocked.Increment(ref _readerCount);
                _writerCount.Should().Be(0);
                await Task.Delay(readTime);
                Interlocked.Decrement(ref _readerCount);
            }

            public async Task WriteAsync(int writeTime)
            {
                Interlocked.Increment(ref _writerCount);
                _writerCount.Should().Be(1);
                _readerCount.Should().Be(0);
                await Task.Delay(writeTime);
                Interlocked.Decrement(ref _writerCount);
            }
        }
    }
}
