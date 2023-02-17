// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Serialization;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class StringTableTests : XunitBuildXLTest
    {
        public StringTableTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void SizeMix()
        {
            var st = new StringTable(0);
            var map = new Dictionary<StringId, string>();
            var sb = new StringBuilder(3000000);

            // zip through small sizes
            for (int i = 0; i < 3000; i++)
            {
                sb.Length = 0;
                sb.Append('x', i);
                string str = sb.ToString();
                StringId sd = st.AddString(str);
                map.Add(sd, str);
            }

            // now use increasingly large amounts, including exceeding the size of a single buffer's worth
            for (int i = 0; i < 10; i++)
            {
                sb.Length = 0;
                sb.Append('x', i * 10000);
                string str = sb.ToString();
                StringId sd = st.AddString(str);

                if (!map.ContainsKey(sd))
                {
                    map.Add(sd, str);
                }
            }

            for (int i = 0; i < 2; i++)
            {
                // make sure all the right strings come out
                int startBias = 0;
                var buf = new char[4000000];
                foreach (StringId sd in map.Keys)
                {
                    // get the actual string we kept
                    string str = map[sd];

                    // does the table report the right length?
                    int length = st.GetLength(sd);
                    XAssert.AreEqual(str.Length, length);

                    // put sentinel bytes in the char buffer, extract the string from the table, and check the sentinels
                    if (startBias > 0)
                    {
                        buf[startBias - 1] = (char)42;
                    }

                    buf[startBias + length] = (char)31415;
                    st.CopyString(sd, buf, startBias);

                    if (startBias > 0)
                    {
                        XAssert.AreEqual(42, buf[startBias - 1]);
                    }

                    XAssert.AreEqual(31415, buf[startBias + length]);

                    // make sure we got all the characters out that we should have
                    for (int j = 0; j < str.Length; j++)
                    {
                        XAssert.AreEqual(str[j], buf[startBias + j]);
                    }

                    startBias++;
                }

                // make sure the same behavior occurs after freezing
                st.Freeze();
            }
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public async Task SizeMixParallel(bool asciiOnly, bool useDeflateStream)
        {
            var harness = new StringTableTestHarness();
            var st = harness.StringTable;

            ConcurrentDictionary<StringId, string> map = new ConcurrentDictionary<StringId, string>();
            var sb = new StringBuilder(3000000);

            var strings = new BlockingCollection<string>();
            var stringIds = new BlockingCollection<StringId>();

            var createStringsTask = Task.Run(() =>
                {
                    // zip through small sizes
                    for (int i = 0; i < 3000; i++)
                    {
                        sb.Length = 0;
                        sb.Append(asciiOnly ? 'x' : '建', i);
                        string str = sb.ToString();
                        strings.Add(str);
                    }

                    // now use increasingly large amounts, including exceeding the size of a single buffer's worth
                    for (int i = 0; i < 100; i++)
                    {
                        sb.Length = 0;
                        sb.Append('x', i * 10000);
                        string str = sb.ToString();
                    }

                    strings.CompleteAdding();
                });

            var validateStringsTask = Task.Run(() =>
                {
                    for (int i = 0; i < 2; i++)
                    {
                        // make sure all the right strings come out
                        int startBias = 0;
                        var buf = new char[4000000];
                        foreach (StringId sd in stringIds.GetConsumingEnumerable())
                        {
                            // get the actual string we kept
                            string str = map[sd];

                            // does the table report the right length?
                            int length = st.GetLength(sd);
                            XAssert.AreEqual(str.Length, length);

                            // put sentinel bytes in the char buffer, extract the string from the table, and check the sentinels
                            if (startBias > 0)
                            {
                                buf[startBias - 1] = (char)42;
                            }

                            buf[startBias + length] = (char)31415;
                            st.CopyString(sd, buf, startBias);

                            if (startBias > 0)
                            {
                                XAssert.AreEqual(42, buf[startBias - 1]);
                            }

                            XAssert.AreEqual(31415, buf[startBias + length]);

                            // make sure we got all the characters out that we should have
                            for (int j = 0; j < str.Length; j++)
                            {
                                XAssert.AreEqual(str[j], buf[startBias + j]);
                            }

                            startBias++;
                        }

                        // make sure the same behavior occurs after freezing
                        st.Freeze();
                    }
                });

            Parallel.ForEach(strings.GetConsumingEnumerable(), str =>
                {
                    StringId sd = st.AddString(str);
                    map.TryAdd(sd, str);
                    stringIds.Add(sd);
                });

            stringIds.CompleteAdding();

            await createStringsTask;
            await validateStringsTask;
        }

        [Fact]
        public async Task CharMix()
        {
            var harness = new StringTableTestHarness();
            var st = harness.StringTable;

            // make sure all characters are handled properly
            var map = new Dictionary<StringId, string>();
            var sb = new StringBuilder(3000000);

            // zip through small sizes
            for (int i = 0; i < 65535; i++)
            {
                sb.Length = 0;
                sb.Append((char)i, (i % 255) + 1);
                string str = sb.ToString();
                StringId sd = harness.AddString(str);
                map.Add(sd, str);
            }

            await harness.RunCommonTestsAsync();

            for (int i = 0; i < 2; i++)
            {
                // make sure all the right strings come out
                var buf = new char[65535];
                foreach (StringId sd in map.Keys)
                {
                    // get the actual string we kept
                    string str = map[sd];

                    // does the table report the right length?
                    int length = st.GetLength(sd);
                    XAssert.AreEqual(str.Length, length);

                    // extract the chars from the table
                    st.CopyString(sd, buf, 0);

                    // make sure we got all the characters out that we should have
                    for (int j = 0; j < str.Length; j++)
                    {
                        XAssert.AreEqual(str[j], buf[j]);
                    }
                }

                // make sure the same behavior occurs after freezing
                st.Freeze();
            }
        }

        [Fact]
        public Task Match()
        {
            var harness = new StringTableTestHarness();
            var st = harness.StringTable;

            StringId id1 = harness.AddString("Hello");
            StringId id2 = harness.AddString("Goodbye");
            StringId id3 = harness.AddString("hello");
            StringId id4 = harness.AddString("goodBYE");
            StringId id5 = harness.AddString("HELLOX");
            StringId id6 = harness.AddString("HEL");

            XAssert.AreNotEqual(id1, id3);
            XAssert.AreNotEqual(id1, id5);
            XAssert.AreNotEqual(id1, id6);
            XAssert.AreNotEqual(id5, id6);
            XAssert.AreNotEqual(id2, id4);

            // different length, different character sizes
            StringId ascii = st.AddString("abc");
            XAssert.IsFalse(st.CaseInsensitiveEquals(ascii, st.AddString("\u1234\u1234")));
            XAssert.IsTrue(st.CaseInsensitiveEquals(ascii, st.AddString("abc")));
            XAssert.IsFalse(st.Equals("\u1234\u1234", ascii));
            XAssert.IsTrue(st.Equals("abc", ascii));

            // same length, different character sizes
            ascii = st.AddString("abc");
            XAssert.IsFalse(st.CaseInsensitiveEquals(ascii, st.AddString("\u1234\u1234\u1234")));
            XAssert.IsTrue(st.CaseInsensitiveEquals(ascii, st.AddString("abc")));
            XAssert.IsFalse(st.Equals("\u1234\u1234\u1234", ascii));
            XAssert.IsTrue(st.Equals("abc", ascii));

            // different length, different character sizes
            StringId utf16 = st.AddString("\u1234\u1234");
            XAssert.IsFalse(st.CaseInsensitiveEquals(utf16, st.AddString("abc")));
            XAssert.IsTrue(st.CaseInsensitiveEquals(utf16, st.AddString("\u1234\u1234")));
            XAssert.IsFalse(st.Equals("abc", utf16));
            XAssert.IsTrue(st.Equals("\u1234\u1234", utf16));

            // same length, different character sizes
            utf16 = st.AddString("\u1234\u1234");
            XAssert.IsFalse(st.CaseInsensitiveEquals(utf16, st.AddString("ab")));
            XAssert.IsTrue(st.CaseInsensitiveEquals(utf16, st.AddString("\u1234\u1234")));
            XAssert.IsFalse(st.Equals("ab", utf16));
            XAssert.IsTrue(st.Equals("\u1234\u1234", utf16));

            return harness.RunCommonTestsAsync();
        }

        [Fact]
        public void Substrings()
        {
            var st = new StringTable(0);

            var s1 = st.AddString(new StringSegment("abc1234def", 3, 4));
            var s2 = st.AddString(new StringSegment("abc1234def", 3, 4));
            var str = st.GetString(s1);

            XAssert.AreEqual(s1, s2);
            XAssert.AreEqual("1234", str);
        }

        [Fact]
        public Task BigStrings()
        {
            var harness = new StringTableTestHarness();
            var st = harness.StringTable;

            var s1 = new string('x', StringTable.BytesPerBuffer - 1);
            var s2 = new string('y', StringTable.BytesPerBuffer);
            var s3 = new string('z', StringTable.BytesPerBuffer + 1);

            StringId id1 = harness.AddString(s1);
            StringId id2 = harness.AddString(s2);
            StringId id3 = harness.AddString(s3);
            harness.AddString(s3 + "繙");

            XAssert.AreEqual(s1.Length, st.GetLength(id1));
            XAssert.AreEqual(s2.Length, st.GetLength(id2));
            XAssert.AreEqual(s3.Length, st.GetLength(id3));

            var buf = new char[StringTable.BytesPerBuffer + 1];

            st.CopyString(id1, buf, 0);
            for (int j = 0; j < s1.Length; j++)
            {
                XAssert.AreEqual(s1[j], buf[j]);
            }

            st.CopyString(id2, buf, 0);
            for (int j = 0; j < s2.Length; j++)
            {
                XAssert.AreEqual(s2[j], buf[j]);
            }

            st.CopyString(id3, buf, 0);
            for (int j = 0; j < s3.Length; j++)
            {
                XAssert.AreEqual(s3[j], buf[j]);
            }

            XAssert.IsTrue(st.CaseInsensitiveEquals(id1, st.AddString(s1)));
            XAssert.IsTrue(st.CaseInsensitiveEquals(id2, st.AddString(s2)));
            XAssert.IsTrue(st.CaseInsensitiveEquals(id3, st.AddString(s3)));

            XAssert.IsTrue(st.Equals(s1, id1));
            XAssert.IsTrue(st.Equals(s2, id2));
            XAssert.IsTrue(st.Equals(s3, id3));

            XAssert.IsFalse(st.CaseInsensitiveEquals(id1, st.AddString(s2)));
            XAssert.IsFalse(st.CaseInsensitiveEquals(id1, st.AddString(s3)));
            XAssert.IsFalse(st.CaseInsensitiveEquals(id2, st.AddString(s1)));
            XAssert.IsFalse(st.CaseInsensitiveEquals(id2, st.AddString(s3)));
            XAssert.IsFalse(st.CaseInsensitiveEquals(id3, st.AddString(s1)));
            XAssert.IsFalse(st.CaseInsensitiveEquals(id3, st.AddString(s2)));

            XAssert.IsFalse(st.Equals(s2, id1));
            XAssert.IsFalse(st.Equals(s3, id1));
            XAssert.IsFalse(st.Equals(s1, id2));
            XAssert.IsFalse(st.Equals(s3, id2));
            XAssert.IsFalse(st.Equals(s1, id3));
            XAssert.IsFalse(st.Equals(s2, id3));

            return harness.RunCommonTestsAsync();
        }

        [Fact]
        public void CaseSensitiveEqualityComparer()
        {
            var st = new StringTable(0);
            IEqualityComparer<StringId> comp = new OrdinalStringIdEqualityComparer();

            StringId d1 = st.AddString("123");
            StringId d2 = st.AddString("456");

            XAssert.IsTrue(comp.Equals(d1, d1));
            XAssert.IsFalse(comp.Equals(d1, d2));

            StringId d3 = st.AddString("ABC");
            StringId d4 = st.AddString("abc");
            XAssert.IsFalse(comp.Equals(d3, d4));
        }

        [Fact]
        public void CaseInsensitiveEqualityComparer()
        {
            var st = new StringTable(0);
            IEqualityComparer<StringId> comp = new CaseInsensitiveStringIdEqualityComparer(st);

            StringId d1 = st.AddString("123");
            StringId d2 = st.AddString("456");

            XAssert.IsTrue(comp.Equals(d1, d1));
            XAssert.IsFalse(comp.Equals(d1, d2));

            StringId d3 = st.AddString("ABC");
            StringId d4 = st.AddString("abc");
            XAssert.IsTrue(comp.Equals(d3, d4));
        }

        [Fact]
        public void CaseSensitiveComparer()
        {
            var st = new StringTable(0);
            IComparer<StringId> comp = new OrdinalStringIdComparer(st);

            StringId d1 = st.AddString("123");
            StringId d2 = st.AddString("456");

            XAssert.IsTrue(comp.Compare(d1, d1) == 0);
            XAssert.IsTrue(comp.Compare(d1, d2) < 0);

            StringId d3 = st.AddString("ABC");
            StringId d4 = st.AddString("abc");
            XAssert.IsTrue(comp.Compare(d4, d3) > 0);
        }

        [Fact]
        public void CaseInsensitiveComparer()
        {
            var st = new StringTable(0);
            IComparer<StringId> comp = new CaseInsensitiveStringIdComparer(st);

            StringId d1 = st.AddString("123");
            StringId d2 = st.AddString("456");

            XAssert.IsTrue(comp.Compare(d1, d1) == 0);
            XAssert.IsTrue(comp.Compare(d1, d2) < 0);

            StringId d3 = st.AddString("ABC");
            StringId d4 = st.AddString("abc");
            XAssert.IsTrue(comp.Compare(d3, d4) == 0);
        }

        [Fact]
        public void Stats()
        {
            var st = new StringTable(0);
            st.AddString("AAA");
            XAssert.IsTrue(st.SizeInBytes > 0);
        }

        [Fact]
        public void AbsolutePathEquality()
        {
            StructTester.TestEquality(
                baseValue: new StringId(123),
                equalValue: new StringId(123),
                notEqualValues: new StringId[] { StringId.Invalid, new StringId(124) },
                eq: (a, b) => a == b,
                neq: (a, b) => a != b);
        }

        [Fact]
        public void BinaryStringRoundrip()
        {
            var st = new StringTable();
            var unicodeStringId = st.AddString("繙B");
            var asciiStringId = st.AddString("Hello");

            var binaryUnicodeString = st.GetBinaryString(unicodeStringId);
            var binaryAsciiString = st.GetBinaryString(asciiStringId);

            var roundTrippedUnicodeStringId = st.AddString(binaryUnicodeString);
            var roundTrippedAsciiStringId = st.AddString(binaryAsciiString);

            XAssert.AreEqual(unicodeStringId, roundTrippedUnicodeStringId);
            XAssert.AreEqual(asciiStringId, roundTrippedAsciiStringId);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public Task Serialization(bool includeOverflowBuffers, bool useDeflateStream)
{
            var st = new StringTableTestHarness(includeOverflowBuffers ? 3 : 0, useDeflateStream);
            // Adding a lot of strings to check a weird case that was happening with deflated strings in .NET6.
            int count = 10_000;
            for (int i = 0; i < count; i++)
            {
                var guid = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                st.AddString(guid);
            }

            return st.RunCommonTestsAsync();
        }

        [Fact]
        public async Task SpillIntoOverflowBuffer()
        {
            // All but the first row of the table will be overflow buffers
            var st = new StringTableTestHarness(StringTable.MaxNumByteBuffers - 1);

            var guidSize = Guid.NewGuid().ToString("X").Length;

            // Make sure that we fill the first bucket and spill into the overflows
            int repetitions = (int)(1.25 * (StringTable.BytesPerBuffer / guidSize));
            for (var i = 0; i < repetitions; i++)
            {
                st.AddString(Guid.NewGuid().ToString("X"));
            }

            var lastId = st.AddString(Guid.NewGuid().ToString("X"));
            var bufferNum = (lastId.Value >> StringTable.BytesPerBufferBits) & StringTable.NumByteBuffersMask;
            Assert.Equal(1, bufferNum); // Spilled into the first overflow buffer

            await st.RunCommonTestsAsync();
            
            // Add more strings -- if the rows were regular buffers we would fill the second row
            // and go into the third, but with the overflow buffers we have increased capacity 
            for (var i = 0; i < repetitions; i++)
            {
                st.AddString(Guid.NewGuid().ToString("X"));
            }

            lastId = st.AddString(Guid.NewGuid().ToString("X"));
            bufferNum = (lastId.Value >> StringTable.BytesPerBufferBits) & StringTable.NumByteBuffersMask;
            Assert.Equal(1, bufferNum); // We are still filling the first overflow buffer
        }

        private class StringTableTestHarness
        {
            private readonly bool m_useDeflateStream;

            public StringTable StringTable { get; }

            public Dictionary<StringId, string> AddedStrings = new Dictionary<StringId, string>();

            public long MaxSize = 0;

            public StringTableTestHarness(int? overflowBufferCount = null, bool useDeflateStream = false)
            {
                m_useDeflateStream = useDeflateStream;
                StringTable = new StringTable(overflowBufferCount: overflowBufferCount);
            }

            public StringId AddString(string value)
            {
                var stringId = StringTable.AddString(value);
                XAssert.AreEqual(value.Length, StringTable.GetLength(stringId));
                AddedStrings[stringId] = value;

                MaxSize += value.Length * 2 + 10 /* overestimate of per string overhead */;

                return stringId;
            }

            public async Task RunCommonTestsAsync()
            {
                XAssert.AreNotEqual(StringTable.Count, AddedStrings.Count, "Ensure strings are added through harness");

                StringTable deserializedStringTable = null;
                using (MemoryStream ms = new MemoryStream())
                {
                    using (Stream stream = createWrapperStream(ms, compress: true))
                    {
                        using (BuildXLWriter writer = new BuildXLWriter(true, stream, true, logStats: true))
                        {
                            StringTable.Serialize(writer);
                        }

                        if (MaxSize < StringTable.BytesPerBuffer)
                        {
                            XAssert.IsTrue(stream.Position < StringTable.BytesPerBuffer,
                                "Small StringTable should not serialize a full empty byte buffer.");
                        }
                    }

                    // We can't set a position on a wrapper stream, setting it for the memory stream that contains the data.
                    using (MemoryStream ms2 = new MemoryStream(ms.ToArray()))
                    using (Stream readStream = createWrapperStream(ms2, compress: false))
                    using (BuildXLReader reader = new BuildXLReader(true, readStream, true))
                    {
                        deserializedStringTable = await StringTable.DeserializeAsync(reader);
                    }
                }

                foreach (var entry in AddedStrings)
                {
                    // Test deserialization behaviors
                    XAssert.AreEqual(entry.Value.Length, deserializedStringTable.GetLength(entry.Key));
                    XAssert.AreEqual(entry.Value.Length, deserializedStringTable.GetBinaryString(entry.Key).Length);
                    XAssert.AreEqual(entry.Value, deserializedStringTable.GetString(entry.Key));
                    XAssert.AreEqual(entry.Key, deserializedStringTable.AddString(entry.Value));

                    // Test original string table behaviors
                    XAssert.AreEqual(entry.Key, StringTable.AddString(entry.Value));
                    XAssert.AreEqual(entry.Value.Length, StringTable.GetLength(entry.Key));
                    XAssert.AreEqual(entry.Value.Length, StringTable.GetBinaryString(entry.Key).Length);
                    XAssert.AreEqual(entry.Value, StringTable.GetString(entry.Key));
                }
                
                Stream createWrapperStream(MemoryStream memoryStream, bool compress)
                {
                    if (m_useDeflateStream)
                    {
                        return new TrackedStream(
                            new BufferedStream(
                                compress ? new DeflateStream(memoryStream, CompressionLevel.Fastest, leaveOpen: true)
                                : new DeflateStream(memoryStream, CompressionMode.Decompress, leaveOpen: true),
                                bufferSize: 64 << 10),
                            // Need to close the underlying stream to flush the content.
                            leaveOpen: false);
                    }

                    return memoryStream;
                }
            }
        }
    }
}
