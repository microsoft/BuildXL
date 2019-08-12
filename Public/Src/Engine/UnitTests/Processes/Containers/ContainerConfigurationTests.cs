// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes.Containers
{
    [Trait("Category", "WindowsOSOnly")]
    public class ContainerConfigurationTests : TemporaryStorageTestBase
    {
        private readonly string m_testProcessPath;
        protected const string TestProcessToolName = "Test.BuildXL.Executables.TestProcess.exe";

        protected AbsolutePath TemporaryDirectoryPath { get; }

        protected PathTable PathTable { get; }

        public ContainerConfigurationTests(ITestOutputHelper output)
            : base(output)
        {
            PathTable = new PathTable();
            m_testProcessPath = Path.Combine(TestDeploymentDir, TestProcessToolName);
        }

        [FactIfSupported(requiresHeliumDriversAvailable: true)]
        public void WritesAreRedirected()
        {
            CreateSourceAndDestinationDirectories(out string sourceDirectory, out string destinationDirectory);

            var destinationFile = Path.Combine(sourceDirectory, "output.txt");

            var operations = new Operation[] { Operation.WriteFile(FileArtifact.CreateOutputFile(AbsolutePath.Create(PathTable, destinationFile))) };

            var containerConfiguration = ContainerConfiguration.CreateConfigurationForTesting(PathTable, new[] { (sourceDirectory, destinationDirectory) });

            RunOperationsInContainerAsync(operations, containerConfiguration).GetAwaiter().GetResult();

            // The real file should have been produced in the destination dir
            var realDestinationFile = Path.Combine(destinationDirectory, "output.txt");
            XAssert.IsTrue(File.Exists(realDestinationFile));
        }

        [FactIfSupported(requiresHeliumDriversAvailable: true)]
        public void ReadsAreVirtualized()
        {
            CreateSourceAndDestinationDirectories(out string sourceDirectory, out string destinationDirectory);

            var inputFile = Path.Combine(sourceDirectory, "input.txt");
            File.WriteAllText(inputFile, "input");

            var operations = new Operation[] { Operation.ReadFile(FileArtifact.CreateSourceFile(AbsolutePath.Create(PathTable, inputFile))) };

            var containerConfiguration = ContainerConfiguration.CreateConfigurationForTesting(PathTable, new[] { (sourceDirectory, destinationDirectory) });

            // The operation should succeed, meaning the expected input file was there
            RunOperationsInContainerAsync(operations, containerConfiguration).GetAwaiter().GetResult();
        }

        [FactIfSupported(requiresHeliumDriversAvailable: true)]
        public void WritesOnInputsImpliesCopyOnWrite()
        {
            CreateSourceAndDestinationDirectories(out string sourceDirectory, out string destinationDirectory);

            var inputFile = Path.Combine(sourceDirectory, "input.txt");
            File.WriteAllText(inputFile, "input");

            var operations = new Operation[] { Operation.WriteFileIfInputEqual(FileArtifact.CreateOutputFile(AbsolutePath.Create(PathTable, inputFile)), inputFile, "input", " appended") };

            var containerConfiguration = ContainerConfiguration.CreateConfigurationForTesting(PathTable, new[] { (sourceDirectory, destinationDirectory) });

            RunOperationsInContainerAsync(operations, containerConfiguration).GetAwaiter().GetResult();

            // The input file should still be there, with its original content
            var originalContent = File.ReadAllText(inputFile);
            XAssert.AreEqual("input", originalContent);

            // The output file should be in the redirected directory, with new content
            var modifiedContent = File.ReadAllText(Path.Combine(destinationDirectory, "input.txt"));
            XAssert.AreEqual("input appended", modifiedContent);
        }

        [FactIfSupported(requiresHeliumDriversAvailable: true)]
        public void MultipleSourcesAreVirtualizedAndMergedInOrder()
        {
            CreateSourceAndDestinationDirectories(out string source1, out string destinationDirectory);
            // Create a second source directory
            var source2 = CreateTemporaryDirectory("source2");

            // Creates the following layout with [content]
            // 
            // source1/ ---> upper.txt [upper]
            //          +--> both.txt  [upper]
            // source2/ ---> lower.txt [lower]
            //          + -> both.txt  [lower]
            var upper = Path.Combine(source1, "upper.txt");
            File.WriteAllText(upper, "upper");
            var upperBoth = Path.Combine(source1, "both.txt");
            File.WriteAllText(upperBoth, "upper");

            var lower = Path.Combine(source2, "lower.txt");
            File.WriteAllText(lower, "lower");
            var lowerBoth = Path.Combine(source2, "both.txt");
            File.WriteAllText(lowerBoth, "lower");

            // Source1 goes first, then source2. This determines who wins the merge.
            var containerConfiguration = ContainerConfiguration.CreateConfigurationForTesting(PathTable, new[] {
                (source1, destinationDirectory),
                (source2, destinationDirectory)
            });

            var operations = new Operation[] {
                Operation.WriteFileIfInputEqual(FileArtifact.CreateOutputFile(AbsolutePath.Create(PathTable, upper)), upper, "upper", " appended"),
                Operation.WriteFileIfInputEqual(FileArtifact.CreateOutputFile(AbsolutePath.Create(PathTable, upperBoth)), upperBoth, "upper", " appended"),
                Operation.WriteFileIfInputEqual(FileArtifact.CreateOutputFile(AbsolutePath.Create(PathTable, lowerBoth)), lowerBoth, "upper appended", " again"),
                Operation.WriteFileIfInputEqual(FileArtifact.CreateOutputFile(AbsolutePath.Create(PathTable, lower)), lower, "lower", " appended"),
            };

            RunOperationsInContainerAsync(operations, containerConfiguration).GetAwaiter().GetResult();

            // The result should look like this:
            // 
            // destination/ ---> upper.txt [upper appended]
            //              +--> lower.txt [lower appended]
            //              + -> both.txt  [upper appended again]
            var modifiedUpper = File.ReadAllText(Path.Combine(destinationDirectory, "upper.txt"));
            XAssert.AreEqual("upper appended", modifiedUpper);
            var modifiedLower = File.ReadAllText(Path.Combine(destinationDirectory, "lower.txt"));
            XAssert.AreEqual("lower appended", modifiedLower);
            var modifiedBoth = File.ReadAllText(Path.Combine(destinationDirectory, "both.txt"));
            XAssert.AreEqual("upper appended again", modifiedBoth);
        }

        [FactIfSupported(requiresHeliumDriversAvailable: true)]
        public void MultipleSourcesAreVirtualizedRecursively()
        {
            CreateSourceAndDestinationDirectories(out string source1Directory, out string destinationDirectory);
            var source1ADirectory = CreateTemporaryDirectory("source\\A");

            // Create a second source directory
            var source2Directory = CreateTemporaryDirectory("source2");
            var source2ADirectory = CreateTemporaryDirectory("source2\\A");

            // Creates the following layout
            // 
            // source1/ ---> source1.txt
            //          +--> A/
            //                +--> source1_A.txt 
            // source2/ ---> source2.txt 
            //          + -> A/
            //                +--> source2_A.txt
            var source1 = Path.Combine(source1Directory, "source1.txt");
            File.WriteAllText(source1, "source1");
            var source1A = Path.Combine(source1ADirectory, "source1_A.txt");
            File.WriteAllText(source1A, "source1A");
            var source2 = Path.Combine(source2Directory, "source2.txt");
            File.WriteAllText(source2, "source2");
            var source2A = Path.Combine(source2ADirectory, "source2_A.txt");
            File.WriteAllText(source2A, "source2A");

            var containerConfiguration = ContainerConfiguration.CreateConfigurationForTesting(PathTable, new[] {
                (source1Directory, destinationDirectory),
                (source2Directory, destinationDirectory)
            });

            var operations = new Operation[] {
                Operation.ReadFile(FileArtifact.CreateSourceFile(AbsolutePath.Create(PathTable, source1))),
                Operation.ReadFile(FileArtifact.CreateSourceFile(AbsolutePath.Create(PathTable, source2))),
                Operation.ReadFile(FileArtifact.CreateSourceFile(AbsolutePath.Create(PathTable, source1A))),
                Operation.ReadFile(FileArtifact.CreateSourceFile(AbsolutePath.Create(PathTable, source2A)))
            };

            // The output file should be in the redirected directory, with new content
            RunOperationsInContainerAsync(operations, containerConfiguration).GetAwaiter().GetResult();
        }

        [FactIfSupported(requiresHeliumDriversAvailable: true)]
        public void SecurityDescriptorIsInherited()
        {
            CreateSourceAndDestinationDirectories(out string sourceDirectory, out string destinationDirectory);

            var inputFile = Path.Combine(sourceDirectory, "input.txt");
            File.WriteAllText(inputFile, "input");

#if PLATFORM_WIN
            // Deny write on the original input file
            FileUtilities.SetFileAccessControl(inputFile, FileSystemRights.Write, allow: false);
#endif
            var operations = new Operation[] { Operation.WriteFileIfInputEqual(FileArtifact.CreateOutputFile(AbsolutePath.Create(PathTable, inputFile)), inputFile, "input", " appended") };

            var containerConfiguration = ContainerConfiguration.CreateConfigurationForTesting(PathTable, new[] { (sourceDirectory, destinationDirectory) });

            RunOperationsInContainerAsync(operations, containerConfiguration).GetAwaiter().GetResult();

            // The output file should be in the redirected directory, with new content, even though
            // the original file was ACLed for deny-write
            var modifiedContent = File.ReadAllText(Path.Combine(destinationDirectory, "input.txt"));
            XAssert.AreEqual("input appended", modifiedContent);
        }

            #region helpers
            protected async Task<SandboxedProcessResult> RunOperationsInContainerAsync(IEnumerable<Operation> operations, ContainerConfiguration containerConfiguration)
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                // Write an output file to the source dir, but pass remapping information so it actually gets written in the destination directory
                var info =
                new SandboxedProcessInfo(PathTable, tempFiles, m_testProcessPath, disableConHostSharing: false, containerConfiguration: containerConfiguration)
                {
                    PipSemiStableHash = 0,
                    PipDescription = "TestContainer",
                    Arguments = CreateArguments(operations)
                };

                info.FileAccessManifest.FailUnexpectedFileAccesses = false;
                info.FileAccessManifest.MonitorChildProcesses = true;

                using (SandboxedProcess process = await SandboxedProcess.StartAsync(info))
                {
                    SandboxedProcessResult result = await process.GetResultAsync();

                    XAssert.AreEqual(0, result.ExitCode);

                    return result;
                }
            }
        }

        protected string CreateArguments(
            IEnumerable<Operation> processOperations)
        {
            var sb = new StringBuilder();
            foreach (var op in processOperations)
            {
                sb.Append(op.ToCommandLine(PathTable));
                sb.Append(" ");
            }

            return sb.ToString();
        }

        protected void CreateSourceAndDestinationDirectories(out string sourceDirectory, out string destinationDirectory)
        {
            sourceDirectory = CreateTemporaryDirectory("source");
            destinationDirectory = CreateTemporaryDirectory("destination");
        }

        private string CreateTemporaryDirectory(string directoryName)
        {
            var sourceDirectory = Path.Combine(TemporaryDirectory, directoryName);
            Directory.CreateDirectory(sourceDirectory);
            return sourceDirectory;
        }

        #endregion
    }
}
