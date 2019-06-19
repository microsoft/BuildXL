// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public class TemporaryOutputFingerprintTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        private readonly BuildXLContext m_context;

        public TemporaryOutputFingerprintTests(ITestOutputHelper output)
            : base(output)
        {
            m_context = BuildXLContext.CreateInstanceForTesting();
        }
#if false
        /// <summary>
        /// Fingerprinting logic depends on the FileExistence attributes but not on rewrite count.
        /// I.e. two process with the same output paths but with different version should produce the same
        /// fingerprints but two processes with the same output paths but with different attributes
        /// </summary>
        [Theory]
        [InlineData(FileExistence.Optional, FileExistence.Required)]
        [InlineData(FileExistence.Optional, FileExistence.Temporary)]
        [InlineData(FileExistence.Required, FileExistence.Temporary)]
        public void TestPipOutputFingerprinting(FileExistence existence, FileExistence anotherExistence)
        {
            // Arrange
            var pathTable = m_context.PathTable;
            var executable = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/pkgs/tool.exe")));
            AbsolutePath outputPath = AbsolutePath.Create(pathTable, X("/x/obj/working/out.bin"));

            FileArtifactWithAttributes output = new FileArtifactWithAttributes(outputPath, rewriteCount: 1, fileExistence: existence);
            Process process = DefaultBuilder.WithOutputs(output).Build();

            Process processSecondVersion = DefaultBuilder.WithOutputs(output.CreateNextWrittenVersion()).Build();

            var outputWithDifferenceExistence = new FileArtifactWithAttributes(outputPath, rewriteCount: 1, fileExistence: anotherExistence);
            var processWithDifferenceExistence = DefaultBuilder.WithOutputs(outputWithDifferenceExistence).Build();

            var fingerprinter = CreateFingerprinter(executable);

            // Act
            var fingerprint = fingerprinter.ComputeFingerprint(process);

            // Assert
            XAssert.AreEqual(fingerprint, fingerprinter.ComputeFingerprint(processSecondVersion),
                "RewriteCount should not affect the fingerprint");
            XAssert.AreNotEqual(fingerprint, fingerprinter.ComputeFingerprint(processWithDifferenceExistence),
                "Two process with the same output path but with different attributes should produce different fingerprints");
        }

        private ProcessBuilder DefaultBuilder
        {
            get
            {
                var pathTable = m_context.PathTable;

                var executable = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/pkgs/tool.exe")));
                var dependencies = new HashSet<FileArtifact> { executable };

                return
                    new ProcessBuilder()
                    .WithExecutable(executable)
                    .WithWorkingDirectory(AbsolutePath.Create(pathTable, X("/x/obj/working")))
                    .WithArguments(PipDataBuilder.CreatePipData(pathTable.StringTable, " ", PipDataFragmentEscaping.CRuntimeArgumentRules, "-loadargs"))
                    .WithStandardDirectory(AbsolutePath.Create(pathTable, X("/x/obj/working.std")))
                    .WithDependencies(dependencies)
                    .WithContext(m_context);
            }
        }

        private PipContentFingerprinter CreateFingerprinter(FileArtifact executable)
        {
            return new PipContentFingerprinter(
                m_context.PathTable,
                PipFingerprinterTests.GetContentHashLookup(executable),
                ExtraFingerprintSalts.Default())
            {
                FingerprintTextEnabled = true
            };
        }
#endif
    }
}
