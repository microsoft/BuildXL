// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BuildXL.Native.IO;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Tests for <see cref="BigBuffer{TEntry}"/>
    /// </summary>
    public class BigBufferTests : XunitBuildXLTest
    {
        private const int LargeObjectHeapLimit = 85_000;

        public BigBufferTests(ITestOutputHelper output)
            : base(output) { }
        
        [StructLayout(LayoutKind.Explicit, Size = 33)]
        private struct ContentHash { }

        private readonly record struct Entry(ContentHash Hash, ushort TimeToLive, ulong Usn, long Length);

        [Fact]
        public void BigBufferShouldNotAllocateInLargeObjectHeap()
        {
            // Making sure we can correctly compute the size for generic structs, for instance, Marshal.Sizeof<T> fails when T is a generic type.
            var buffer = new BigBuffer<KeyValuePair<FileIdAndVolumeId, Entry>>();
            var entrySize = TypeInspector.GetSize(typeof(KeyValuePair<FileIdAndVolumeId, Entry>)).size;

            var arraySize = buffer.EntriesPerBuffer * entrySize;
            Assert.True(arraySize < LargeObjectHeapLimit, $"arraySize: {arraySize}, LargeObjectHeapLimit: {LargeObjectHeapLimit}");

            // Originally such key-value-pair were allocated in LOH
            Assert.True(buffer.EntriesPerBuffer < BigBuffer<KeyValuePair<FileIdAndVolumeId, Entry>>.DefaultEntriesPerBuffer);
        }
        
        [Fact]
        public void BigBufferSizeForReferenceTypes()
        {
            // Making sure we can correctly compute the size for generic structs, for instance, Marshal.Sizeof<T> fails when T is a generic type.
            var buffer = new BigBuffer<string>();
            var arraySize = buffer.EntriesPerBuffer * TypeInspector.GetSize(typeof(string)).size;
            Assert.True(arraySize < LargeObjectHeapLimit, $"arraySize: {arraySize}, LargeObjectHeapLimit: {LargeObjectHeapLimit}");

            Assert.True(buffer.EntriesPerBuffer > BigBuffer<KeyValuePair<FileIdAndVolumeId, Entry>>.DefaultEntriesPerBuffer);
        }

        [Theory]
        [InlineData(20)]
        [InlineData(24)]
        public void InitializeBeyondHalfOfIntRange(int entriesPerBufferBitWidth)
        {
            // Doubling 2^30 produces 2^31, exceeding int.MaxValue and overflowing checked int arithmetic.
            const int doublingOverflowThreshold = 1 << 30;

            // Only lazy page descriptors are allocated, not the billion-entry backing storage.
            var buffer = new BigBuffer<int>(entriesPerBufferBitWidth);
            Assert.Equal(doublingOverflowThreshold, buffer.Initialize(doublingOverflowThreshold));

            var maximumCapacity = int.MaxValue & ~(buffer.EntriesPerBuffer - 1);
            Assert.Equal(maximumCapacity, buffer.Initialize(doublingOverflowThreshold + 1));
            Assert.Equal(maximumCapacity, buffer.Capacity);
            Assert.Equal(maximumCapacity / buffer.EntriesPerBuffer, buffer.NumberOfBuffers);
        }

        [Theory]
        [InlineData(20)]
        [InlineData(24)]
        [InlineData(30)]
        public void InitializeMaximumAlignedCapacity(int entriesPerBufferBitWidth)
        {
            var buffer = new BigBuffer<int>(entriesPerBufferBitWidth);
            var maximumCapacity = int.MaxValue & ~(buffer.EntriesPerBuffer - 1);
            Assert.Equal(maximumCapacity, buffer.Initialize(maximumCapacity));

            Assert.Throws<OverflowException>(() => buffer.Initialize(maximumCapacity + 1));
            Assert.Equal(maximumCapacity, buffer.Capacity);
        }

        [Fact]
        public void InitializePreservesEntriesAcrossGrowth()
        {
            var buffer = new BigBuffer<int>(entriesPerBufferBitWidth: 4);
            var originalCapacity = buffer.Initialize(1);
            buffer[0] = 42;

            Assert.Equal(originalCapacity * 2, buffer.Initialize(originalCapacity + 1));
            Assert.Equal(42, buffer[0]);
            Assert.Equal(0, buffer[originalCapacity]);

            buffer[originalCapacity] = 123;
            Assert.Equal(originalCapacity * 2, buffer.Initialize(1));
            Assert.Equal(42, buffer[0]);
            Assert.Equal(123, buffer[originalCapacity]);
        }
    }
}
