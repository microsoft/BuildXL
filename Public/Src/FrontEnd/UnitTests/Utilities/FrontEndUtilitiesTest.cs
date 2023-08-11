// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities;
using BuildXL.Processes;
using BuildXL.ProcessPipExecutor;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.Processes;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Utilities
{
    public class FrontEndUtilitiesTest : TemporaryStorageTestBase
    {
        protected BuildXLContext Context { get; } 
        protected FrontEndContext FrontEndContext { get; }
        protected IConfiguration Configuration { get; }
        protected PipEnvironment PipEnvironment { get; }

        public FrontEndUtilitiesTest(ITestOutputHelper output) : base(output)
        {
            Configuration = new ConfigurationImpl();
            Context = BuildXLContext.CreateInstanceForTesting();
            FrontEndContext = FrontEndContext.CreateInstanceForTesting(
                pathTable: Context.PathTable, symbolTable: Context.SymbolTable, qualifierTable: Context.QualifierTable, frontEndConfig: Configuration.FrontEnd, loggingContext: LoggingContext);
            PipEnvironment = new PipEnvironment(LoggingContext);
        }

        [Fact]
        public void RunSandboxedToolHasAllExpectedAccesses()
        {
            var input = GetFullPath("input");
            var inputPath = AbsolutePath.Create(Context.PathTable, input);
            File.WriteAllText(input, "some input");

            var output = GetFullPath("output");
            var outputPath = AbsolutePath.Create(Context.PathTable, output);

            var temporaryDirectoryPath = AbsolutePath.Create(Context.PathTable, TemporaryDirectory);
            var nonExistentPath = AbsolutePath.Create(Context.PathTable, GetFullPath("non-existent"));

            string toolArguments;

            // Let's test a read, an enumeration, a write and an absent probe
            if (OperatingSystemHelper.IsWindowsOS)
            {
                toolArguments = "/C \"type input & dir & copy input output & if exist non-existent echo hi\"";
            }
            else
            {
                toolArguments = "-c \"cat input; ls; cp input output; if [ -e non-existent ]; then echo hi; fi\"";
            }

            var toolDirectory = AbsolutePath.Create(Context.PathTable, CmdHelper.OsShellExe).GetParent(Context.PathTable);

            var result = FrontEndUtilities.RunSandboxedToolAsync(
               FrontEndContext,
               CmdHelper.OsShellExe,
               buildStorageDirectory: TemporaryDirectory,
               fileAccessManifest: FrontEndUtilities.GenerateToolFileAccessManifest(FrontEndContext, toolDirectory),
               arguments: toolArguments,
               workingDirectory: TemporaryDirectory,
               description: $"Test sandboxed tool",
               PipEnvironment.GetBaseEnvironmentVariables()).GetAwaiter().GetResult();

            XAssert.AreEqual(0, result.ExitCode);
            var allAccesses = result.AllUnexpectedFileAccesses.Union(result.ExplicitlyReportedFileAccesses);

            // type input
            XAssert.IsTrue(allAccesses.Any(a => a.RequestedAccess == RequestedAccess.Read && GetAbsolutePath(a) == inputPath));
            // dir
            XAssert.IsTrue(allAccesses.Any(a => a.RequestedAccess == RequestedAccess.Enumerate && GetAbsolutePath(a) == temporaryDirectoryPath));
            // copy input output
            XAssert.IsTrue(allAccesses.Any(a => a.RequestedAccess == RequestedAccess.Write && GetAbsolutePath(a) == outputPath));
            // if exist non-existent echo hi
            XAssert.IsTrue(allAccesses.Any(a => a.RequestedAccess == RequestedAccess.Probe && GetAbsolutePath(a) == nonExistentPath));
        }

        private AbsolutePath GetAbsolutePath(ReportedFileAccess access) => AbsolutePath.Create(Context.PathTable, access.GetPath(Context.PathTable));
    }
}
