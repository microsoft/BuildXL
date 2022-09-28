// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Runtime.InteropServices;
using BuildXL.Native.IO;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

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
    }
}
