// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;
using Xunit;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.BuildXL.Utilities
{
    public sealed class InliningWriterTests
    {
        [Fact]
        public void RoundTripInlining()
        {
            var pt = new PathTable();
            var st = pt.StringTable;

            var paths = new AbsolutePath[]
            {
                AbsolutePath.Create(pt, A("c","a","b","c","d")),
                AbsolutePath.Create(pt, A("c","a","b","c")),
                AbsolutePath.Create(pt, A("d","AAA","CCC")),
                AbsolutePath.Create(pt, A("D","AAA","CCC")),
                AbsolutePath.Create(pt, A("F","BBB","CCC"))
            };

            var strings = new StringId[]
            {
                StringId.Create(st, "AAA"),
                StringId.Create(st, "hello"),
                StringId.Create(st, "繙BШЂЋЧЉЊЖ"),
                StringId.Create(st, "buildxl"),
                StringId.Create(st, "inline")
            };

            using (var stream = new MemoryStream())
            {
                using (var writer = new InliningWriter(stream, pt))
                {
                    for (int i = 0; i < paths.Length; i++)
                    {
                        writer.Write(paths[i]);
                        writer.Write(strings[i]);
                    }
                }

                stream.Position = 0;

                using (var reader = new InliningReader(stream, pt))
                {
                    for (int i = 0; i < paths.Length; i++)
                    {
                        var readPath = reader.ReadAbsolutePath();
                        var readString = reader.ReadStringId();

                        Assert.Equal(paths[i], readPath);
                        Assert.Equal(strings[i], readString);
                    }
                }

                stream.Position = 0;
                var pt2 = new PathTable();
                var st2 = pt2.StringTable;
                AbsolutePath.Create(pt2, A("d"));
                AbsolutePath.Create(pt2, A("x","dir", "buildxl", "file.txt"));

                using (var reader = new InliningReader(stream, pt2))
                {
                    for (int i = 0; i < paths.Length; i++)
                    {
                        var readPath = reader.ReadAbsolutePath();
                        var readString = reader.ReadStringId();

                        Assert.Equal(paths[i].ToString(pt).ToUpperInvariant(), readPath.ToString(pt2).ToUpperInvariant());
                        Assert.Equal(strings[i].ToString(st), readString.ToString(st2));
                    }
                }
            }
        }
    }
}
