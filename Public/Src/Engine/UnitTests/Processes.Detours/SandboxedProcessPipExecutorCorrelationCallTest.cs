// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Processes.Detours
{
    public sealed partial class SandboxedProcessPipExecutorTest
    {
        [Fact]
        public async Task CorrelateCopyFileAsync()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var sourceFile = tempFiles.GetFileName(pathTable, "SourceFile.txt");
                var destinationFile = tempFiles.GetFileName(pathTable, "DestinationFile.txt");
                WriteFile(pathTable, sourceFile, "content");

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CorrelateCopyFile",
                    inputFiles: ReadOnlyArray<FileArtifact>.FromWithoutCopy(FileArtifact.CreateSourceFile(sourceFile)),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(
                        FileArtifactWithAttributes.Create(
                            FileArtifact.CreateOutputFile(destinationFile), FileExistence.Required)),
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                var correlator = new Correlator(pathTable);
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreReparsePoints: false,
                    ignoreNonCreateFileReparsePoints: false,
                    monitorZwCreateOpenQueryFile: false,
                    context: context,
                    pip: process,
                    detoursListener: correlator,
                    errorString: out _);

                VerifyNormalSuccess(context, result);

                XAssert.IsTrue(File.Exists(destinationFile.ToString(pathTable)));

                var toVerify = new List<(AbsolutePath, RequestedAccess, FileAccessStatus)>
                {
                    (sourceFile, RequestedAccess.Read, FileAccessStatus.Allowed),
                    (destinationFile, RequestedAccess.Write, FileAccessStatus.Allowed)
                };

                VerifyFileAccesses(context, result.AllReportedFileAccesses, toVerify.ToArray());
                correlator.VerifyCorrelation(new Correlator.VerifiedCorrelation(
                    destinationFile.ToString(pathTable),
                    ReportedFileOperation.CopyFileDestination,
                    sourceFile.ToString(pathTable),
                    ReportedFileOperation.CopyFileSource));
            }
        }

        [Fact]
        public async Task CorrelateCreateHardLinkAsync()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var sourceFile = tempFiles.GetFileName(pathTable, "SourceFile.txt");
                var destinationFile = tempFiles.GetFileName(pathTable, "DestinationFile.txt");
                WriteFile(pathTable, sourceFile, "content");

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CorrelateCreateHardLink",
                    inputFiles: ReadOnlyArray<FileArtifact>.FromWithoutCopy(FileArtifact.CreateSourceFile(sourceFile)),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(
                        FileArtifactWithAttributes.Create(
                            FileArtifact.CreateOutputFile(destinationFile), FileExistence.Required)),
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                var correlator = new Correlator(pathTable);
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreReparsePoints: false,
                    ignoreNonCreateFileReparsePoints: false,
                    monitorZwCreateOpenQueryFile: false,
                    context: context,
                    pip: process,
                    detoursListener: correlator,
                    errorString: out _);

                VerifyNormalSuccess(context, result);

                XAssert.IsTrue(File.Exists(destinationFile.ToString(pathTable)));

                var toVerify = new List<(AbsolutePath, RequestedAccess, FileAccessStatus)>
                {
                    (sourceFile, RequestedAccess.Read, FileAccessStatus.Allowed),
                    (destinationFile, RequestedAccess.Write, FileAccessStatus.Allowed)
                };

                VerifyFileAccesses(context, result.AllReportedFileAccesses, toVerify.ToArray());
                correlator.VerifyCorrelation(new Correlator.VerifiedCorrelation(
                    destinationFile.ToString(pathTable),
                    ReportedFileOperation.CreateHardLinkDestination,
                    sourceFile.ToString(pathTable),
                    ReportedFileOperation.CreateHardLinkSource));
            }
        }

        [Fact]
        public async Task CorrelateMoveFileAsync()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                AbsolutePath sourceDirectory = tempFiles.GetDirectory(pathTable, "Source");
                AbsolutePath sourceFile = sourceDirectory.Combine(pathTable, "SourceFile.txt");
                var destinationFile = tempFiles.GetFileName(pathTable, "DestinationFile.txt");
                WriteFile(pathTable, sourceFile, "content");

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CorrelateMoveFile",
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(
                        FileArtifactWithAttributes.Create(
                            FileArtifact.CreateOutputFile(destinationFile), FileExistence.Required)),
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.FromWithoutCopy(sourceDirectory));

                var correlator = new Correlator(pathTable);
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreReparsePoints: false,
                    ignoreNonCreateFileReparsePoints: false,
                    monitorZwCreateOpenQueryFile: false,
                    context: context,
                    pip: process,
                    detoursListener: correlator,
                    errorString: out _);

                VerifyNormalSuccess(context, result);

                XAssert.IsTrue(File.Exists(destinationFile.ToString(pathTable)));

                var toVerify = new List<(AbsolutePath, RequestedAccess, FileAccessStatus)>
                {
                    (sourceFile, RequestedAccess.ReadWrite, FileAccessStatus.Allowed),
                    (destinationFile, RequestedAccess.Write, FileAccessStatus.Allowed)
                };

                VerifyFileAccesses(context, result.AllReportedFileAccesses, toVerify.ToArray());
                correlator.VerifyCorrelation(new Correlator.VerifiedCorrelation(
                    destinationFile.ToString(pathTable),
                    ReportedFileOperation.MoveFileWithProgressDest,
                    sourceFile.ToString(pathTable),
                    ReportedFileOperation.MoveFileWithProgressSource));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CorrelateMoveOrRenameDirectoryAsync(bool move)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                AbsolutePath workDirectory = tempFiles.GetDirectory(pathTable, "Directory");
                AbsolutePath sourceDirectory = tempFiles.GetDirectory(pathTable, workDirectory, "SourceDirectory");
                AbsolutePath sourceFile = sourceDirectory.Combine(pathTable, "SourceFile.txt");
                WriteFile(pathTable, sourceFile, "content");

                AbsolutePath destinationDirectory = workDirectory.Combine(pathTable, "DestinationDirectory");
                AbsolutePath destinationFile = destinationDirectory.Combine(pathTable, "SourceFile.txt");

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: move ? "CorrelateMoveDirectory" : "CorrelateRenameDirectory",
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.FromWithoutCopy(workDirectory));

                var correlator = new Correlator(pathTable);
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreReparsePoints: false,
                    ignoreNonCreateFileReparsePoints: false,
                    monitorZwCreateOpenQueryFile: false,
                    context: context,
                    pip: process,
                    detoursListener: correlator,
                    errorString: out _);

                VerifyNormalSuccess(context, result);

                XAssert.IsTrue(Directory.Exists(destinationDirectory.ToString(pathTable)));

                var toVerify = new List<(AbsolutePath, RequestedAccess, FileAccessStatus)>
                {
                    (sourceFile, RequestedAccess.Write, FileAccessStatus.Allowed),
                    (destinationFile, RequestedAccess.Write, FileAccessStatus.Allowed),
                    (sourceDirectory, RequestedAccess.Write, FileAccessStatus.Allowed),
                    (destinationDirectory, RequestedAccess.Write, FileAccessStatus.Allowed)
                };

                VerifyFileAccesses(context, result.AllReportedFileAccesses, toVerify.ToArray());
                correlator.VerifyCorrelation(
                    new Correlator.VerifiedCorrelation(
                        destinationFile.ToString(pathTable),
                        move ? ReportedFileOperation.MoveFileWithProgressDest : ReportedFileOperation.SetFileInformationByHandleDest,
                        sourceFile.ToString(pathTable),
                        move ? ReportedFileOperation.MoveFileWithProgressSource : ReportedFileOperation.SetFileInformationByHandleSource),
                    new Correlator.VerifiedCorrelation(
                        destinationDirectory.ToString(pathTable),
                        move ? ReportedFileOperation.MoveFileWithProgressDest : ReportedFileOperation.SetFileInformationByHandleDest,
                        sourceDirectory.ToString(pathTable),
                        move ? ReportedFileOperation.MoveFileWithProgressSource : ReportedFileOperation.SetFileInformationByHandleSource));
            }
        }

        public class Correlator : IDetoursEventListener
        {
            public bool HasCorrelatedFileOperations => !m_correlatedFileOperations.IsEmpty;

            private readonly ConcurrentDictionary<uint, FileAccessData> m_reportedFileAccesses = new ConcurrentDictionary<uint, FileAccessData>();
            private readonly ConcurrentQueue<(FileAccessData, FileAccessData)> m_correlatedFileOperations = new ConcurrentQueue<(FileAccessData, FileAccessData)>();

            private readonly PathTable m_pathTable;

            public Correlator(PathTable pathTable)
            {
                m_pathTable = pathTable;
                SetMessageHandlingFlags(GetMessageHandlingFlags() | MessageHandlingFlags.FileAccessNotify);
            }

            public override void HandleDebugMessage(DebugData debugData)
            {
            }

            public override void HandleFileAccess(FileAccessData fileAccessData)
            {
                // id must exist when report comes from Detours.
                XAssert.AreNotEqual(SandboxedProcessReports.FileAccessNoId, fileAccessData.Id);

                // id must be unique.
                XAssert.IsTrue(m_reportedFileAccesses.TryAdd(fileAccessData.Id, fileAccessData));

                if (fileAccessData.CorrelationId != SandboxedProcessReports.FileAccessNoId)
                {
                    // The correlated id must have been reported beforehand.
                    XAssert.IsTrue(m_reportedFileAccesses.TryGetValue(fileAccessData.CorrelationId, out var correlatedAccess));
                    m_correlatedFileOperations.Enqueue((correlatedAccess, fileAccessData));
                }
            }

            public override void HandleProcessData(ProcessData processData)
            {
            }

            public override void HandleProcessDetouringStatus(ProcessDetouringStatusData data)
            {
            }

            public void VerifyCorrelation(params VerifiedCorrelation[] correlationsToVerify)
            {
                if (!HasCorrelatedFileOperations)
                {
                    string allReportedOperations = string.Join(
                        Environment.NewLine,
                        m_reportedFileAccesses.Select(r => $"'{r.Value.Path}' ({r.Value.Operation}) id: {r.Value.Id}, correlation id: {r.Value.CorrelationId}"));
                    XAssert.Fail($"No correlated file accesses are found. Reported file accesses are {Environment.NewLine}{allReportedOperations}");
                }

                foreach (var correlationToVerify in correlationsToVerify)
                {
                    bool found = false;
                    foreach (var correlation in m_correlatedFileOperations)
                    {
                        FileAccessData otherAccess = default;

                        if (OperatingSystemHelper.PathComparer.Equals(correlationToVerify.File, correlation.Item1.Path)
                            && correlationToVerify.Operation == correlation.Item1.Operation)
                        {
                            found = true;
                            otherAccess = correlation.Item2;
                        }
                        else if (OperatingSystemHelper.PathComparer.Equals(correlationToVerify.File, correlation.Item2.Path)
                            && correlationToVerify.Operation == correlation.Item2.Operation)
                        {
                            found = true;
                            otherAccess = correlation.Item1;
                        }

                        if (found)
                        {
                            string otherPath = AbsolutePath.Create(m_pathTable, otherAccess.Path).ToString(m_pathTable); // To normalize the path from Nt prefixes.
                            XAssert.IsTrue(
                                OperatingSystemHelper.PathComparer.Equals(correlationToVerify.CorrelatedFile, otherPath),
                                $"Mismatched correlated file for '{correlationToVerify.File}' ({correlationToVerify.Operation}) {Environment.NewLine}Expected: '{correlationToVerify.CorrelatedFile}'. Actual: '{otherPath}'");
                            XAssert.AreEqual(
                                correlationToVerify.CorrelatedOperation,
                                otherAccess.Operation,
                                $"Mismatched operations for '{correlationToVerify.File}' ({correlationToVerify.Operation}) {Environment.NewLine}Expected: '{correlationToVerify.CorrelatedFile}' ({correlationToVerify.CorrelatedOperation}). Actual: '{otherAccess.Path}' ({otherAccess.Operation})");
                            break;
                        }
                    }

                    if (!found)
                    {
                        var allCorrelations = string.Join(
                            Environment.NewLine,
                            m_correlatedFileOperations.Select(c => $"[ '{c.Item1.Path}' ({c.Item1.Operation}), '{c.Item2.Path}' ({c.Item2.Operation}) ]"));
                        XAssert.Fail($"Correlation for '{correlationToVerify.File}' ({correlationToVerify.Operation}) not found {Environment.NewLine}{allCorrelations}");
                    }
                }
            }

            public struct VerifiedCorrelation
            {
                public readonly string File;
                public readonly ReportedFileOperation Operation;
                public readonly string CorrelatedFile;
                public readonly ReportedFileOperation CorrelatedOperation;

                public VerifiedCorrelation(
                    string file,
                    ReportedFileOperation operation,
                    string correlatedFile,
                    ReportedFileOperation correlatedOperation)
                {
                    File = file;
                    Operation = operation;
                    CorrelatedFile = correlatedFile;
                    CorrelatedOperation = correlatedOperation;
                }
            }
        }
    }
}
