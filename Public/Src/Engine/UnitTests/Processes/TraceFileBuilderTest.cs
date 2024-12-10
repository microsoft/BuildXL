// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Processes;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;
using Xunit;
using System.IO;
using System.Collections.Generic;
using System;

namespace Test.BuildXL.Processes
{
    public class TraceFileBuilderTest : XunitBuildXLTest
    {
        private readonly PathTable m_pathTable;
        private readonly ISandboxedProcessFileStorage m_fileStorage;

        public TraceFileBuilderTest(ITestOutputHelper output) : base(output)
        {
            m_pathTable = new PathTable();
            m_fileStorage = new TraceBuilderFileStorage(TestOutputDirectory);
        }

        [Fact]
        public void TestBasic()
        {
            var builder = CreateBuilder();
            var path = GetPath(X("/c/foo/bar/file.txt"));
            builder.ReportFileAccess(1, ReportedFileOperation.CreateFile, RequestedAccess.Write, path, 0, false, null);
            builder.ReportFileAccess(1, ReportedFileOperation.CreateFile, RequestedAccess.Write, AbsolutePath.Invalid, 1, false, null);
            builder.ReportProcess(new ReportedProcess(1, X("/c/cmd.exe"), "/c foo") { ExitCode = 0 });
            builder.ReportProcess(new ReportedProcess(2, X("/c/ps.exe"), "-Test bar") { ExitCode = 1 });

            XAssert.AreEqual(2, builder.OperationCount);
            XAssert.AreEqual(2, builder.ReportedProcessCount);

            var (version, operations, reportedProcesses) = SerializeAndDeserialize(builder);

            XAssert.AreEqual(1, version);
            XAssert.AreEqual(2, operations.Count);
            XAssert.AreEqual(ReportedFileOperation.CreateFile, operations[0].FileOperation);
            XAssert.AreEqual(path, operations[0].Path);
            XAssert.AreEqual(AbsolutePath.Invalid, operations[1].Path);

            XAssert.AreEqual(2, reportedProcesses.Count);
            XAssert.AreEqual((uint)1, reportedProcesses[0].ProcessId);
            XAssert.AreEqual(X("/c/cmd.exe"), reportedProcesses[0].Path);
        }

        private SandboxedProcessTraceBuilder CreateBuilder() => new(m_fileStorage, m_pathTable);
        private AbsolutePath GetPath(string path) => AbsolutePath.Create(m_pathTable, path);

        private (byte version, List<SandboxedProcessTraceBuilder.Operation>, List<ReportedProcess>) SerializeAndDeserialize(SandboxedProcessTraceBuilder builder)
        {
            using var stream = new MemoryStream();

            using (var writer = new StreamWriter(stream, encoding: System.Text.Encoding.UTF8, bufferSize: 4096, leaveOpen: true))
            {
                builder.WriteToStream(writer);
            }

            stream.Position = 0;
            using (var reader = new StreamReader(stream, encoding: System.Text.Encoding.UTF8))
            {
                return SandboxedProcessTraceBuilder.ReadFromStream(reader, m_pathTable);
            }
        }

        private class TraceBuilderFileStorage : ISandboxedProcessFileStorage
        {
            private readonly string m_storageDirectory;

            public TraceBuilderFileStorage(string storageDirectory)
            {
                m_storageDirectory = storageDirectory;
            }

            public string GetFileName(SandboxedProcessFile file) => Path.Combine(m_storageDirectory, GetName(file));

            private static string GetName(SandboxedProcessFile file) =>
                file switch
                {
                    SandboxedProcessFile.StandardError => "stderr",
                    SandboxedProcessFile.StandardOutput => "stdout",
                    SandboxedProcessFile.Trace => "trace",
                    _ => throw new ArgumentOutOfRangeException(nameof(file))
                };
        }
    }
}
