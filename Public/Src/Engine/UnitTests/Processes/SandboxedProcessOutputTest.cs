// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
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
                XAssert.IsTrue(outputBuilder.HookOutputStream);
                outputBuilder.AppendLine(content);
                var output = outputBuilder.Freeze();
                XAssert.IsFalse(output.IsSaved);
                XAssert.IsFalse(File.Exists(fileName));
                XAssert.AreEqual(content + Environment.NewLine, await output.ReadValueAsync());
            }
        }

        [Fact]
        public async Task ObservedOutputWithNullStorage()
        {
            var content = new string('S', 100);
            string observedOutput = string.Empty;
            var outputBuilder =
                new SandboxedProcessOutputBuilder(
                    Encoding.UTF8,
                    0,
                    null,
                    SandboxedProcessFile.StandardOutput,
                    writtenOutput => observedOutput += writtenOutput);
            XAssert.IsTrue(outputBuilder.HookOutputStream);
            outputBuilder.AppendLine(content);
            SandboxedProcessOutput output = outputBuilder.Freeze();
            XAssert.IsFalse(output.IsSaved);
            XAssert.AreEqual(string.Empty, await output.ReadValueAsync());
            XAssert.AreEqual(content, observedOutput);
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
                XAssert.IsTrue(outputBuilder.HookOutputStream);
                outputBuilder.AppendLine(content);
                var output = outputBuilder.Freeze();
                XAssert.IsTrue(output.IsSaved);
                XAssert.AreEqual(fileName, output.FileName);
                XAssert.IsTrue(File.Exists(fileName));
                XAssert.AreEqual(content + Environment.NewLine, await output.ReadValueAsync());
            }
        }

        [Fact]
        public async Task ReadValueAsyncTruncates()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var storage = (ISandboxedProcessFileStorage)tempFiles;
                var fileName = storage.GetFileName(SandboxedProcessFile.StandardOutput);
                var outputBuilder =
                    new SandboxedProcessOutputBuilder(
                        Encoding.UTF8,
                        300,
                        tempFiles,
                        SandboxedProcessFile.StandardOutput,
                        null);
                XAssert.IsTrue(outputBuilder.HookOutputStream);
                
                for (int i = 0; i < 100; i++)
                {
                    outputBuilder.AppendLine(new string('a', 1000));
                }
                
                var output = outputBuilder.Freeze();
                var result = await output.ReadValueAsync(10_000);
                Assert.Equal(10_000, result.Length);
            }
        }

        [Fact]
        public void OutputPassThroughToParentConsole()
        {
            // Verify we can construct a builder with pass-through arguments.
            var outputBuilder =
                new SandboxedProcessOutputBuilder(
                    Encoding.UTF8,
                    maxMemoryLength: 0,
                    fileStorage: null,
                    file: SandboxedProcessFile.StandardOutput,
                    observer: null);
            XAssert.IsFalse(outputBuilder.HookOutputStream);
            SandboxedProcessOutput output = outputBuilder.Freeze();
            XAssert.IsNotNull(output);
            XAssert.IsTrue(output.HasLength);
            XAssert.IsFalse(output.HasException);
            XAssert.AreEqual(0, output.Length);
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
                    XAssert.IsTrue(outputBuilder.HookOutputStream);

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
