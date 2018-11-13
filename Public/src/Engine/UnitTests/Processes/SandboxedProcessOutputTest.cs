// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    public sealed class SandboxedProcessOutputTest : XunitBuildXLTest
    {
        public SandboxedProcessOutputTest(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public async Task OutputInMemory()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var storage = (ISandboxedProcessFileStorage)tempFiles;
                var fileName = storage.GetFileName(SandboxedProcessFile.StandardOutput);
                var content = new string('S', 100);
                var outputBuilder =
                    new SandboxedProcessOutputBuilder(
                        Encoding.UTF8,
                        content.Length + Environment.NewLine.Length,
                        tempFiles,
                        SandboxedProcessFile.StandardOutput,
                        null);
                outputBuilder.AppendLine(content);
                var output = outputBuilder.Freeze();
                XAssert.IsFalse(output.IsSaved);
                XAssert.IsFalse(File.Exists(fileName));
                XAssert.AreEqual(await output.ReadValueAsync(), content + Environment.NewLine);
            }
        }

        [Fact]
        public async Task OutputOnDisk()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var storage = (ISandboxedProcessFileStorage)tempFiles;
                var fileName = storage.GetFileName(SandboxedProcessFile.StandardOutput);
                var content = new string('S', 100);
                var outputBuilder =
                    new SandboxedProcessOutputBuilder(
                        Encoding.UTF8,
                        content.Length + Environment.NewLine.Length - 1,
                        tempFiles,
                        SandboxedProcessFile.StandardOutput,
                        null);
                outputBuilder.AppendLine(content);
                var output = outputBuilder.Freeze();
                XAssert.IsTrue(output.IsSaved);
                XAssert.AreEqual(fileName, output.FileName);
                XAssert.IsTrue(File.Exists(fileName));
                XAssert.AreEqual(await output.ReadValueAsync(), content + Environment.NewLine);
            }
        }

        // Skipping this test for *nix systems because there it is not true that a file cannot be deleted if another program has opened it first.
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ErrorHandling()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var storage = (ISandboxedProcessFileStorage)tempFiles;
                var fileName = storage.GetFileName(SandboxedProcessFile.StandardOutput);
                SandboxedProcessOutput output;
                using (var fileStream = FileUtilities.CreateFileStream(fileName, FileMode.CreateNew, FileAccess.Write, FileShare.None, allowExcludeFileShareDelete: true))
                {
                    var content = new string('S', 100);
                    var outputBuilder =
                        new SandboxedProcessOutputBuilder(Encoding.UTF8, content.Length / 2, tempFiles, SandboxedProcessFile.StandardOutput, null);

                    // NOTE: this only holds on Windows
                    // The specified content plus a NewLine will exceed the max memory length.
                    // Thus, the output builder will try to write the content to a file.
                    // However, the file is not writable (this runs in a using clause that already opened the file).
                    // Thus, this will internally fail, but not yet throw an exception.
                    outputBuilder.AppendLine(content);
                    output = outputBuilder.Freeze();
                    XAssert.IsTrue(output.HasException);
                }

                await Assert.ThrowsAsync<BuildXLException>(() => output.ReadValueAsync());
                await Assert.ThrowsAsync<BuildXLException>(() => output.SaveAsync());
                Assert.Throws<BuildXLException>(() => output.CreateReader());
            }
        }
    }
}
