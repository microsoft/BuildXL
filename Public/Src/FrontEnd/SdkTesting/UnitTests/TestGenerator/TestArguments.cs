// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.FrontEnd.Script.Testing.TestGenerator;
using BuildXL.Utilities;
using Xunit;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.BuildXL.FrontEnd.Script.Testing.TestGenerator
{
    public sealed class TestArguments
    {
        [Fact]
        public void ParseAllArguments()
        {
            var args = new Args(new[]
                {
                    "/o:folder",
                    "/t:file1.dsc",
                    "/t:file2.dsc",
                    "/s:sdk1",
                    "/s:sdk2",
                    "/l:" + R("file1", "test1.lkg"),
                    "/l:" + R("file1", "lkg2.lkg"),
                    "/l:" + R("file2", "lkg3"),
                });

            // Check /outputFolder:
            Assert.EndsWith(Path.DirectorySeparatorChar + "folder", args.OutputFolder);

            // Check /testFile:
            Assert.Equal(2, args.TestFiles.Count);
            Assert.EndsWith(Path.DirectorySeparatorChar + "file1.dsc", args.TestFiles[0]);
            Assert.EndsWith(Path.DirectorySeparatorChar + "file2.dsc", args.TestFiles[1]);

            // Check /sdksToTest:
            Assert.Equal(2, args.SdksToTest.Count);
            Assert.EndsWith(Path.DirectorySeparatorChar + "sdk1", args.SdksToTest[0]);
            Assert.EndsWith(Path.DirectorySeparatorChar + "sdk2", args.SdksToTest[1]);

            // Check /lkgFiles:
            string path;
            bool found;
            Assert.Equal(3, args.LkgFiles.Count);

            // Check LkgFile 1
            found = args.LkgFiles.TryGetValue("file1#test1", out path);
            Assert.True(found);
            Assert.EndsWith(Path.DirectorySeparatorChar + R("file1", "test1.lkg"), path);

            // Check LkgFile 2
            found = args.LkgFiles.TryGetValue("file1#LKG2", out path);
            Assert.True(found, "Should be case insensitive");
            Assert.EndsWith(Path.DirectorySeparatorChar + R("file1", "lkg2.lkg"), path);

            // Check LkgFile 3
            found = args.LkgFiles.TryGetValue("file2#lkg3", out path);
            Assert.True(found, "Shouldn't matter which extension");
            Assert.EndsWith(Path.DirectorySeparatorChar + R("file2", "lkg3"), path);
        }

        [Fact]
        public void DisallowDuplicateLkg()
        {
            try
            {
                var args = new Args(new[]
                                {
                                    "/o:folder",
                                    "/t:file1.dsc",
                                    "/l:lkg1.lkg",
                                    "/l:lkg1.other",
                                });
                Assert.True(false, "Expected exception");
            }
            catch (Exception e)
            {
#pragma warning disable EPC12 // Suspicious exception handling: only Message property is observed in exception block.
                Assert.True(e.Message.Contains("Duplicate LkgFile defined"));
                Assert.True(e.Message.Contains("lkg1.lkg"));
                Assert.True(e.Message.Contains("lkg1.other"));
#pragma warning restore EPC12 // Suspicious exception handling: only Message property is observed in exception block.
            }
        }

        [Fact]
        public void RequiredOutput()
        {
            try
            {
                var args = new Args(new[]
                                {
                                    "/t:file1.dsc",
                                });
                Assert.True(false, "Expected exception");
            }
            catch (Exception e)
            {
                Assert.Contains("OutputFolder parameter is required", e.GetLogEventMessage());
            }
        }

        [Fact]
        public void AtLeastOneTestFile()
        {
            try
            {
                var args = new Args(new[]
                                {
                                    "/o:file1.dsc",
                                });
                Assert.True(false, "Expected exception");
            }
            catch (Exception e)
            {
                Assert.Contains("At least one", e.GetLogEventMessage());
            }
        }
    }
}
