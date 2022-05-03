// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using Test.BuildXL.Scheduler;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.RuntimeAnalyzer
{
    public class DumpPipLiteAnalysisUtilitiesTests : PipTestBase
    {
        private readonly string m_logPath;

        public DumpPipLiteAnalysisUtilitiesTests(ITestOutputHelper output) : base(output)
        {
            m_logPath = GetFullPath("Logs");
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
        }

        [Fact]
        public void TestCopyFileDump()
        {
            var sourceArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(Context.PathTable, GetFullPath("source")));
            var destinationArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(Context.PathTable, GetFullPath("destination")));

            CopyFile pip = CreateCopyFile(sourceArtifact, destinationArtifact);
            PipGraphBuilder.AddCopyFile(pip);

            RunAndAssertDumpPip(pip);
        }

        [Fact]
        public void TestProcessDump()
        {
            var sourceFile = CreateSourceFile();
            var outputFile = CreateOutputFileArtifact();

            Process process = CreateCmdProcess(dependencies: new[] { sourceFile }, outputs: new[] { outputFile });
            PipGraphBuilder.AddProcess(process);

            RunAndAssertDumpPip(process);

            // Test a few empty list fields to ensure they did not get printed out
            var pipWrittenToFile = Encoding.UTF8.GetString(File.ReadAllBytes(GetDumpFilePath(process)));
            // This pip does not have any of the following fields set (they will have size 0 lists)
            Assert.False(pipWrittenToFile.Contains("Tags"));
            Assert.False(pipWrittenToFile.Contains("Environment Variables")); 
            Assert.False(pipWrittenToFile.Contains("Retry Exit Codes"));
        }

        [Fact]
        public void TestModulePipDump()
        {
            AbsolutePath specPath = CreateUniqueSourcePath();
            ModulePip module = ModulePip.CreateForTesting(Context.StringTable, specPath);

            PipGraphBuilder.AddModule(module);

            RunAndAssertDumpPip(module);
        }

        [Fact]
        public void TestSealDirectoryDump()
        {
            var sealPath = CreateOutputDirectoryArtifact(TemporaryDirectory);
            SealDirectory sealDirectory = CreateSealDirectory(sealPath.Path, SealDirectoryKind.Full, scrub: true, new[] { CreateSourceFile(), CreateSourceFile(), CreateSourceFile() });
            sealDirectory.SetDirectoryArtifact(sealPath);

            PipGraphBuilder.AddSealDirectory(sealDirectory);

            RunAndAssertDumpPip(sealDirectory);
        }

        [Fact]
        public void TestWriteFileDump()
        {
            FileArtifact outputFile = CreateOutputFileArtifact();
            WriteFile pip = CreateWriteFile(outputFile, string.Empty, new[] { "some content" });

            PipGraphBuilder.AddWriteFile(pip);

            RunAndAssertDumpPip(pip);
        }

        [Fact]
        public void TestBadPath()
        {
            FileArtifact outputFile = CreateOutputFileArtifact();
            WriteFile pip = CreateWriteFile(outputFile, string.Empty, new[] { "some content" });

            PipGraphBuilder.AddWriteFile(pip);
            var graph = PipGraphBuilder.Build();

            var success = DumpPipLiteAnalysisUtilities.DumpPip(pip, @"X:\not\a\real\path\", Context.PathTable, Context.StringTable, Context.SymbolTable, graph, LoggingContext);

            Assert.False(success);
            AssertWarningEventLogged(LogEventId.DumpPipLiteUnableToSerializePipDueToBadPath);
        }

        #region HelperFunctions
        private bool CreateLogPathAndRun(Pip pip, PipGraph graph)
        {
            DumpPipLiteAnalysisUtilities.CreateLoggingDirectory(m_logPath, LoggingContext);
            return DumpPipLiteAnalysisUtilities.DumpPip(pip, m_logPath, Context.PathTable, Context.StringTable, Context.SymbolTable, graph, LoggingContext);
        }

        private string GetDumpFilePath(Pip pip)
        {
            return Path.Combine(m_logPath, $"{pip.FormattedSemiStableHash}.json");
        }

        private bool VerifyContentWrittenToFile(Pip pip, PipGraph graph)
        {
            var success = true;
            var serializedPip = DumpPipLiteAnalysisUtilities.CreateObjectForSerialization(pip, dynamicData: null, Context.PathTable, Context.StringTable, Context.SymbolTable, graph);
            var pipWrittenToFile = File.ReadAllBytes(GetDumpFilePath(pip));
            var serializerOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
#if NET5_0_OR_GREATER
// .NET 5 and 6 have a different way of dealing with null values
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
#else
                IgnoreNullValues = true,
#endif

            };

            var dumpContents = JsonSerializer.SerializeToUtf8Bytes(serializedPip, serializerOptions);
            success &= pipWrittenToFile.SequenceEqual(dumpContents);

            // Verify some random data written to the file
            var dumpString = Encoding.UTF8.GetString(pipWrittenToFile);

            success &= dumpString.Contains(pip.PipId.Value.ToString(CultureInfo.InvariantCulture) + " (" + pip.PipId.Value.ToString("X16", CultureInfo.InvariantCulture) + ")");
            success &= dumpString.Contains(pip.PipType.ToString());

            return success;
        }

        private void AssertCommon(bool success, Pip pip, PipGraph graph)
        {
            Assert.True(success);
            Assert.True(File.Exists(GetDumpFilePath(pip)));
            Assert.True(VerifyContentWrittenToFile(pip, graph));
        }

        private void RunAndAssertDumpPip(Pip pip)
        {
            var graph = PipGraphBuilder.Build();
            var success = CreateLogPathAndRun(pip, graph);

            AssertCommon(success, pip, graph);
        }
        #endregion HelperFunctions
    }
}
