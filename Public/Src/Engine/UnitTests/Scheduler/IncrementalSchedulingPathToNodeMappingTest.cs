// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using IncrementalSchedulingPathToNodeMapping = BuildXL.Scheduler.IncrementalScheduling.IncrementalSchedulingPathMapping<BuildXL.Scheduler.Graph.NodeId>;

namespace Test.BuildXL.Scheduler
{
    public class IncrementalSchedulingPathToNodeMappingTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public IncrementalSchedulingPathToNodeMappingTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestEmptySerialization()
        {
            var mapping = new IncrementalSchedulingPathToNodeMapping();
            Assert.Equal(0, mapping.PathCount);

            mapping = SaveAndReload(mapping);
            Assert.Equal(0, mapping.PathCount);
        }

        [Fact]
        public void AddSimple()
        {
            var pt = new PathTable();
            var f1 = AbsolutePath.Create(pt, X("/c/file1"));
            var f2 = AbsolutePath.Create(pt, X("/c/file2"));
            var f3 = AbsolutePath.Create(pt, X("/c/file3"));
            var mapping = new IncrementalSchedulingPathToNodeMapping();
            mapping.AddEntry(new NodeId(1), f1);
            mapping.AddEntry(new NodeId(1), f2);
            mapping.AddEntry(new NodeId(2), f3);

            Assert.Equal(3, mapping.PathCount);
            AssertMapping(mapping, f1, 1);
            AssertMapping(mapping, f2, 1);
            AssertMapping(mapping, f3, 2);

            mapping = SaveAndReload(mapping);
            Assert.Equal(3, mapping.PathCount);
            AssertMapping(mapping, f1, 1);
            AssertMapping(mapping, f2, 1);
            AssertMapping(mapping, f3, 2);
        }

        [Fact]
        public void AddDuplicates()
        {
            var pt = new PathTable();
            var f1 = AbsolutePath.Create(pt, X("/c/file1"));
            var f2 = AbsolutePath.Create(pt, X("/c/file2"));

            var mapping = new IncrementalSchedulingPathToNodeMapping();
            mapping.AddEntry(new NodeId(1), f1);
            mapping.AddEntry(new NodeId(1), f1);
            mapping.AddEntry(new NodeId(2), f1);
            mapping.AddEntry(new NodeId(1), f2);
            mapping.AddEntry(new NodeId(1), f2);
            mapping.AddEntry(new NodeId(2), f2);
            mapping.AddEntry(new NodeId(2), f2);
            mapping.AddEntry(new NodeId(3), f2);

            Assert.Equal(2, mapping.PathCount);
            AssertMapping(mapping, f1, 1, 2);
            AssertMapping(mapping, f2, 1, 2, 3);

            mapping = SaveAndReload(mapping);
            Assert.Equal(2, mapping.PathCount);
            AssertMapping(mapping, f1, 1, 2);
            AssertMapping(mapping, f2, 1, 2, 3);
        }

        [Fact]
        public void ClearNode()
        {
            var pt = new PathTable();
            var f1 = AbsolutePath.Create(pt, X("/c/file1"));
            var f2 = AbsolutePath.Create(pt, X("/c/file2"));
            var f3 = AbsolutePath.Create(pt, X("/c/file3"));
            var f4 = AbsolutePath.Create(pt, X("/c/file4"));

            var mapping = new IncrementalSchedulingPathToNodeMapping();
            mapping.AddEntry(new NodeId(1), f1);
            mapping.AddEntry(new NodeId(1), f1);
            mapping.AddEntry(new NodeId(2), f1);
            mapping.AddEntry(new NodeId(1), f2);
            mapping.AddEntry(new NodeId(1), f2);
            mapping.AddEntry(new NodeId(2), f2);
            mapping.AddEntry(new NodeId(2), f2);
            mapping.AddEntry(new NodeId(3), f2);
            mapping.AddEntry(new NodeId(2), f3);
            mapping.AddEntry(new NodeId(4), f4);

            Assert.Equal(4, mapping.PathCount);
            AssertMapping(mapping, f1, 1, 2);
            AssertMapping(mapping, f2, 1, 2, 3);
            AssertMapping(mapping, f3, 2);
            AssertMapping(mapping, f4, 4);

            mapping.ClearValue(new NodeId(2));

            Assert.Equal(4, mapping.PathCount);
            AssertMapping(mapping, f1, 1);
            AssertMapping(mapping, f2, 1, 3);
            AssertMapping(mapping, f3);
            AssertMapping(mapping, f4, 4);

            mapping = SaveAndReload(mapping);

            Assert.Equal(4, mapping.PathCount);
            AssertMapping(mapping, f1, 1);
            AssertMapping(mapping, f2, 1, 3);
            AssertMapping(mapping, f3);
            AssertMapping(mapping, f1, 1);

            mapping.ClearValue(new NodeId(1));

            Assert.Equal(4, mapping.PathCount);
            AssertMapping(mapping, f1);
            AssertMapping(mapping, f2, 3);
            AssertMapping(mapping, f3);
            AssertMapping(mapping, f4, 4);

            mapping = SaveAndReload(mapping);

            Assert.Equal(4, mapping.PathCount);
            AssertMapping(mapping, f1);
            AssertMapping(mapping, f2, 3);
            AssertMapping(mapping, f3);
            AssertMapping(mapping, f4, 4);
        }

        [Fact]
        public void ClearNonExistingNode()
        {
            var pt = new PathTable();
            var f1 = AbsolutePath.Create(pt, X("/c/file1"));

            var mapping = new IncrementalSchedulingPathToNodeMapping();
            mapping.AddEntry(new NodeId(1), f1);
            mapping.AddEntry(new NodeId(2), f1);

            Assert.Equal(1, mapping.PathCount);
            AssertMapping(mapping, f1, 1, 2);

            mapping.ClearValue(new NodeId(3));

            Assert.Equal(1, mapping.PathCount);
            AssertMapping(mapping, f1, 1, 2);
        }

        private void AssertMapping(IncrementalSchedulingPathToNodeMapping mapping, AbsolutePath path, params uint[] nodeIds)
        {
            IEnumerable<NodeId> nodes = null;
            Assert.True(mapping.TryGetValues(path, out nodes));
            Assert.Equal(nodes.OrderBy(n => n.Value), nodeIds.OrderBy(n => n).Select(id => new NodeId(id)));
        }

        private IncrementalSchedulingPathToNodeMapping SaveAndReload(IncrementalSchedulingPathToNodeMapping current)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BuildXLWriter(debug: false, stream: stream, leaveOpen: true, logStats: false))
                {
                    current.Serialize(writer, (w, n) => w.Write(n.Value));
                }

                // rewind
                stream.Position = 0;

                using (var reader = new BuildXLReader(debug: false, stream: stream, leaveOpen: true))
                {
                    return IncrementalSchedulingPathToNodeMapping.Deserialize(reader, r => new NodeId(r.ReadUInt32()));
                }
            }
        }
    }
}
