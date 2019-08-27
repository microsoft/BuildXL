// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class InliningReaderWriterTests : XunitBuildXLTest
    {
        public InliningReaderWriterTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestReadWrite()
        {
            using (var stream1 = new MemoryStream())
            using (var stream2 = new MemoryStream())
            {
                var pt1 = new PathTable();

                using (var writer1 = new InliningWriter(stream1, pt1, leaveOpen: true))
                {
                    writer1.Write(AbsolutePath.Create(pt1, A("C", "f", "g", "h1")));
                    writer1.Write(AbsolutePath.Create(pt1, A("C", "f", "g", "h")));
                    writer1.Write(PathAtom.Create(pt1.StringTable, "a1"));
                    writer1.Write(PathAtom.Create(pt1.StringTable, "a"));
                    writer1.Write(StringId.Create(pt1.StringTable, "s1"));
                    writer1.Write(StringId.Create(pt1.StringTable, "s"));
                }

                var pt2 = new PathTable();

                using (var writer2 = new InliningWriter(stream2, pt2, leaveOpen: true))
                {
                    writer2.Write(AbsolutePath.Create(pt2, A("C", "f", "g", "h2")));
                    writer2.Write(AbsolutePath.Create(pt2, A("C", "f", "g", "h")));
                    writer2.Write(PathAtom.Create(pt2.StringTable, "a2"));
                    writer2.Write(PathAtom.Create(pt2.StringTable, "a"));
                    writer2.Write(StringId.Create(pt2.StringTable, "s2"));
                    writer2.Write(StringId.Create(pt2.StringTable, "s"));
                }

                stream1.Seek(0, SeekOrigin.Begin);
                stream2.Seek(0, SeekOrigin.Begin);

                var pt = new PathTable();

                using (var reader1 = new InliningReader(stream1, pt, leaveOpen: true))
                {
                    XAssert.AreEqual(A("C", "f", "g", "h1").ToUpperInvariant(), reader1.ReadAbsolutePath().ToString(pt).ToUpperInvariant());
                    XAssert.AreEqual(A("C", "f", "g", "h").ToUpperInvariant(), reader1.ReadAbsolutePath().ToString(pt).ToUpperInvariant());
                    XAssert.AreEqual("a1".ToUpperInvariant(), reader1.ReadPathAtom().ToString(pt.StringTable).ToUpperInvariant());
                    XAssert.AreEqual("a".ToUpperInvariant(), reader1.ReadPathAtom().ToString(pt.StringTable).ToUpperInvariant());
                    XAssert.AreEqual("s1", reader1.ReadStringId().ToString(pt.StringTable));
                    XAssert.AreEqual("s", reader1.ReadStringId().ToString(pt.StringTable));
                }

                using (var reader2 = new InliningReader(stream2, pt, leaveOpen: true))
                {
                    XAssert.AreEqual(A("C", "f", "g", "h2").ToUpperInvariant(), reader2.ReadAbsolutePath().ToString(pt).ToUpperInvariant());
                    XAssert.AreEqual(A("C", "f", "g", "h").ToUpperInvariant(), reader2.ReadAbsolutePath().ToString(pt).ToUpperInvariant());
                    XAssert.AreEqual("a2".ToUpperInvariant(), reader2.ReadPathAtom().ToString(pt.StringTable).ToUpperInvariant());
                    XAssert.AreEqual("a".ToUpperInvariant(), reader2.ReadPathAtom().ToString(pt.StringTable).ToUpperInvariant());
                    XAssert.AreEqual("s2", reader2.ReadStringId().ToString(pt.StringTable));
                    XAssert.AreEqual("s", reader2.ReadStringId().ToString(pt.StringTable));
                }
            }
        }
    }
}
