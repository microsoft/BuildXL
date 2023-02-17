// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class PathMapSerializerTests : TemporaryStorageTestBase
    {
        public PathMapSerializerTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void BasicTest()
        {
            var symlinkMap = new Dictionary<string, string>
            {
                [A("X","file1_cpy.txt")] = A("X","file1.txt"),
                [A("X","file2_cpy.txt")] = A("X","file2.txt")
            };
            
            var file = GetFullPath("SomeFileMappingDefinition");
            var pathMapSerializer = new PathMapSerializer(file);

            foreach (var mapping in symlinkMap)
            {
                pathMapSerializer.OnNext(mapping);
            }

            ((IObserver<KeyValuePair<string, string>>)pathMapSerializer).OnCompleted();

            PathTable newPathTable = new PathTable();
            var loadedSymlinkMap = PathMapSerializer.LoadAsync(file, newPathTable).Result;

            foreach (var mapping in loadedSymlinkMap)
            {
                var source = mapping.Key.ToString(newPathTable);
                var target = mapping.Value.ToString(newPathTable);

                XAssert.IsTrue(symlinkMap.ContainsKey(source));
                XAssert.AreEqual(symlinkMap[source], target);
            }
        }

        [Fact]
        public void TestName()
        {
            // Office takes PathMapSerializer as a dependency, and thus, renaming the class can break them.
            XAssert.AreEqual("PathMapSerializer", nameof(PathMapSerializer));
        }
    }
}
