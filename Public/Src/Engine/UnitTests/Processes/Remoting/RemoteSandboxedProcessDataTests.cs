// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Processes.Remoting;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    public sealed class RemoteSandboxedProcessDataTests : XunitBuildXLTest
    {
        public RemoteSandboxedProcessDataTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestTaggedSerialization()
        {
            RemoteSandboxedProcessData data = CreateForTest();

            using var stream = new MemoryStream();
            var writer = new BuildXLWriter(false, stream, true, false);
            data.TaggedSerialize(writer);

            stream.Position = 0;
            var reader = new BuildXLReader(false, stream, true);
            RemoteSandboxedProcessData deserializeData = RemoteSandboxedProcessData.TaggedDeserialize(reader);

            AssertEqualData(data, deserializeData);
        }

        [Fact]
        public void TestSerialization()
        {
            RemoteSandboxedProcessData data = CreateForTest();

            using var stream = new MemoryStream();
            var writer = new BuildXLWriter(false, stream, true, false);
            data.Serialize(writer);

            stream.Position = 0;
            var reader = new BuildXLReader(false, stream, true);
            RemoteSandboxedProcessData deserializeData = RemoteSandboxedProcessData.Deserialize(reader);

            AssertEqualData(data, deserializeData);
        }

        [Fact]
        public void TestForwardCompatTagSerialization()
        {
            RemoteSandboxedProcessData data = CreateForTest();

            using var stream = new MemoryStream();
            var writer = new BuildXLWriter(false, stream, true, false);
            data.TaggedSerialize(writer);

            stream.Position = 0;
            var reader = new BuildXLReader(false, stream, true);
            ModifiedRemoteData deserializeData = ModifiedRemoteData.TaggedDeserialize(reader);

            AssertEqualData(
                new ModifiedRemoteData(
                    "cl.exe",
                    "--args1 --args2",
                    null,
                    new Dictionary<string, string>
                    {
                        ["key1"] = "value1",
                        ["key2"] = "value2"
                    },
                    new ReadOnlyHashSet<string>(new[] { X("C/wd/f.cpp"), X("C/wd/f.h") }),
                    new ReadOnlyHashSet<string>()),
                deserializeData);
        }

        [Fact]
        public void TestBackwardCompatTagSerialization()
        {
            ModifiedRemoteData data = new ModifiedRemoteData(
                "cl.exe",
                "--args1 --args2",
                "So useful",
                new Dictionary<string, string>
                {
                    ["key1"] = "value1",
                    ["key2"] = "value2"
                },
                new ReadOnlyHashSet<string>(new[] { X("C/wd/f.cpp"), X("C/wd/f.h") }),
                new ReadOnlyHashSet<string>(new[] { "useful1", "useful2" }));

            using var stream = new MemoryStream();
            var writer = new BuildXLWriter(false, stream, true, false);
            data.TaggedSerialize(writer);

            stream.Position = 0;
            var reader = new BuildXLReader(false, stream, true);
            RemoteSandboxedProcessData deserializeData = RemoteSandboxedProcessData.TaggedDeserialize(reader);

            AssertEqualData(
                new (
                    "cl.exe",
                    "--args1 --args2",
                    null,
                    new Dictionary<string, string>
                    {
                        ["key1"] = "value1",
                        ["key2"] = "value2"
                    },
                    new ReadOnlyHashSet<string>(new[] { X("C/wd/f.cpp"), X("C/wd/f.h") }),
                    new ReadOnlyHashSet<string>(),
                    new ReadOnlyHashSet<string>(),
                    new ReadOnlyHashSet<string>(),
                    new ReadOnlyHashSet<string>(),
                    new ReadOnlyHashSet<string>()),
                deserializeData);
        }

        private static RemoteSandboxedProcessData CreateForTest() =>
            new (
                "cl.exe",
                "--args1 --args2",
                X("/C/wd"),
                new Dictionary<string, string>
                {
                    ["key1"] = "value1",
                    ["key2"] = "value2"
                },
                new ReadOnlyHashSet<string>(new[] { X("C/wd/f.cpp"), X("C/wd/f.h") }),
                new ReadOnlyHashSet<string>(new[] { X("C/wd/telemetry"), X("C/wd/utils") }),
                new ReadOnlyHashSet<string>(new[] { X("C/wd/out/telemetry"), X("C/wd/out/utils") }),
                new ReadOnlyHashSet<string>(new[] { X("C/wd/temp") }),
                new ReadOnlyHashSet<string>(new[] { X("C/wd/cache") }),
                new ReadOnlyHashSet<string>(new[] { X("C/wd/foo.cpp") }));

        private static void AssertEqualData(RemoteSandboxedProcessData data1, RemoteSandboxedProcessData data2)
        {
            if (data1 == data2)
            {
                return;
            }

            XAssert.ArePathEqual(data1.Executable, data2.Executable);
            XAssert.AreEqual(data1.Arguments, data2.Arguments);
            XAssert.AreEqual(data1.WorkingDirectory, data2.WorkingDirectory);
            XAssert.AreSetsEqual(data1.EnvVars.Select(kvp => (kvp.Key, kvp.Value)).ToHashSet(), data1.EnvVars.Select(kvp => (kvp.Key, kvp.Value)).ToHashSet(), true);
            XAssert.AreSetsEqual(data1.FileDependencies, data2.FileDependencies, true, OperatingSystemHelper.PathComparer);
            XAssert.AreSetsEqual(data1.DirectoryDependencies, data2.DirectoryDependencies, true, OperatingSystemHelper.PathComparer);
            XAssert.AreSetsEqual(data1.OutputDirectories, data2.OutputDirectories, true, OperatingSystemHelper.PathComparer);
            XAssert.AreSetsEqual(data1.TempDirectories, data2.TempDirectories, true, OperatingSystemHelper.PathComparer);
            XAssert.AreSetsEqual(data1.UntrackedScopes, data2.UntrackedScopes, true, OperatingSystemHelper.PathComparer);
            XAssert.AreSetsEqual(data1.UntrackedPaths, data2.UntrackedPaths, true, OperatingSystemHelper.PathComparer);
        }

        private static void AssertEqualData(ModifiedRemoteData data1, ModifiedRemoteData data2)
        {
            if (data1 == data2)
            {
                return;
            }

            XAssert.ArePathEqual(data1.Executable, data2.Executable);
            XAssert.AreEqual(data1.Arguments, data2.Arguments);
            XAssert.AreEqual(data1.UsefulData1, data2.UsefulData1);
            XAssert.AreSetsEqual(data1.EnvVars.Select(kvp => (kvp.Key, kvp.Value)).ToHashSet(), data1.EnvVars.Select(kvp => (kvp.Key, kvp.Value)).ToHashSet(), true);
            XAssert.AreSetsEqual(data1.FileDependencies, data2.FileDependencies, true, OperatingSystemHelper.PathComparer);
            XAssert.AreSetsEqual(data1.UsefulData2, data2.UsefulData2, true, OperatingSystemHelper.PathComparer);
        }
    }
}
