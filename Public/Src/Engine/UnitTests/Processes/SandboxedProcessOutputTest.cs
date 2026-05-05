// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

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

        /// <summary>
        /// Stress test for the AppendLine / Freeze race fixed by the latch-and-drain
        /// concurrency model in <see cref="SandboxedProcessOutputBuilder"/>.
        ///
        /// Reproduces the production race that crashed QBuilder under
        /// failure_hash {57f3196a-e0f1-7a58-bd94-1a53486ad22d}: an in-flight
        /// pipe-reader callback running <c>AppendLine</c> concurrently with the
        /// process-exit thread running <c>Freeze</c>. With <see cref="MaxLengthInMemory"/> = 0
        /// (the QB Tracker pattern), every <c>AppendLine</c> trips the spill-to-file branch,
        /// keeping the racing window permanently open.
        ///
        /// The test repeats the race many times. Each iteration spawns a producer
        /// hammering <c>AppendLine</c> and a closer that calls <c>Freeze</c> at a
        /// random point. The constructed <see cref="SandboxedProcessOutput"/> must
        /// satisfy the ctor invariant
        /// <c>exception != null ^ (value != null ^ fileName != null)</c> for every
        /// iteration; if it does not, the ctor would throw <c>Contract.AssertFailure</c>.
        /// </summary>
        [Fact(Skip = "Stress test — kept for manual / diagnostic use; nondeterministic timing makes it a poor fit for routine CI runs")]
        public void AppendLineFreezeRaceStress()
        {
            const int Iterations = 200;
            const int LinesPerIteration = 100;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: Path.Combine(Path.GetTempPath(), "AppendLineFreezeRaceStress_" + Guid.NewGuid())))
            {
                for (int iteration = 0; iteration < Iterations; iteration++)
                {
                    using var storage = new SingleFileStorage(Path.Combine(tempFiles.RootDirectory, $"iter{iteration}.txt"));

                    // QB-style configuration: maxMemoryLength = 0 means every AppendLine
                    // beyond the first trips the spill-to-file branch (the racing one).
                    var outputBuilder = new SandboxedProcessOutputBuilder(
                        Encoding.UTF8,
                        maxMemoryLength: 0,
                        fileStorage: storage,
                        file: SandboxedProcessFile.StandardOutput,
                        observer: null);

                    using var ready = new ManualResetEventSlim(false);
                    using var go = new ManualResetEventSlim(false);

                    var producer = Task.Run(() =>
                    {
                        ready.Set();
                        go.Wait();
                        for (int i = 0; i < LinesPerIteration; i++)
                        {
                            outputBuilder.AppendLine("x");
                        }
                    });

                    SandboxedProcessOutput output = null;
                    var closer = Task.Run(() =>
                    {
                        ready.Wait();
                        go.Set();

                        // Spin a few cycles so the producer reaches the spill path
                        // before Freeze runs. Cap the budget at 15 to stay in
                        // SpinWait's CPU-spin range; higher counts trigger
                        // Thread.Sleep(1) which slows the test substantially without
                        // adding meaningful race coverage.
                        var spin = new SpinWait();
                        int budget = (iteration % 16);
                        while (budget-- > 0)
                        {
                            spin.SpinOnce();
                        }

                        output = outputBuilder.Freeze();
                    });

                    // Wait for both. If the ctor invariant fired, Freeze would have thrown
                    // and Task.WaitAll would surface that.
                    Task.WaitAll(producer, closer);

                    XAssert.IsNotNull(output, $"iteration {iteration}: Freeze did not produce an output");

                    // Sanity check the ctor invariant directly. If the production code path
                    // ever produces an output that violates it, that's the bug we're guarding
                    // against. The ctor itself would have failed before reaching here, but
                    // double-check via reflection so we get a clean assertion.
                    AssertOutputInvariant(output, iteration);
                }
            }
        }

        /// <summary>
        /// Boundary-case stress: spill threshold is non-zero so the spill-to-file
        /// transition only happens once per iteration, but the race window is still
        /// active around that exact transition. Confirms the latch-and-drain
        /// handles the "spill happening concurrently with Freeze" case specifically.
        /// </summary>
        [Fact(Skip = "Stress test — kept for manual / diagnostic use; nondeterministic timing makes it a poor fit for routine CI runs")]
        public void AppendLineFreezeRaceStressAtSpillBoundary()
        {
            const int Iterations = 200;
            const int LinesBeforeSpill = 8;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: Path.Combine(Path.GetTempPath(), "AppendLineFreezeRaceStressBoundary_" + Guid.NewGuid())))
            {
                int spillObserved = 0;
                int inMemoryObserved = 0;

                for (int iteration = 0; iteration < Iterations; iteration++)
                {
                    using var storage = new SingleFileStorage(Path.Combine(tempFiles.RootDirectory, $"boundary{iteration}.txt"));

                    var line = new string('a', 64);

                    // Pick a maxMemoryLength so the spill trips after exactly LinesBeforeSpill
                    // appends. Concurrency probes the moment the threshold is crossed.
                    int maxMemoryLength = (line.Length + Environment.NewLine.Length) * LinesBeforeSpill;

                    var outputBuilder = new SandboxedProcessOutputBuilder(
                        Encoding.UTF8,
                        maxMemoryLength: maxMemoryLength,
                        fileStorage: storage,
                        file: SandboxedProcessFile.StandardOutput,
                        observer: null);

                    using var ready = new ManualResetEventSlim(false);
                    using var go = new ManualResetEventSlim(false);

                    var producer = Task.Run(() =>
                    {
                        ready.Set();
                        go.Wait();
                        for (int i = 0; i < LinesBeforeSpill * 4; i++)
                        {
                            outputBuilder.AppendLine(line);
                        }
                    });

                    SandboxedProcessOutput output = null;
                    var closer = Task.Run(() =>
                    {
                        ready.Wait();
                        go.Set();

                        // Vary the spin budget across iterations to probe both pre- and
                        // post-spill timings. Cap at 15 to stay in SpinWait's CPU-spin
                        // range (no Thread.Sleep(1)).
                        var spin = new SpinWait();
                        int budget = ((iteration * 7) % 16);
                        while (budget-- > 0)
                        {
                            spin.SpinOnce();
                        }

                        output = outputBuilder.Freeze();
                    });

                    Task.WaitAll(producer, closer);

                    XAssert.IsNotNull(output, $"iteration {iteration}: Freeze did not produce an output");
                    AssertOutputInvariant(output, iteration);

                    if (output.IsSaved)
                    {
                        spillObserved++;
                    }
                    else if (!output.HasException)
                    {
                        inMemoryObserved++;
                    }
                }

                // Both branches should be exercised across iterations. If we never
                // hit one, the test isn't actually probing the boundary.
                XAssert.IsTrue(spillObserved > 0, $"spill branch never exercised across {Iterations} iterations");
                XAssert.IsTrue(inMemoryObserved > 0, $"in-memory branch never exercised across {Iterations} iterations");
            }
        }

        private static void AssertOutputInvariant(SandboxedProcessOutput output, int iteration)
        {
            // exception != null ^ (value != null ^ fileName != null) — i.e. exactly one
            // of {exception, value, fileName} is non-null.
            bool hasException = output.HasException;

            // m_value and m_fileName are private; inspect via reflection for the test.
            var type = typeof(SandboxedProcessOutput);
            var valueField = type.GetField("m_value", BindingFlags.Instance | BindingFlags.NonPublic);
            var fileNameField = type.GetField("m_fileName", BindingFlags.Instance | BindingFlags.NonPublic);
            XAssert.IsNotNull(valueField);
            XAssert.IsNotNull(fileNameField);

            string value = (string)valueField.GetValue(output);
            string fileName = (string)fileNameField.GetValue(output);

            bool hasValue = value != null;
            bool hasFileName = fileName != null;

            // Exactly one of {exception, value, fileName} is set.
            int populatedCount = (hasException ? 1 : 0) + (hasValue ? 1 : 0) + (hasFileName ? 1 : 0);
            XAssert.AreEqual(
                1,
                populatedCount,
                $"iteration {iteration}: ctor invariant violated. hasException={hasException}, hasValue={hasValue} (length={value?.Length}), hasFileName={hasFileName}");
        }

        /// <summary>
        /// Minimal <see cref="ISandboxedProcessFileStorage"/> for stress tests that need
        /// a stable per-iteration file name without TempFileStorage's shared-state
        /// bookkeeping (which is itself not thread-safe).
        /// </summary>
        private sealed class SingleFileStorage : ISandboxedProcessFileStorage, IDisposable
        {
            private readonly string m_path;

            public SingleFileStorage(string path)
            {
                m_path = path;
                Directory.CreateDirectory(Path.GetDirectoryName(m_path)!);
            }

            public string GetFileName(SandboxedProcessFile file) => m_path;

            public void Dispose()
            {
                try
                {
                    if (File.Exists(m_path))
                    {
                        File.Delete(m_path);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
