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
using System.Linq;

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
            builder.ReportFileAccess(1, ReportedFileOperation.CreateFile, RequestedAccess.Write, path, 0, false, null, 123, 7, FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL, FlagsAndAttributes.FILE_ATTRIBUTE_TEMPORARY);
            builder.ReportFileAccess(1, ReportedFileOperation.CreateFile, RequestedAccess.Write, AbsolutePath.Invalid, 1, false, null, 456, 42, FlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY, FlagsAndAttributes.FILE_ATTRIBUTE_REPARSE_POINT);
            builder.ReportProcess(new ReportedProcess(1, X("/c/cmd.exe"), "/c foo") { ExitCode = 0 });
            builder.ReportProcess(new ReportedProcess(2, X("/c/ps.exe"), "-Test bar") { ExitCode = 1 });

            XAssert.AreEqual(2, builder.OperationCount);
            XAssert.AreEqual(2, builder.ReportedProcessCount);

            var (version, operations, reportedProcesses) = SerializeAndDeserialize(builder);

            XAssert.AreEqual(3, version);
            XAssert.AreEqual(2, operations.Count);
            XAssert.AreEqual(ReportedFileOperation.CreateFile, operations[0].FileOperation);
            XAssert.AreEqual(path, operations[0].Path);
            XAssert.AreEqual(AbsolutePath.Invalid, operations[1].Path);

            XAssert.AreEqual(2, reportedProcesses.Count);
            XAssert.AreEqual((uint)1, reportedProcesses[0].ProcessId);
            XAssert.AreEqual(X("/c/cmd.exe"), reportedProcesses[0].Path);

            XAssert.AreEqual((uint)123, operations[0].ReportedFileAccessId);
            XAssert.AreEqual((uint)7, operations[0].ReportedFileAccessCorrelationId);
            XAssert.AreEqual(FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL, operations[0].FlagsAndAttributes);
            XAssert.AreEqual(FlagsAndAttributes.FILE_ATTRIBUTE_TEMPORARY, operations[0].OpenedFileOrDirectoryAttributes);

            XAssert.AreEqual((uint)456, operations[1].ReportedFileAccessId);
            XAssert.AreEqual((uint)42, operations[1].ReportedFileAccessCorrelationId);
            XAssert.AreEqual(FlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY, operations[1].FlagsAndAttributes);
            XAssert.AreEqual(FlagsAndAttributes.FILE_ATTRIBUTE_REPARSE_POINT, operations[1].OpenedFileOrDirectoryAttributes);
        }

        [Fact]
        public void TestFindingMatchingOperations()
        {
            var builder = CreateBuilder();
            var path = GetPath(X("/c/foo/bar/file.txt"));
            var copySourcePath1 = GetPath(X("/c/copySource1.txt"));
            var copyDestPath1 = GetPath(X("/c/copyDest1.txt"));
            var copySourcePath2 = GetPath(X("/c/copySource2.txt"));
            var copyDestPath2 = GetPath(X("/c/copyDest2.txt"));

            builder.ReportFileAccess(1, ReportedFileOperation.CreateFile, RequestedAccess.Write, path, 0, false, null, 123, 0, 0, 0);
            builder.ReportFileAccess(1, ReportedFileOperation.CopyFileSource, RequestedAccess.Write, copySourcePath1, 0, false, null, 456, 0, 0, 0);
            builder.ReportFileAccess(2, ReportedFileOperation.CopyFileSource, RequestedAccess.Write, copySourcePath2, 0, false, null, 666, 0, 0, 0);
            builder.ReportFileAccess(3, ReportedFileOperation.CreateFile, RequestedAccess.Write, path, 0, false, null, 777, 0, 0, 0);
            builder.ReportFileAccess(2, ReportedFileOperation.CopyFileDestination, RequestedAccess.Write, copyDestPath2, 0, false, null, 667, 666, 0, 0);
            builder.ReportFileAccess(2, ReportedFileOperation.CopyFileDestination, RequestedAccess.Write, copyDestPath1, 0, false, null, 457, 456, 0, 0);
            builder.ReportFileAccess(3, ReportedFileOperation.CreateFile, RequestedAccess.Write, path, 0, false, null, 778, 0, 0, 0);

            builder.ReportProcess(new ReportedProcess(1, X("/c/cmd.exe"), "/c foo") { ExitCode = 0 });
            builder.ReportProcess(new ReportedProcess(2, X("/c/ps.exe"), "-Test bar") { ExitCode = 0 });
            builder.ReportProcess(new ReportedProcess(3, X("/c/ps.exe"), "-Test foo") { ExitCode = 0 });

            var (version, operations, reportedProcesses) = SerializeAndDeserialize(builder);

            var idMaps = operations.ToDictionary(op => (op.ProcessId, op.ReportedFileAccessId), op => op.Id);

            // Find matching operation for copyDestPath1
            var opId = idMaps[(1, 456)];
            var matchingOp = operations[(int)opId];
            XAssert.AreEqual(opId, matchingOp.Id);
            XAssert.AreEqual(ReportedFileOperation.CopyFileSource, matchingOp.FileOperation);
            XAssert.AreEqual(copySourcePath1, matchingOp.Path);

            // Find matching operation for copyDestPath2
            opId = idMaps[(2, 666)];
            matchingOp = operations[(int)opId];
            XAssert.AreEqual(opId, matchingOp.Id);
            XAssert.AreEqual(ReportedFileOperation.CopyFileSource, matchingOp.FileOperation);
            XAssert.AreEqual(copySourcePath2, matchingOp.Path);
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
