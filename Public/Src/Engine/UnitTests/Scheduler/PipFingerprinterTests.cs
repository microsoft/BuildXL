// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Cache.ContentStore.Hashing;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Scheduler correctness depends on an injective pip fingerprinting function.
    /// These tests exercise fingerprinting by varying pips and validating the
    /// expected presence or absence of fingerprint collisions. Expectations
    /// are established by requiring a <see cref="PipCachingAttribute" /> on
    /// pip properties.
    /// </summary>
    public sealed class PipFingerprinterTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        private readonly BuildXLContext m_context;

        private enum FingerprinterTestKind
        {
            Static,
            Content
        }

        public PipFingerprinterTests(ITestOutputHelper output) : base(output) => m_context = BuildXLContext.CreateInstanceForTesting();

        internal static PipFragmentRenderer.ContentHashLookup GetContentHashLookup(FileArtifact executable)
        {
            return file =>
            {
                if (executable == file)
                {
                    var hashBytes = Enumerable.Repeat((byte)1, ContentHashingUtilities.HashInfo.ByteLength).ToArray();
                    var hash = ContentHashingUtilities.CreateFrom(hashBytes);
                    return FileContentInfo.CreateWithUnknownLength(hash);
                }

                XAssert.Fail("Attempted to load hash for file other than executable");
                return FileContentInfo.CreateWithUnknownLength(ContentHashingUtilities.ZeroHash);
            };
        }

        [Fact]
        public void ProcessContentFingerprinting()
        {
            var contentFingerprintingTypeDescriptors = CreateTypeDescriptors(m_context.PathTable, FingerprinterTestKind.Content);

            VerifySinglePropertyVariationsAreCollisionFree<Process>(
                m_context.PathTable,
                contentFingerprintingTypeDescriptors,
                CreateProcessPipVariant,
                CreateDefaultContentFingerprinter,
                (fingerprinter, pip) => fingerprinter.ComputeWeakFingerprint(pip));
        }

        [Fact]
        public void ProcessStaticFingerprinting()
        {
            var staticFingerprintingTypeDescriptors = CreateTypeDescriptors(m_context.PathTable, FingerprinterTestKind.Static);

            VerifySinglePropertyVariationsAreCollisionFree<Process>(
                m_context.PathTable,
                staticFingerprintingTypeDescriptors,
                CreateProcessPipVariant,
                CreateDefaultStaticFingerprinter,
                (fingerprinter, pip) => fingerprinter.ComputeWeakFingerprint(pip));
        }

        [Fact]
        public void ProcessFingerprintingOrderIndependent()
        {
            var pathTable = m_context.PathTable;
            var executable = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/pkgs/tool.exe")));

            Func<FileArtifact[], FileArtifactWithAttributes[], Process> createProcess = (deps, outputs) =>
            {
                var dependencies = new HashSet<FileArtifact>(deps) { executable };
                return GetDefaultProcessBuilder(pathTable, executable)
                    .WithDependencies(dependencies)
                    .WithOutputs(outputs)
                    .Build();
            };

            FileArtifact input1 = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/obj/working/written1.txt")));
            FileArtifact input2 = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/obj/working/written2.txt")));

            FileArtifactWithAttributes output1 = FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/obj/working/out1.bin"))), FileExistence.Temporary).CreateNextWrittenVersion();
            FileArtifactWithAttributes output2 = FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/obj/working/out2.bin"))), FileExistence.Temporary).CreateNextWrittenVersion();

            var processA = createProcess(new[] { input1, input2 }, new[] { output1, output2 });
            var processB = createProcess(new[] { input2, input1 }, new[] { output2, output1 });

            PipFragmentRenderer.ContentHashLookup contentHashLookupFunc = file =>
            {
                var hashBytes = Enumerable.Repeat((byte)1, ContentHashingUtilities.HashInfo.ByteLength).ToArray();
                var hash = ContentHashingUtilities.CreateFrom(hashBytes);
                return FileContentInfo.CreateWithUnknownLength(hash);
            };

            var contentFingerprinter = new PipContentFingerprinter(
                m_context.PathTable,
                contentHashLookupFunc,
                ExtraFingerprintSalts.Default())
            {
                FingerprintTextEnabled = true
            };

            var contentFingerprintA = contentFingerprinter.ComputeWeakFingerprint(processA, out var contentFingerprintTextA);
            var contentFingerprintB = contentFingerprinter.ComputeWeakFingerprint(processB, out var contentFingerprintTextB);

            XAssert.AreEqual(contentFingerprintA, contentFingerprintB);

            var staticFingerprinter = new PipStaticFingerprinter(
                m_context.PathTable,
                extraFingerprintSalts: ExtraFingerprintSalts.Default())
            {
                FingerprintTextEnabled = true
            };

            var staticFingerprintA = contentFingerprinter.ComputeWeakFingerprint(processA, out var staticFingerprintTextA);
            var staticFingerprintB = contentFingerprinter.ComputeWeakFingerprint(processB, out var staticFingerprintTextB);

            XAssert.AreEqual(staticFingerprintA, staticFingerprintB);
        }

        [Fact]
        public void CopyFileStaticFingerprinting()
        {
            var staticFingerprintingTypeDescriptors = CreateTypeDescriptors(m_context.PathTable, FingerprinterTestKind.Static);

            VerifySinglePropertyVariationsAreCollisionFree<CopyFile>(
                m_context.PathTable,
                staticFingerprintingTypeDescriptors,
                CreateCopyFileVariant,
                CreateDefaultStaticFingerprinter,
                (fingerprinter, pip) => fingerprinter.ComputeWeakFingerprint(pip));
        }

        [Fact]
        public void WriteFileStaticFingerprinting()
        {
            var staticFingerprintingTypeDescriptors = CreateTypeDescriptors(m_context.PathTable, FingerprinterTestKind.Static);

            VerifySinglePropertyVariationsAreCollisionFree<WriteFile>(
                m_context.PathTable,
                staticFingerprintingTypeDescriptors,
                CreateWriteFileVariant,
                CreateDefaultStaticFingerprinter,
                (fingerprinter, pip) => fingerprinter.ComputeWeakFingerprint(pip));
        }

        [Fact]
        public void HashSourceFileStaticFingerprinting()
        {
            var staticFingerprintingTypeDescriptors = CreateTypeDescriptors(m_context.PathTable, FingerprinterTestKind.Static);

            VerifySinglePropertyVariationsAreCollisionFree<HashSourceFile>(
                m_context.PathTable,
                staticFingerprintingTypeDescriptors,
                CreateHashSourceFileVariant,
                CreateDefaultStaticFingerprinter,
                (fingerprinter, pip) => fingerprinter.ComputeWeakFingerprint(pip));
        }

        [Fact]
        public void SealDirectoryStaticFingerprinting()
        {
            var staticFingerprintingTypeDescriptors = CreateTypeDescriptors(m_context.PathTable, FingerprinterTestKind.Static);

            VerifySinglePropertyVariationsAreCollisionFree<SealDirectory>(
                m_context.PathTable,
                staticFingerprintingTypeDescriptors,
                CreateSealDirectoryVariant,
                CreateSealDirectoryStaticFingerprinter,
                (fingerprinter, pip) => fingerprinter.ComputeWeakFingerprint(pip));
        }

        [Fact]
        public void TestPipDataDependencyFingerprinting()
        {
            var pathTable = m_context.PathTable;
            var executable = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/pkgs/tool.exe")));

            Func<FileArtifact[], Process> createProcessWithDependencies = (additionalDependencies =>
            {
                var dependencies = new HashSet<FileArtifact>(additionalDependencies) {executable};
                return GetDefaultProcessBuilder(pathTable, executable)
                    .WithDependencies(dependencies)
                    .WithOutputs(FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/obj/working/out.bin"))), FileExistence.Temporary).CreateNextWrittenVersion())
                    .Build();
            });

            FileArtifact inputPipDataContentFile = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/obj/working/written.txt")));

            var baselineProcess = createProcessWithDependencies(new FileArtifact[0]);
            var processWithPipDataDependency = createProcessWithDependencies(new[] { inputPipDataContentFile });

            const string TestPipDataContent = @"TESTPIPDATACONTENT";

            var pipDataLookup = new PipFingerprinter.PipDataLookup(file =>
            {
                if (file == inputPipDataContentFile)
                {
                    return PipDataBuilder.CreatePipData(m_context.StringTable, "\n", PipDataFragmentEscaping.CRuntimeArgumentRules, TestPipDataContent);
                }

                XAssert.AreEqual(file, executable, "Attempted to load pip data for file which is not a dependency");
                return PipData.Invalid;
            });

            // TODO: Maybe test that version is included in the fingerprint.
            var fingerprinter = new PipContentFingerprinter(
                m_context.PathTable,
                GetContentHashLookup(executable),
                ExtraFingerprintSalts.Default(),
                pipDataLookup: pipDataLookup)
            {
                FingerprintTextEnabled = true
            };

            string fingerprintText;
            var baselineFingerprint = fingerprinter.ComputeWeakFingerprint(baselineProcess, out fingerprintText);
            XAssert.IsFalse(fingerprintText.Contains(TestPipDataContent));

            var fingerprintWithPipDataDependency = fingerprinter.ComputeWeakFingerprint(processWithPipDataDependency, out fingerprintText);
            XAssert.IsTrue(fingerprintText.Contains(TestPipDataContent));

            XAssert.AreNotEqual(baselineFingerprint, fingerprintWithPipDataDependency);
        }

        private (string, string) CreateProcessAndFingerprintsWithPathArg(string pathArg, bool pathAsStringLIteral)
        {
            var pathTable = new PathTable();
            var executable = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/pkgs/tool.exe")));
            var pathExpander = new MountPathExpander(pathTable);

            // Make a pip data command line with a path added
            var dataBuilder = new PipDataBuilder(pathTable.StringTable);
            if (pathAsStringLIteral)
            {
                dataBuilder.Add(pathArg);
            }
            else
            {
                AbsolutePath.TryCreate(pathTable, pathArg, out var path);
                dataBuilder.Add(path);
            }
            var pipData = dataBuilder.ToPipData(" ", PipDataFragmentEscaping.NoEscaping);

            // Make a pip with the command line
            var process = GetDefaultProcessBuilder(pathTable, executable).WithArguments(pipData).Build();

            // Compute the fingerprints of the pip
            var fingerprinter = new PipContentFingerprinter(
                pathTable,
                GetContentHashLookup(executable),
                ExtraFingerprintSalts.Default(),
                pathExpander: pathExpander)
            {
                FingerprintTextEnabled = true
            };

            fingerprinter.ComputeWeakFingerprint(process, out var fingerprintText);

            var json = JsonFingerprinter.CreateJsonString(writer =>
            {
                fingerprinter.AddWeakFingerprint(writer, process);
            },
            pathTable: pathTable);

            return (fingerprintText, json);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestCaseInsensitivePathsInCommandLine(bool pathAsStringLiteral)
        {
            // Paths in the command line should be case insensitive
            // Note, this is not true of the path is added to the command line as a raw string
            var lowerCaseResult = CreateProcessAndFingerprintsWithPathArg(A("x", "pathtest", "nestedpath"), pathAsStringLiteral);
            var camelCaseResult = CreateProcessAndFingerprintsWithPathArg(A("x", "pathTest", "nestedPath"), pathAsStringLiteral);
            var upperCaseResult = CreateProcessAndFingerprintsWithPathArg(A("x", "PATHTEST", "NESTEDPATH"), pathAsStringLiteral);

            if (pathAsStringLiteral)
            {
                XAssert.AreNotEqual(lowerCaseResult, camelCaseResult);
                XAssert.AreNotEqual(lowerCaseResult, upperCaseResult);
                XAssert.AreNotEqual(camelCaseResult, upperCaseResult);
            }
            else
            {
                XAssert.AreEqual(lowerCaseResult, camelCaseResult);
                XAssert.AreEqual(camelCaseResult, upperCaseResult);
                // Skip an equality check because of transitive propertry of equality
            }
        }

        [Fact]
        public void TestUserProfileStaysOutOfFingerprints()
        {
            var pathTable = m_context.PathTable;
            var executable = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/pkgs/tool.exe")));

            PipDataBuilder pdb = new PipDataBuilder(m_context.PathTable.StringTable);
            string appDataSubdirName = "appdataSubDir";
            pdb.Add(AbsolutePath.Create(pathTable, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appDataSubdirName)));
            EnvironmentVariable envVar = new EnvironmentVariable(m_context.StringTable.AddString("testVar"), pdb.ToPipData(" ", PipDataFragmentEscaping.NoEscaping));

            var dependencies = new HashSet<FileArtifact>() { executable };
            var process = GetDefaultProcessBuilder(pathTable, executable)
                .WithEnvironmentVariables(new EnvironmentVariable[] { envVar })
                .WithDependencies(dependencies)
                .WithOutputs(FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/obj/working/out.bin"))), FileExistence.Temporary).CreateNextWrittenVersion())
                .WithUntrackedPaths(new AbsolutePath[] { AbsolutePath.Create(pathTable, Environment.GetFolderPath(Environment.SpecialFolder.InternetCache)) })
                .WithUntrackedScopes(new AbsolutePath[] { AbsolutePath.Create(pathTable, OperatingSystemHelper.IsUnixOS ? "/tmp" : Environment.GetFolderPath(Environment.SpecialFolder.History)) })
                .Build();

            MountPathExpander expander = new MountPathExpander(pathTable);
            string userProfileDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userProfilePath = AbsolutePath.Create(pathTable, userProfileDir);
            expander.Add(pathTable, new SemanticPathInfo(PathAtom.Create(pathTable.StringTable, "UserProfile"), userProfilePath, SemanticPathFlags.System));

            var fingerprinter = new PipContentFingerprinter(
                m_context.PathTable,
                GetContentHashLookup(executable),
                ExtraFingerprintSalts.Default(),
                pathExpander: expander)
            {
                FingerprintTextEnabled = true
            };

            var baselineFingerprint = fingerprinter.ComputeWeakFingerprint(process, out string fingerprintText);
            fingerprintText = fingerprintText.ToUpperInvariant();

            // Make sure the actual user profile doesn't show up in the fingerprint
            XAssert.IsFalse(fingerprintText.Contains(userProfileDir.ToUpperInvariant()));

            // Check that the paths originally containing the user profile are still there
            XAssert.IsTrue(fingerprintText.Contains(appDataSubdirName.ToUpperInvariant()));
            XAssert.IsTrue(fingerprintText.Contains(Path.GetFileName(Environment.GetFolderPath(Environment.SpecialFolder.InternetCache)).ToUpperInvariant()));

            // Validate the JSON fingerprinter does the same
            var json = JsonFingerprinter.CreateJsonString(writer =>
            {
                fingerprinter.AddWeakFingerprint(writer, process);
            },
            pathTable: m_context.PathTable).ToUpperInvariant();
            
            var fence = OperatingSystemHelper.IsUnixOS ? "\"" : "";
            XAssert.IsFalse(json.Contains($"{fence}{userProfileDir.ToUpperInvariant()}{fence}"));

            XAssert.IsTrue(json.Contains(appDataSubdirName.ToUpperInvariant()));
            XAssert.IsTrue(json.Contains(Path.GetFileName(Environment.GetFolderPath(Environment.SpecialFolder.InternetCache)).ToUpperInvariant()));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestPathsUnderRedirectedUserProfileProperlyTokenized()
        {
            var pathTable = m_context.PathTable;
            var executable = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/pkgs/tool.exe")));

            string pathInsideUserProfile = R("AppData", "Roaming", "Foo");
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string redirectedUserProfile = A("D", "buildXLUserProfile");

            PipDataBuilder pdb = new PipDataBuilder(m_context.PathTable.StringTable);
            pdb.Add(AbsolutePath.Create(pathTable, Path.Combine(userProfile, pathInsideUserProfile)));
            EnvironmentVariable envVarRealProfile = new EnvironmentVariable(m_context.StringTable.AddString("var1"), pdb.ToPipData(" ", PipDataFragmentEscaping.NoEscaping));

            PipDataBuilder pdb2 = new PipDataBuilder(m_context.PathTable.StringTable);
            pdb2.Add(AbsolutePath.Create(pathTable, Path.Combine(redirectedUserProfile, pathInsideUserProfile)));
            EnvironmentVariable envVarRedirectedProfile = new EnvironmentVariable(m_context.StringTable.AddString("var2"), pdb2.ToPipData(" ", PipDataFragmentEscaping.NoEscaping));

            var process = GetDefaultProcessBuilder(pathTable, executable)
                .WithEnvironmentVariables(new EnvironmentVariable[] { envVarRealProfile, envVarRedirectedProfile })
                .WithUntrackedPaths(new AbsolutePath[]
                                    {
                                        AbsolutePath.Create(pathTable, Environment.GetFolderPath(Environment.SpecialFolder.InternetCache)),
                                        AbsolutePath.Create(pathTable, Path.Combine(redirectedUserProfile, "APPDATA","LOCAL","MICROSOFT","WINDOWS","INETCACHE")),
                                    })
                .WithUntrackedScopes(new AbsolutePath[]
                                     {
                                         AbsolutePath.Create(pathTable, Environment.GetFolderPath(Environment.SpecialFolder.History)),
                                         AbsolutePath.Create(pathTable, Path.Combine(redirectedUserProfile, "APPDATA","LOCAL","MICROSOFT","WINDOWS","HISTORY")),
                                     })
                .Build();

            MountPathExpander expander = new MountPathExpander(pathTable);
            var userProfilePath = AbsolutePath.Create(pathTable, userProfile);
            var redirectedUserProfilePath = AbsolutePath.Create(pathTable, redirectedUserProfile);
            
            expander.Add(pathTable, new SemanticPathInfo(PathAtom.Create(pathTable.StringTable, "UserProfile"), redirectedUserProfilePath, SemanticPathFlags.System));
            expander.AddWithExistingName(pathTable, new SemanticPathInfo(PathAtom.Create(pathTable.StringTable, "UserProfile"), userProfilePath, SemanticPathFlags.System));

            var fingerprinter = new PipContentFingerprinter(
                m_context.PathTable,
                GetContentHashLookup(executable),
                ExtraFingerprintSalts.Default(),
                pathExpander: expander)
            {
                FingerprintTextEnabled = true
            };

            fingerprinter.ComputeWeakFingerprint(process, out string fingerprintText);
            fingerprintText = fingerprintText.ToUpperInvariant();

            // Check that neither the real nor redirect user profile appears in the fingerprint
            XAssert.IsFalse(fingerprintText.Contains(userProfile.ToUpperInvariant()));
            XAssert.IsFalse(fingerprintText.Contains(redirectedUserProfile.ToUpperInvariant()));

            // Check that all paths have been added
            // Note: normally, there would be no duplicates in untracked paths/scopes blocks. However, for testing purposes
            // (i.e., tokenization happens properly in both of those blocks, and for both real and redirected profiles)
            // we've intentionally added the 'same' paths twice.
            XAssert.IsTrue(fingerprintText.IndexOf("Foo".ToUpperInvariant(), StringComparison.InvariantCulture) != fingerprintText.LastIndexOf("Foo",StringComparison.InvariantCulture));
            XAssert.IsTrue(fingerprintText.IndexOf("INETCACHE".ToUpperInvariant(), StringComparison.InvariantCulture) != fingerprintText.LastIndexOf("INETCACHE",StringComparison.InvariantCulture));
            XAssert.IsTrue(fingerprintText.IndexOf("HISTORY".ToUpperInvariant(), StringComparison.InvariantCulture) != fingerprintText.LastIndexOf("HISTORY",StringComparison.InvariantCulture));
        }

        /// <summary>
        /// Configuration settings that are included in the weak fingerprint salt should only make it in when a non-default setting is being used.
        /// This allows us to change the fingerprint salt without causing cache misses for users using the default settings.
        /// </summary>
        [Fact]
        public void CheckDefaultSandboxSettingsStayOutOfFingerprint()
        {
            var pathTable = m_context.PathTable;
            var executable = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/pkgs/tool.exe")));

            var process = GetDefaultProcessBuilder(pathTable, executable)
                .WithOutputs(FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/obj/working/out.bin"))), FileExistence.Temporary).CreateNextWrittenVersion())
                .Build();

            var defaultSalts = ExtraFingerprintSalts.Default();

            // Calculate a fingerprint using the default configuration settings
            var sandboxConfig = new SandboxConfiguration
            {
                NormalizeReadTimestamps = defaultSalts.NormalizeReadTimestamps,
                MaskUntrackedAccesses = defaultSalts.MaskUntrackedAccesses
            };

            var contentHashLookup = GetContentHashLookup(executable);

            var fingerprinter = new PipContentFingerprinter(
                m_context.PathTable,
                contentHashLookup,
                defaultSalts)
            {
                FingerprintTextEnabled = true
            };
            
            string fingerprintText;
            fingerprinter.ComputeWeakFingerprint(process, out fingerprintText);

            // If the defaults are being used, don't include them in the fingerprint to prevent cache misses
            XAssert.IsFalse(fingerprintText.Contains("NormalizeReadTimestamps"));
            XAssert.IsFalse(fingerprintText.Contains("MaskUntrackedAccesses"));

            // Calculate a fingerprint with non-default settings
            sandboxConfig.NormalizeReadTimestamps = !defaultSalts.NormalizeReadTimestamps;
            sandboxConfig.MaskUntrackedAccesses = !defaultSalts.MaskUntrackedAccesses;

            var configuration = new ConfigurationImpl
            {
                Sandbox = sandboxConfig
            };

            var nonDefaultSalts = new ExtraFingerprintSalts(configuration, PipFingerprintingVersion.TwoPhaseV2, null, null);

            var nonDefaultFingerprinter = new PipContentFingerprinter(
                m_context.PathTable,
                contentHashLookup,
                nonDefaultSalts)
            {
                FingerprintTextEnabled = true
            };

            // If the non-default values are specified by configuration, include the settings in the fingerprint to cause cache misses
            nonDefaultFingerprinter.ComputeWeakFingerprint(process, out string nonDefaultFingerprintText);
            XAssert.IsTrue(nonDefaultFingerprintText.Contains("NormalizeReadTimestamps"));
            XAssert.IsTrue(nonDefaultFingerprintText.Contains("MaskUntrackedAccesses"));
        }

        [Fact]
        public void TestChoose()
        {
            var chosen = new HashSet<string>(
                Combinatorics.Choose(2, "abcd".ToCharArray()).Select(c => new string(c)));

            var expectedSubsequences = new HashSet<string> { "ab", "ac", "ad", "bc", "bd", "cd" };
            foreach (string expected in expectedSubsequences)
            {
                XAssert.IsTrue(chosen.Contains(expected), "Missing " + expected);
            }

            XAssert.AreEqual(expectedSubsequences.Count, chosen.Count);
        }

        [Fact]
        public void WarnAsError()
        {
            LoggingConfiguration config = new LoggingConfiguration();
            XAssert.IsFalse(ExtraFingerprintSalts.ArePipWarningsPromotedToErrors(config));

            config.TreatWarningsAsErrors = true;
            XAssert.IsTrue(ExtraFingerprintSalts.ArePipWarningsPromotedToErrors(config));

            config.WarningsNotAsErrors.Add((int)EventId.PipProcessWarning);
            XAssert.IsFalse(ExtraFingerprintSalts.ArePipWarningsPromotedToErrors(config));

            config.WarningsNotAsErrors.Clear();
            config.TreatWarningsAsErrors = false;
            config.WarningsAsErrors.Add((int)EventId.PipProcessWarning);
            XAssert.IsTrue(ExtraFingerprintSalts.ArePipWarningsPromotedToErrors(config));
        }

        private ProcessBuilder GetDefaultProcessBuilder(PathTable pathTable, FileArtifact executable)
        {
            return (new ProcessBuilder())
                .WithContext(m_context)
                .WithExecutable(executable)
                .WithWorkingDirectory(AbsolutePath.Create(pathTable, X("/x/obj/working")))
                .WithArguments(PipDataBuilder.CreatePipData(pathTable.StringTable, " ", PipDataFragmentEscaping.CRuntimeArgumentRules, "-loadargs"))
                .WithStandardDirectory(AbsolutePath.Create(pathTable, X("/x/obj/working.std")));
        }

        private Process CreateProcessPipVariant(VariationSource<Process> source)
        {
            // Process pip construction has some preconditions around executable, response file, standard output, etc.
            // being listed as dependencies / outputs. We satisfy those here by appending to the varied lists and by
            // substituting a valid path when needed (invalid paths / file artifacts are the general base case; see
            // CreateTypeDescriptors).
            FileArtifact executable = source.Vary(p => p.Executable);
            if (!executable.IsValid)
            {
                executable = FileArtifact.CreateSourceFile(AbsolutePath.Create(source.PathTable, X("/Z/DefaultExe")));
            }

            AbsolutePath workingDirectory = source.Vary(p => p.WorkingDirectory);
            if (!workingDirectory.IsValid)
            {
                workingDirectory = AbsolutePath.Create(source.PathTable, X("/Z/DefaultWorkingDir"));
            }

            var standardDirectory = AbsolutePath.Create(source.PathTable, X("/Z/DefaultStandardDir"));

            var dependencies = new List<FileArtifact>(source.Vary(p => p.Dependencies)) {executable};

            FileArtifact standardInputFile = source.Vary(p => p.StandardInputFile);
            PipData standardInputData = source.Vary(p => p.StandardInputData);

            StandardInput standardInput;

            if (standardInputFile.IsValid)
            {
                standardInput = standardInputFile;
                dependencies.Add(standardInputFile);
            }
            else
            {
                standardInput = standardInputData;
            }

            var outputs = new List<FileArtifactWithAttributes>(source.Vary(p => p.FileOutputs));

            FileArtifact standardError = source.Vary(p => p.StandardError);
            if (standardError.IsValid)
            {
                if (standardError.IsSourceFile)
                {
                    standardError = standardError.CreateNextWrittenVersion();
                }

                outputs.Add(standardError.WithAttributes());
            }

            FileArtifact standardOutput = source.Vary(p => p.StandardOutput);
            if (standardOutput.IsValid)
            {
                if (standardOutput.IsSourceFile)
                {
                    standardOutput = standardOutput.CreateNextWrittenVersion();
                }

                outputs.Add(standardOutput.WithAttributes());
            }

            for (int i = 0; i < outputs.Count; i++)
            {
                if (outputs[i].IsSourceFile)
                {
                    outputs[i] = outputs[i].CreateNextWrittenVersion();
                }
            }

            bool hasUntrackedChildProcesses = source.Vary(p => p.HasUntrackedChildProcesses);
            bool producesPathIndepenentOutputs = source.Vary(p => p.ProducesPathIndependentOutputs);
            bool outputsMustBeWritable = source.Vary(p => p.OutputsMustRemainWritable);
            bool allowUndeclaredSourceReads = source.Vary(p => p.AllowUndeclaredSourceReads);
            bool needsToRunInContainer = source.Vary(p => p.NeedsToRunInContainer);
            bool requiresAdmin = source.Vary(p => p.RequiresAdmin);
            DoubleWritePolicy doubleWritePolicy = source.Vary(p => p.DoubleWritePolicy);
            ContainerIsolationLevel containerIsolationLevel = source.Vary(p => p.ContainerIsolationLevel);
            var uniqueRedirectedDirectoryRoot = source.Vary(p => p.UniqueRedirectedDirectoryRoot);
            var preserveOutputWhitelist = source.Vary(p => p.PreserveOutputWhitelist);

            Process.Options options = Process.Options.None;
            if (hasUntrackedChildProcesses)
            {
                options |= Process.Options.HasUntrackedChildProcesses;
            }

            if (producesPathIndepenentOutputs)
            {
                options |= Process.Options.ProducesPathIndependentOutputs;
            }

            if (outputsMustBeWritable)
            {
                options |= Process.Options.OutputsMustRemainWritable;
            }

            if (allowUndeclaredSourceReads)
            {
                options |= Process.Options.AllowUndeclaredSourceReads;
            }

            if (needsToRunInContainer)
            {
                options |= Process.Options.NeedsToRunInContainer;
                
                // if the process needs to run in a container, then the redirected root must be set
                if (!uniqueRedirectedDirectoryRoot.IsValid)
                {
                    uniqueRedirectedDirectoryRoot = AbsolutePath.Create(source.PathTable, X("/Z/DefaultRedirectedDirectory"));
                }
            }

            if (requiresAdmin)
            {
                options |= Process.Options.RequiresAdmin;
            }

            return new Process(
                executable: executable,
                workingDirectory: workingDirectory,
                arguments: source.Vary(p => p.Arguments),
                environmentVariables: source.Vary(p => p.EnvironmentVariables),
                standardInput: standardInput,
                standardOutput: standardOutput,
                standardError: standardError,
                standardDirectory: standardDirectory,
                warningTimeout: source.Vary(p => p.WarningTimeout),
                timeout: source.Vary(p => p.Timeout),
                dependencies: ReadOnlyArray<FileArtifact>.From(dependencies),
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.From(outputs),
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.From(source.Vary(p => p.DirectoryDependencies)),
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.From(source.Vary(p => p.DirectoryOutputs)),
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.From(source.Vary(p => p.UntrackedPaths)),
                untrackedScopes: ReadOnlyArray<AbsolutePath>.From(source.Vary(p => p.UntrackedScopes)),
                tags: ReadOnlyArray<StringId>.From(source.Vary(p => p.Tags)),
                successExitCodes: ReadOnlyArray<int>.From(source.Vary(p => p.SuccessExitCodes)),
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                warningRegex: source.Vary(p => p.WarningRegex),
                errorRegex: source.Vary(p => p.ErrorRegex),

                // The response file fields are for logging only, and have a funny precondition constraint between them.
                responseFile: FileArtifact.Invalid,
                responseFileData: PipData.Invalid,
                uniqueOutputDirectory: source.Vary(p => p.UniqueOutputDirectory),
                uniqueRedirectedDirectoryRoot: uniqueRedirectedDirectoryRoot,
                provenance: PipProvenance.CreateDummy(m_context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                options: options,
                doubleWritePolicy: doubleWritePolicy,
                containerIsolationLevel: containerIsolationLevel,
                preserveOutputWhitelist: preserveOutputWhitelist);
        }

        private CopyFile CreateCopyFileVariant(VariationSource<CopyFile> source)
        {
            var copySource = source.Vary(c => c.Source);
            if (!copySource.IsValid)
            {
                copySource = FileArtifact.CreateSourceFile(AbsolutePath.Create(source.PathTable, X("/Z/DefaultCopySource")));
            }

            var copyDestination = source.Vary(c => c.Destination);
            if (!copyDestination.IsValid)
            {
                copyDestination = FileArtifact.CreateOutputFile(AbsolutePath.Create(source.PathTable, X("/Z/DefaultCopyDestination")));
            }

            return new CopyFile(
                copySource, 
                copyDestination, 
                ReadOnlyArray<StringId>.From(source.Vary(c => c.Tags)), 
                PipProvenance.CreateDummy(m_context));
        }

        private WriteFile CreateWriteFileVariant(VariationSource<WriteFile> source)
        {
            var writeDestination = source.Vary(w => w.Destination);
            if (!writeDestination.IsValid)
            {
                writeDestination = FileArtifact.CreateOutputFile(AbsolutePath.Create(source.PathTable, X("/Z/DefaultWriteDestination")));
            }

            return new WriteFile(
                writeDestination,
                source.Vary(w => w.Contents),
                source.Vary(w => w.Encoding),
                ReadOnlyArray<StringId>.From(source.Vary(c => c.Tags)),
                PipProvenance.CreateDummy(m_context));
        }

        private HashSourceFile CreateHashSourceFileVariant(VariationSource<HashSourceFile> source)
        {
            var file = source.Vary(h => h.Artifact);
            if (!file.IsValid)
            {
                file = FileArtifact.CreateSourceFile(AbsolutePath.Create(source.PathTable, X("/Z/SourceFile")));
            }

            return new HashSourceFile(file);
        }

        private SealDirectory CreateSealDirectoryVariant(VariationSource<SealDirectory> source)
        {
            var root = source.Vary(sd => sd.DirectoryRoot);

            if (!root.IsValid)
            {
                root = AbsolutePath.Create(source.PathTable, X("/Z/DefaultRootSealedDirectory"));
            }

            var kind = source.Vary(sd => sd.Kind);
            var contents = source.Vary(sd => sd.Contents);
            var patterns = source.Vary(sd => sd.Patterns);
            var composedDirectories = source.Vary(sd => sd.ComposedDirectories);
            var isComposite = source.Vary(sd => sd.IsComposite);
            var scrub = source.Vary(sd => sd.Scrub);

            // If the resulting combination will create an invalid seal directory, create a random valid combination.
            if (kind == SealDirectoryKind.Full || kind == SealDirectoryKind.Partial)
            {
                if (isComposite || composedDirectories.Count > 0 || patterns.Any())
                {
                    root = AbsolutePath.Create(source.PathTable, X("/Z/Random-") + Guid.NewGuid().ToString());
                    isComposite = false;
                    composedDirectories = ReadOnlyArray<DirectoryArtifact>.Empty;
                    patterns = ReadOnlyArray<StringId>.Empty;
                }
            }
            else if (kind == SealDirectoryKind.Opaque)
            {
                if (isComposite || composedDirectories.Count > 0 || contents.Any() || patterns.Any())
                {
                    root = AbsolutePath.Create(source.PathTable, X("/Z/Random-") + Guid.NewGuid().ToString());
                    isComposite = false;
                    composedDirectories = ReadOnlyArray<DirectoryArtifact>.Empty;
                    contents = SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.FromSortedArrayUnsafe(
                        ReadOnlyArray<FileArtifact>.Empty,
                        OrdinalFileArtifactComparer.Instance);
                    patterns = ReadOnlyArray<StringId>.Empty;
                }
            }
            else if (kind.IsSourceSeal())
            {
                if (isComposite || composedDirectories.Count > 0 || contents.Any())
                {
                    root = AbsolutePath.Create(source.PathTable, X("/Z/Random-") + Guid.NewGuid().ToString());
                    isComposite = false;
                    composedDirectories = ReadOnlyArray<DirectoryArtifact>.Empty;
                    contents = SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.FromSortedArrayUnsafe(
                        ReadOnlyArray<FileArtifact>.Empty,
                        OrdinalFileArtifactComparer.Instance);
                }
            }
            else if (kind == SealDirectoryKind.SharedOpaque)
            {
                if (isComposite)
                {
                    if (contents.Any() || patterns.Any())
                    {
                        root = AbsolutePath.Create(source.PathTable, X("/Z/Random-") + Guid.NewGuid().ToString());
                        contents = SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.FromSortedArrayUnsafe(
                            ReadOnlyArray<FileArtifact>.Empty,
                            OrdinalFileArtifactComparer.Instance);
                        patterns = ReadOnlyArray<StringId>.Empty;
                    }
                }
                else
                {
                    if (composedDirectories.Count > 0 || contents.Any() || patterns.Any())
                    {
                        root = AbsolutePath.Create(source.PathTable, X("/Z/Random-") + Guid.NewGuid().ToString());
                        composedDirectories = ReadOnlyArray<DirectoryArtifact>.Empty;
                        contents = SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.FromSortedArrayUnsafe(
                            ReadOnlyArray<FileArtifact>.Empty,
                            OrdinalFileArtifactComparer.Instance);
                        patterns = ReadOnlyArray<StringId>.Empty;
                    }
                }
            }

            if (isComposite)
            {
                return new CompositeSharedOpaqueSealDirectory(
                    root,
                    composedDirectories,
                    PipProvenance.CreateDummy(m_context),
                    ReadOnlyArray<StringId>.From(source.Vary(sd => sd.Tags)));
            }

            var sealDirectory = new SealDirectory(
                root,
                contents,
                kind,
                PipProvenance.CreateDummy(m_context),
                ReadOnlyArray<StringId>.From(source.Vary(sd => sd.Tags)),
                patterns,
                scrub);

            // The seal directory needs to be initialized
            DirectoryArtifact directoryArtifact;

            if (sealDirectory.Kind == SealDirectoryKind.SharedOpaque)
            {
                // Since this is a shared opaque, create a directory artifact with any seal id != 0.
                directoryArtifact = new DirectoryArtifact(root, 1, isSharedOpaque: true);
            }
            else
            {
                directoryArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(root);
            }

            sealDirectory.SetDirectoryArtifact(directoryArtifact);

            return sealDirectory;
        }

        /// <summary>
        /// Defines per-type test values and their equivalence classes. These values are used to vary pip fields,
        /// including by constructing various arrays of test values. All types for properties that are varied
        /// must have a corresponding descriptor.
        /// </summary>
        private static IFingerprintingTypeDescriptor[] CreateTypeDescriptors(PathTable pathTable, FingerprinterTestKind fingerprinterTestKind)
        {
            Contract.Requires(pathTable != null);

            var paths = new[]
                        {
                            AbsolutePath.Create(pathTable, X("/X/someDirectory/someArtifact")),
                            AbsolutePath.Create(pathTable, X("/X/otherDirectory/someArtifact")),
                            AbsolutePath.Create(pathTable, X("/X/rootChild")),
                            AbsolutePath.Create(pathTable, X("/Y/rootChild"))
                        };

            var stringTable = pathTable.StringTable;

            var pathWithSpace = AbsolutePath.Create(pathTable, X("/X/folder with space/someArtifact"));

            return new IFingerprintingTypeDescriptor[]
                   {
                       new FingerprintingTypeDescriptor<bool>(false, true),
                       new FingerprintingTypeDescriptor<int>(0, -1, 1, 11, 2, 3),
                       new FingerprintingTypeDescriptor<string>(string.Empty, "A", "1", "Abc", "ABC", "Def", "ABC with suffix"),
                       new FingerprintingTypeDescriptor<TimeSpan?>(null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)),
                       new FingerprintingTypeDescriptor<StringId>(
                           StringId.Create(stringTable, "A"),
                           StringId.Create(stringTable, "B"),
                           StringId.Create(stringTable, "C"),
                           StringId.Create(stringTable, "D")),
                       new FingerprintingTypeDescriptor<RegexDescriptor>(
                           new RegexDescriptor(StringId.Create(stringTable, "A"), RegexOptions.None),
                           new RegexDescriptor(StringId.Create(stringTable, ".+"), RegexOptions.None),
                           new RegexDescriptor(StringId.Create(stringTable, ".?"), RegexOptions.None),
                           new RegexDescriptor(StringId.Create(stringTable, ".?"), RegexOptions.Multiline)),
                       new FingerprintingTypeDescriptor<AbsolutePath>(AbsolutePath.Invalid, paths),
                       new FingerprintingTypeDescriptor<DirectoryArtifact>(
                           DirectoryArtifact.Invalid,
                           role => GenerateMutuallyExclusiveDirectoryArtifactEquivalenceClasses(pathTable, paths, role, fingerprinterTestKind).SelectMany(mutex => mutex)),
                       new FingerprintingTypeDescriptor<FileArtifact>(
                           FileArtifact.Invalid,
                           role => GenerateMutuallyExclusiveFileArtifactEquivalenceClasses(pathTable, paths, role, fingerprinterTestKind).SelectMany(mutex => mutex)),
                        new FingerprintingTypeDescriptor<FileArtifactWithAttributes>(
                           FileArtifactWithAttributes.Invalid,
                           GetFileArtifactWithAttributesVariantValues(paths)),

                       // Used for EnvironmentVariables
                       new FingerprintingTypeDescriptor<EnvironmentVariable>(
                                new EnvironmentVariable(StringId.Create(stringTable, "X"), PipDataBuilder.CreatePipData(pathTable.StringTable, string.Empty, PipDataFragmentEscaping.CRuntimeArgumentRules)),
                                new EnvironmentVariable(StringId.Create(stringTable, "Y"), PipDataBuilder.CreatePipData(pathTable.StringTable, string.Empty, PipDataFragmentEscaping.CRuntimeArgumentRules)),
                           new EnvironmentVariable(StringId.Create(stringTable, "X"), PipDataBuilder.CreatePipData(pathTable.StringTable, ":", PipDataFragmentEscaping.CRuntimeArgumentRules))),
                       new FingerprintingTypeDescriptor<PipData>(
                           PipDataBuilder.CreatePipData(pathTable.StringTable, string.Empty, PipDataFragmentEscaping.CRuntimeArgumentRules),
                           PipDataBuilder.CreatePipData(pathTable.StringTable, string.Empty, PipDataFragmentEscaping.CRuntimeArgumentRules, paths[0]),
                           PipDataBuilder.CreatePipData(pathTable.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, paths[1]),
                           PipDataBuilder.CreatePipData(pathTable.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, pathWithSpace),
                           PipDataBuilder.CreatePipData(pathTable.StringTable, string.Empty, PipDataFragmentEscaping.CRuntimeArgumentRules, pathWithSpace),
                           PipDataBuilder.CreatePipData(pathTable.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, pathWithSpace, pathWithSpace),
                           PipDataBuilder.CreatePipData(pathTable.StringTable, ":", PipDataFragmentEscaping.NoEscaping, pathWithSpace, pathWithSpace),
                           PipDataBuilder.CreatePipData(pathTable.StringTable, ":", PipDataFragmentEscaping.CRuntimeArgumentRules, pathWithSpace, pathWithSpace)),

                       new FingerprintingTypeDescriptor<WriteFileEncoding>(
                           WriteFileEncoding.Utf8,
                           WriteFileEncoding.Ascii),

                       new FingerprintingTypeDescriptor<SealDirectoryKind>(
                           SealDirectoryKind.Full, 
                           SealDirectoryKind.Partial, 
                           SealDirectoryKind.SourceAllDirectories, 
                           SealDirectoryKind.SourceTopDirectoryOnly, 
                           SealDirectoryKind.Opaque, 
                           SealDirectoryKind.SharedOpaque),

                       // File artifact arrays are special relative to other arrays due to a need for mutual-exclusion (the same file artifact cannot appear in an array with e.g. different content hashes).
                       new FingerprintingTypeDescriptor<ReadOnlyArray<FileArtifact>>(
                           ReadOnlyArray<FileArtifact>.Empty,
                           role => GenerateArrayVariants<FileArtifact>(
                               role,
                               GenerateMutuallyExclusiveFileArtifactEquivalenceClasses(
                                   pathTable, 
                                   paths, 
                                   role, 
                                   fingerprinterTestKind).ToArray()).Select(ec => ec.Cast<ReadOnlyArray<FileArtifact>>())),

                       // Directory artifact arrays are special relative to other arrays due to a need for mutual-exclusion (the same directory artifact cannot appear in an array with e.g. fingerprints).
                       new FingerprintingTypeDescriptor<ReadOnlyArray<DirectoryArtifact>>(
                           ReadOnlyArray<DirectoryArtifact>.Empty,
                           role => GenerateArrayVariants<DirectoryArtifact>(
                               role,
                               GenerateMutuallyExclusiveDirectoryArtifactEquivalenceClasses(
                                   pathTable, 
                                   paths, 
                                   role, 
                                   fingerprinterTestKind).ToArray()).Select(ec => ec.Cast<ReadOnlyArray<DirectoryArtifact>>())),
                       new FingerprintingTypeDescriptor<DoubleWritePolicy>(DoubleWritePolicy.DoubleWritesAreErrors, DoubleWritePolicy.UnsafeFirstDoubleWriteWins),
                       new FingerprintingTypeDescriptor<ContainerIsolationLevel>(
                           ContainerIsolationLevel.None, 
                           ContainerIsolationLevel.IsolateSharedOpaqueOutputDirectories, 
                           ContainerIsolationLevel.IsolateExclusiveOpaqueOutputDirectories, 
                           ContainerIsolationLevel.IsolateOutputFiles),
                   };
        }

        private static ReadOnlyArray<FileArtifactWithAttributes> GetFileArtifactWithAttributesVariantValues(AbsolutePath[] paths)
        {
            List<FileArtifactWithAttributes> sampleValues = new List<FileArtifactWithAttributes>();

            sampleValues.AddRange(paths.Select(p => FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(p), FileExistence.Required)));
            sampleValues.AddRange(paths.Select(p => FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(p), FileExistence.Temporary)));
            sampleValues.AddRange(paths.Select(p => FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(p), FileExistence.Optional)));

            return ReadOnlyArray<FileArtifactWithAttributes>.From(sampleValues);
        }

        private static IEnumerable<EquivalenceClass<FileArtifact>[]> GenerateMutuallyExclusiveFileArtifactEquivalenceClasses(
            PathTable pathTable,
            AbsolutePath[] paths,
            FingerprintingRole role,
            FingerprinterTestKind fingerprinterTestKind)
        {
            if (role == FingerprintingRole.None)
            {
                // If a file artifact is not significant for fingerprinting there's a single equivalence class for all paths, 
                // even given differing content hashes.

                var classes = new List<EquivalenceClass<FileArtifact>>();

                foreach (AbsolutePath p in paths)
                {
                    FileArtifact artifact = FileArtifact.CreateSourceFile(p);

                    classes.Add(EquivalenceClass<FileArtifact>.FromFileArtifact(artifact));
                    classes.Add(EquivalenceClass<FileArtifact>.FromFileArtifact(artifact, ContentHashingUtilities.ZeroHash));
                }

                yield return new[] {EquivalenceClass<FileArtifact>.Combine(classes.ToArray())};
            }
            else
            {
                // For semantic / content fingerprinting roles, distinct paths are always in separate equivalence classes 
                // (and those classes are not mutually exclusive).

                foreach (AbsolutePath p in paths)
                {
                    FileArtifact artifact = FileArtifact.CreateSourceFile(p);

                    if (role == FingerprintingRole.Semantic || fingerprinterTestKind == FingerprinterTestKind.Static)
                    {
                        // For semantic fingerprinting or static fingerprinter, the path of the file artifact matters but not the corresponding content.
                        // (changing the content of a path should does not result in a new equivalence class).

                        yield return new[]
                                     {
                                         EquivalenceClass<FileArtifact>.Combine(
                                            EquivalenceClass<FileArtifact>.FromFileArtifact(artifact),
                                            EquivalenceClass<FileArtifact>.FromFileArtifact(artifact, ContentHashingUtilities.ZeroHash))
                                     };
                    }
                    else if (role == FingerprintingRole.Content)
                    {
                        // For content fingerprinting, the path of the file artifact and its content hash are significant.
                        // Thus, changing the content hash results in a new equivalence class (but note the two classes are mutually-exclusive;
                        // i.e., an array cannot contain both)

                        yield return new[]
                                     {
                                         EquivalenceClass<FileArtifact>.FromFileArtifact(artifact),
                                         EquivalenceClass<FileArtifact>.FromFileArtifact(artifact, ContentHashingUtilities.ZeroHash)
                                     };
                    }
                    else
                    {
                        throw Contract.AssertFailure("Unhandled FingerprintingRole");
                    }
                }
            }
        }

        private static IEnumerable<EquivalenceClass<DirectoryArtifact>[]> GenerateMutuallyExclusiveDirectoryArtifactEquivalenceClasses(
            PathTable pathTable,
            AbsolutePath[] paths,
            FingerprintingRole role,
            FingerprinterTestKind fingerprinterTestKind)
        {
            if (role == FingerprintingRole.None)
            {
                // If a directory artifact is not significant for fingerprinting there's a single equivalence class for all paths, 
                // even given differing fingperprints.

                var classes = new List<EquivalenceClass<DirectoryArtifact>>();

                foreach (AbsolutePath p in paths)
                {
                    var artifact0 = DirectoryArtifact.CreateDirectoryArtifactForTesting(p, 0);
                    var artifact1 = DirectoryArtifact.CreateDirectoryArtifactForTesting(p, 1);
                    var fp0 = FingerprintUtilities.Hash(I($"{p.ToString(pathTable)}: [{p.ToString(pathTable)}/f0]"));
                    var fp1 = FingerprintUtilities.Hash(I($"{p.ToString(pathTable)}: [{p.ToString(pathTable)}/f1]"));

                    classes.Add(EquivalenceClass<FileArtifact>.FromDirectoryArtifact(artifact0));
                    classes.Add(EquivalenceClass<FileArtifact>.FromDirectoryArtifact(artifact0, new ContentFingerprint(fp0)));
                    classes.Add(EquivalenceClass<FileArtifact>.FromDirectoryArtifact(artifact1));
                    classes.Add(EquivalenceClass<FileArtifact>.FromDirectoryArtifact(artifact1, new ContentFingerprint(fp1)));
                }

                yield return new[] { EquivalenceClass<DirectoryArtifact>.Combine(classes.ToArray()) };
            }
            else
            {
                // For semantic / content fingerprinting roles, distinct paths are always in separate equivalence classes 
                // (and those classes are not mutually exclusive).

                foreach (AbsolutePath p in paths)
                {
                    var artifact0 = DirectoryArtifact.CreateDirectoryArtifactForTesting(p, 0);
                    var artifact1 = DirectoryArtifact.CreateDirectoryArtifactForTesting(p, 1);
                    var fp0 = FingerprintUtilities.Hash(I($"{p.ToString(pathTable)}: [{p.ToString(pathTable)}/f0]"));
                    var fp1 = FingerprintUtilities.Hash(I($"{p.ToString(pathTable)}: [{p.ToString(pathTable)}/f1]"));

                    if (role == FingerprintingRole.Semantic || fingerprinterTestKind == FingerprinterTestKind.Content)
                    {
                        // For semantic fingerprinting or content fingerprinter, the path of the directory artifact matters but not the corresponding members.
                        // (changing the members of a directory does not result in a new equivalence class).

                        yield return new[]
                                     {
                                         EquivalenceClass<DirectoryArtifact>.Combine(
                                            EquivalenceClass<DirectoryArtifact>.FromDirectoryArtifact(artifact0),
                                            EquivalenceClass<DirectoryArtifact>.FromDirectoryArtifact(artifact0, new ContentFingerprint(fp0)),
                                            EquivalenceClass<DirectoryArtifact>.FromDirectoryArtifact(artifact1),
                                            EquivalenceClass<DirectoryArtifact>.FromDirectoryArtifact(artifact1, new ContentFingerprint(fp1)))
                                     };
                    }
                    else if (role == FingerprintingRole.Content)
                    {
                        // For static fingerprinting, the path of the directory artifact and its members are significant.
                        // Thus, changing the members results in a new equivalence class (but note the classes are mutually-exclusive;
                        // i.e., an array cannot contain both)

                        yield return new[]
                                     {
                                         EquivalenceClass<DirectoryArtifact>.FromDirectoryArtifact(artifact0),
                                         EquivalenceClass<DirectoryArtifact>.FromDirectoryArtifact(artifact0, new ContentFingerprint(fp0)),
                                         EquivalenceClass<DirectoryArtifact>.FromDirectoryArtifact(artifact1),
                                         EquivalenceClass<DirectoryArtifact>.FromDirectoryArtifact(artifact1, new ContentFingerprint(fp1))
                                     };
                    }
                    else
                    {
                        throw Contract.AssertFailure("Unhandled FingerprintingRole");
                    }
                }
            }
        }

        private PipFingerprinter CreateDefaultContentFingerprinter<TPip>(VariationSource<TPip> variation) where TPip : Pip
        {
            return new PipContentFingerprinter(
                    m_context.PathTable,
                    variation.LookupContentHash);
        }

        private PipFingerprinter CreateDefaultStaticFingerprinter<TPip>(VariationSource<TPip> variation) where TPip : Pip
        {
            return new PipStaticFingerprinter(
                    m_context.PathTable,
                    variation.LookupDirectoryFingerprint,
                    variation.LookupDirectoryFingerprint);
        }

        private PipFingerprinter CreateSealDirectoryStaticFingerprinter<TPip>(VariationSource<TPip> variation) where TPip : Pip
        {
            return new PipStaticFingerprinter(
                    m_context.PathTable,
                    variation.LookupDirectoryFingerprint,
                    variation.LookupDirectoryFingerprint)
            {
                // For testing purpose, we exclude semi-stable hash on fingerprinting seal directories.
                ExcludeSemiStableHashOnFingerprintingSealDirectory = true
            };
        }

        private void VerifySinglePropertyVariationsAreCollisionFree<TPip>(
            PathTable pathTable,
            IFingerprintingTypeDescriptor[] fingerprintingTypeDescriptors,
            Func<VariationSource<TPip>, TPip> factory,
            Func<VariationSource<TPip>, PipFingerprinter> pipFingerprinterFactory,
            Func<PipFingerprinter, TPip, ContentFingerprint> computeFingerprint) where TPip : Pip
        {
            Contract.Requires(factory != null);

            // We ensure that each equivalence class has a unique fingerprint. We detect and report collisions
            // by mapping content fingerprints to their single generating class.
            var equivalenceClassesByHash = new Dictionary<ContentFingerprint, Tuple<PropertyInfo, IEquivalenceClass>>();

            // All variations are single-property extensions of this one.
            var baseVariation = new VariationSource<TPip>(m_context.PathTable, fingerprintingTypeDescriptors);

            // But to know what will be varied, we first need to experimentally run the factory.
            var variedProperties = new HashSet<PropertyInfo>(baseVariation.LearnVariedProperties(factory));

            // In order to usefully vary properties, we need to know their impact on fingerprinting. This requires a [PipCaching] attribute on varied properties.
            // Furthermore, we require that properties with a [PipCaching] attribute to be varied (or have a specified fingerprinting role of None); otherwise,
            // they clearly cannot satisfy their roles.
            var propertyTypeDescriptors = new Dictionary<PropertyInfo, IFingerprintingTypeDescriptor>();
            var propertyCachingAttributes = new Dictionary<PropertyInfo, PipCachingAttribute>();
            foreach (var property in GetPropertiesWithPipCachingAttributes<TPip>())
            {
                if (!variedProperties.Contains(property.Item1))
                {
                    XAssert.AreEqual(
                        FingerprintingRole.None,
                        property.Item2.FingerprintingRole,
                        "The property '{0}' was not varied by the given pip factory, but it has a fingerprinting role of '{1}'.",
                        property.Item1.Name,
                        property.Item2.FingerprintingRole);

                    continue;
                }

                // Note that we do not lookup type descriptors for those things marked as None and also not varied. We ignore those entirely,
                // and thus can get away with not providing a descriptor (e.g. for PipType).
                IFingerprintingTypeDescriptor typeDescriptor = GetFingerprintTypeDescriptor(
                    property.Item1.PropertyType,
                    fingerprintingTypeDescriptors);
                propertyTypeDescriptors.Add(property.Item1, typeDescriptor);
                propertyCachingAttributes.Add(property.Item1, property.Item2);
            }

            foreach (PropertyInfo variedProperty in variedProperties)
            {
                XAssert.IsTrue(
                    propertyTypeDescriptors.ContainsKey(variedProperty),
                    "The property '{0}' was varied by the given pip factory, and so must be annotated with a [PipCaching] attribute.",
                    variedProperty.Name);
            }

            // Here we fingeprrint the base variation. No other variations should collide with it.
            ContentFingerprint baseFingerprint;
            {
                TPip pip = factory(baseVariation);

                // TODO: Maybe test that version is included in the fingerprint.
                PipFingerprinter fingerprinter = pipFingerprinterFactory(baseVariation);

                baseFingerprint = computeFingerprint(fingerprinter, pip);
            }

            // Now, for each varied property we generate a series of new equivalence classes. For each class, we ensure that
            // (a) the equivalence class doesn't collide with an existing one and (b) all members of the class result in the same fingerprint.
            int variationCount = 0;
            foreach (PropertyInfo variedProperty in variedProperties)
            {
                PipCachingAttribute attr = propertyCachingAttributes[variedProperty];

                Console.WriteLine("Varying property {0} (role: {1})", variedProperty.Name, attr.FingerprintingRole);

                foreach (IEquivalenceClass equivClass in propertyTypeDescriptors[variedProperty].Generate(attr.FingerprintingRole))
                {
                    Console.WriteLine("Beginning equivalence class {0}", equivClass.ToString(pathTable));

                    ContentFingerprint? equivClassFingerprint = null;
                    foreach (Variation variation in equivClass.GenerateEquivalentVariations(variedProperty))
                    {
                        variationCount++;
                        Console.WriteLine("Applying variation {0} (#{1})", variation.ToString(pathTable), variationCount);

                        VariationSource<TPip> varied = baseVariation.WithVariation(variation);
                        TPip pip = factory(varied);

                        // TODO: Maybe test that version is included in the fingerprint.
                        var fingerprinter = pipFingerprinterFactory(varied);
                        ContentFingerprint thisVariationFingerprint = computeFingerprint(fingerprinter, pip);

                        if (thisVariationFingerprint == baseFingerprint && attr.FingerprintingRole != FingerprintingRole.None)
                        {
                            XAssert.Fail(
                                "Variation {0} (#{1}) collided with the base variation. It is likely that this property does not influence the fingerprint at all.",
                                variation.ToString(pathTable),
                                variationCount);
                        }
                        else if (thisVariationFingerprint != baseFingerprint && attr.FingerprintingRole == FingerprintingRole.None)
                        {
                            XAssert.Fail(
                                "Variation {0} (#{1}) diverged from the base variation, but is not supposed to influence the fingerprint.",
                                variation.ToString(pathTable),
                                variationCount);
                        }
                        else if (equivClassFingerprint.HasValue)
                        {
                            if (equivClassFingerprint.Value != thisVariationFingerprint)
                            {
                                XAssert.Fail(
                                    "Variation {0} (#{1}) unexpectedly diverged from the fingerprint of its equivalence class {2}",
                                    variation.ToString(pathTable),
                                    variationCount,
                                    equivClass.ToString(pathTable));
                            }
                        }
                        else
                        {
                            equivClassFingerprint = thisVariationFingerprint;

                            Tuple<PropertyInfo, IEquivalenceClass> collided;
                            bool collision = equivalenceClassesByHash.TryGetValue(thisVariationFingerprint, out collided);
                            if (collision)
                            {
                                XAssert.Fail(
                                    "Variation (first in its class) {0} (#{1}) collided with existing equivalence class {2} from property {3}",
                                    variation.ToString(pathTable),
                                    variationCount,
                                    collided.Item2.ToString(pathTable),
                                    collided.Item1.Name);
                            }
                        }
                    }

                    // All variations of this class have been validated. That means future classes can collide with it (it cannot collide with itself).
                    if (equivClassFingerprint != baseFingerprint)
                    {
                        equivalenceClassesByHash.Add(equivClassFingerprint.Value, Tuple.Create(variedProperty, equivClass));
                        Console.WriteLine("Recorded equivalence class {0} with fingerprint {1}", equivClass.ToString(pathTable), equivClassFingerprint);
                    }
                    else
                    {
                        Console.WriteLine("Recorded {0} as part of the base equivalence class", equivClass.ToString(pathTable));
                    }
                }
            }
        }

        private static IFingerprintingTypeDescriptor GetFingerprintTypeDescriptor(Type type, IFingerprintingTypeDescriptor[] descriptors)
        {
            if (type == typeof(ReadOnlyArray<AbsolutePath>))
            {
                return new FingerprintingTypeDescriptor<ReadOnlyArray<AbsolutePath>>(
                    baseVal: ReadOnlyArray<AbsolutePath>.Empty,
                    generateClasses: role => GenerateArrayVariants<AbsolutePath>(descriptors, role));
            }
            
            if (type == typeof(ReadOnlyArray<int>))
            {
                return new FingerprintingTypeDescriptor<ReadOnlyArray<int>>(
                    baseVal: ReadOnlyArray<int>.Empty,
                    generateClasses: role => GenerateArrayVariants<int>(descriptors, role));
            }
            
            if (type == typeof(ReadOnlyArray<string>))
            {
                return new FingerprintingTypeDescriptor<ReadOnlyArray<string>>(
                    baseVal: ReadOnlyArray<string>.Empty,
                    generateClasses: role => GenerateArrayVariants<string>(descriptors, role));
            }
            
            if (type == typeof(ReadOnlyArray<StringId>))
            {
                return new FingerprintingTypeDescriptor<ReadOnlyArray<StringId>>(
                    baseVal: ReadOnlyArray<StringId>.Empty,
                    generateClasses: role => GenerateArrayVariants<StringId>(descriptors, role));
            }
            
            if (type == typeof(ReadOnlyArray<EnvironmentVariable>))
            {
                return new FingerprintingTypeDescriptor<ReadOnlyArray<EnvironmentVariable>>(
                    baseVal: ReadOnlyArray<EnvironmentVariable>.Empty,
                    generateClasses: role => GenerateArrayVariants<EnvironmentVariable>(descriptors, role));
            }

            if (type == typeof(ReadOnlyArray<FileArtifactWithAttributes>))
            {
                return new FingerprintingTypeDescriptor<ReadOnlyArray<FileArtifactWithAttributes>>(
                    baseVal: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    generateClasses: role => GenerateArrayVariants<FileArtifactWithAttributes>(descriptors, role));
            }

            if (type == typeof(SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>))
            {
                return new FingerprintingTypeDescriptor<SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>>(
                    baseVal: SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.FromSortedArrayUnsafe(
                        ReadOnlyArray<FileArtifact>.Empty,
                        OrdinalFileArtifactComparer.Instance),
                    generateClasses: role => GenerateArrayVariants<FileArtifact>(descriptors, role)
                    .Select(ec => new EquivalenceClass<SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>>(
                        ec.Values
                            .Cast<ReadOnlyArray<FileArtifact>>()
                            .Select(arr => SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>
                            .CloneAndSort(arr, OrdinalFileArtifactComparer.Instance)),
                        ec.ContentHashOverlays,
                        ec.FingerprintOverlays)));
            }

            IFingerprintingTypeDescriptor typeDescriptor = descriptors.SingleOrDefault(td => type.IsAssignableFrom(td.ValueType));
            
            Contract.Assume(
                typeDescriptor != null,
                "No type descriptor was found for " + type.FullName +
                "; each type of property on a pip must have a corresponding descriptor to generate test values.");

            return typeDescriptor;
        }

        /// <summary>
        /// Generates variants of an array property by constructing arrays (of varying sizes) from equivalence-class subsequences.
        /// For example, if the 'int' type has variants 1, 2, 3 and base 0 in separate equivalence classes, the following array
        /// equivalent-classes would be generated:
        /// {[1]}, {[2]}, {[3]}, {[1, 2]}, {[1, 3]}, {[2, 3]}, {[1, 2, 3]}
        /// (note that the base value is not used for subsequence creation)
        /// Now consider equivalence classes {1, 1.1}, {2, 2.1}; the array classes {[1]}, {[2]}, {[1, 2], [1.1, 2.1]} would be
        /// generated
        /// (note that the last case verifies fingerprint equivalence when replacing several inner values with equivalent ones).
        /// </summary>
        private static IEnumerable<EquivalenceClass<ReadOnlyArray<T>>> GenerateArrayVariants<T>(
            IFingerprintingTypeDescriptor[] typeDescriptors,
            FingerprintingRole arrayRole)
        {
            IFingerprintingTypeDescriptor elementTypeDescriptor = GetFingerprintTypeDescriptor(typeof(T), typeDescriptors);
            IEquivalenceClass[] classes = elementTypeDescriptor.Generate(arrayRole).ToArray();

            // For arrays other than FileArtifact[], there's no mutual exclusion required among classes.
            IEquivalenceClass[][] mutexGroups = classes.Select(singleClass => new IEquivalenceClass[] { singleClass }).ToArray();
            return GenerateArrayVariants<T>(arrayRole, mutexGroups);
        }

        /// <summary>
        /// Generates variants of an array property by constructing arrays (of varying sizes) from equivalence-class subsequences.
        /// For example, if the 'int' type has variants 1, 2, 3 and base 0 in separate equivalence classes, the following array
        /// equivalent-classes would be generated:
        /// {[1]}, {[2]}, {[3]}, {[1, 2]}, {[1, 3]}, {[2, 3]}, {[1, 2, 3]}
        /// (note that the base value is not used for subsequence creation)
        /// Now consider equivalence classes {1, 1.1}, {2, 2.1}; the array classes {[1]}, {[2]}, {[1, 2], [1.1, 2.1]} would be
        /// generated
        /// (note that the last case verifies fingerprint equivalence when replacing several inner values with equivalent ones).
        /// To support special rules around FileArtifact arrays (in particular that the same artifact cannot appear twice with
        /// different hashes in an array),
        /// there is an additional notion of mutual-exclusion among equivalence classes. For two file artifacts F and G with hashes
        /// H1 and H2:
        /// {F@H1} | {F@H2}, {G@H1} | {G@H2} (where | indicates mutual exclusion) would generate
        /// {[F@H1]}, {[G@H1]}, {[F@H1, G@H1]}, {[F@H1, G@H2]}, {[F@H2, G@H1]}, {[F@H1, G@H2]}
        /// (note that duplicate file artifacts do not appear, but all combinations of the mutual-exclusion groups is generated).
        /// </summary>
        private static IEnumerable<EquivalenceClass<ReadOnlyArray<T>>> GenerateArrayVariants<T>(
            FingerprintingRole arrayRole,
            IEquivalenceClass[][] mutuallyExclusiveEquivalenceClassGroups)
        {
            // TODO: Maybe annotate for and test handling of duplicates, Nul, order dependence, etc.

            // (1) Array length.
            // For each mutex group, we will pick one member (the picked classes are thus compatible); the number of mutex groups is thus the number of classes
            // from which we pick values (and thus bounds the subsequence length).
            for (int size = 1; size <= Math.Min(mutuallyExclusiveEquivalenceClassGroups.Length, 4); size++)
            {
                // (2) All small subsequences of mutex groups
                foreach (var mutexGroupSubsequence in Combinatorics.Choose(size, mutuallyExclusiveEquivalenceClassGroups))
                {
                    // (3) All equivalence class sequences which satisfy the mutex property.
                    foreach (var compatibleClasses in Combinatorics.CartesianProduct(mutexGroupSubsequence))
                    {
                        // Each equivalence class has zero or more artifact -> hash overlays. We should be able to merge these
                        // mappings cleanly, since only mutually-exclusive equivalence classes should overlap that way.
                        var contentHashOverlays = new Dictionary<FileArtifact, ContentHash>();
                        var fingerprintOverlays = new Dictionary<FileOrDirectoryArtifact, ContentFingerprint>();
                        foreach (IEquivalenceClass ec in compatibleClasses)
                        {
                            if (ec.ContentHashOverlays != null)
                            {
                                foreach (var kvp in ec.ContentHashOverlays)
                                {
                                    Contract.Assume(
                                        !contentHashOverlays.ContainsKey(kvp.Key),
                                        "The same file artifact cannot appear in the same mutex group (when a hash overlay is present)");
                                    contentHashOverlays.Add(kvp.Key, kvp.Value);
                                }
                            }

                            if (ec.FingerprintOverlays != null)
                            {
                                foreach (var kvp in ec.FingerprintOverlays)
                                {
                                    Contract.Assume(
                                        !fingerprintOverlays.ContainsKey(kvp.Key),
                                        "The same file/directory artifact cannot appear in the same mutex group (when a fingerprint overlay is present)");
                                    fingerprintOverlays.Add(kvp.Key, kvp.Value);
                                }
                            }
                        }

                        var equivalentArrays = new List<ReadOnlyArray<T>>();

                        // (4) Various sequences of concrete values from the equivalence class subsequence.
                        foreach (var equivalenceClassSubsequenceSamples in Combinatorics.ZipLongest(compatibleClasses, ec => ec.Values))
                        {
                            T[] sampleArray = new T[size];
                            for (int i = 0; i < size; i++)
                            {
                                sampleArray[i] = (T)equivalenceClassSubsequenceSamples[i];
                            }

                            equivalentArrays.Add(ReadOnlyArray<T>.FromWithoutCopy(sampleArray));
                        }

                        yield return new EquivalenceClass<ReadOnlyArray<T>>(equivalentArrays, contentHashOverlays, fingerprintOverlays);
                    }
                }
            }
        }

        private static IEnumerable<Tuple<PropertyInfo, PipCachingAttribute>> GetPropertiesWithPipCachingAttributes<TPip>() where TPip : Pip
        {
            foreach (PropertyInfo member in typeof(TPip).GetProperties())
            {
                Contract.Assume(member.ReflectedType == typeof(TPip), "PropertyInfos must agree on reflected type (see GetPropertyInfo)");
                var attr = member.GetCustomAttribute(typeof(PipCachingAttribute), inherit: true) as PipCachingAttribute;
                if (attr != null)
                {
                    yield return Tuple.Create(member, attr);
                }
            }
        }

        private interface IFingerprintingTypeDescriptor
        {
            Type ValueType { get; }

            object Base { get; }

            IEnumerable<IEquivalenceClass> Generate(FingerprintingRole role);
        }

        private sealed class FingerprintingTypeDescriptor<TValue> : IFingerprintingTypeDescriptor
        {
            private readonly TValue m_base;
            private readonly Func<FingerprintingRole, IEnumerable<EquivalenceClass<TValue>>> m_generateEquivalenceClasses;

            public FingerprintingTypeDescriptor(TValue baseVal, params EquivalenceClass<TValue>[] classes)
            {
                m_base = baseVal;
                m_generateEquivalenceClasses = role => classes;
            }

            public FingerprintingTypeDescriptor(TValue baseVal, params TValue[] variantValues)
            {
                m_base = baseVal;
                m_generateEquivalenceClasses = 
                    role =>
                    {
                        if (role == FingerprintingRole.None)
                        {
                            // None of the values should affect the fingerprint, so they are all in the same equivalence class.
                            return new[] {new EquivalenceClass<TValue>(variantValues)};
                        }

                        // We assume that each variant has a semantic meaning (and role is Semantic or Content), so each
                        // value is in its own equivalence class.
                        return variantValues.Select(v => new EquivalenceClass<TValue>(new[] {v}));
                    };
            }

            public FingerprintingTypeDescriptor(TValue baseVal, ReadOnlyArray<TValue> variantValues)
            {
                m_base = baseVal;
                m_generateEquivalenceClasses = 
                    role =>
                    {
                        if (role == FingerprintingRole.None)
                        {
                            // None of the values should affect the fingerprint, so they are all in the same equivalence class.
                            return new[] {new EquivalenceClass<TValue>(variantValues)};
                        }

                        // We assume that each variant has a semantic meaning (and role is Semantic or Content), so each
                        // value is in its own equivalence class.
                        return variantValues.Select(v => new EquivalenceClass<TValue>(new[] {v}));
                    };
            }

            public FingerprintingTypeDescriptor(TValue baseVal, Func<FingerprintingRole, IEnumerable<EquivalenceClass<TValue>>> generateClasses)
            {
                Contract.Requires(generateClasses != null);

                m_base = baseVal;
                m_generateEquivalenceClasses = generateClasses;
            }

            public object Base => m_base;

            public Type ValueType => typeof(TValue);

            public IEnumerable<IEquivalenceClass> Generate(FingerprintingRole role) => m_generateEquivalenceClasses(role).Select(c => (IEquivalenceClass)c);
        }

        private interface IEquivalenceClass
        {
            object[] Values { get; }

            Dictionary<FileArtifact, ContentHash> ContentHashOverlays { get; }

            Dictionary<FileOrDirectoryArtifact, ContentFingerprint> FingerprintOverlays { get; }

            IEnumerable<Variation> GenerateEquivalentVariations(PropertyInfo property);

            string ToString(PathTable pathTable);
        }

        private sealed class EquivalenceClass<TValue> : IEquivalenceClass
        {
            private readonly object[] m_values;
            private readonly Dictionary<FileArtifact, ContentHash> m_contentHashOverlays;
            private readonly Dictionary<FileOrDirectoryArtifact, ContentFingerprint> m_fingerprintOverlays;

            public EquivalenceClass(
                IEnumerable<TValue> values, 
                Dictionary<FileArtifact, ContentHash> contentHashOverlays = null, 
                Dictionary<FileOrDirectoryArtifact, ContentFingerprint> fingerprintOverlays = null)
            {
                m_values = values.Select(v => (object)v).ToArray();
                m_contentHashOverlays = contentHashOverlays;
                m_fingerprintOverlays = fingerprintOverlays;
            }

            public object[] Values => m_values;

            public Dictionary<FileArtifact, ContentHash> ContentHashOverlays => m_contentHashOverlays;

            public Dictionary<FileOrDirectoryArtifact, ContentFingerprint> FingerprintOverlays => m_fingerprintOverlays;

            public IEnumerable<Variation> GenerateEquivalentVariations(PropertyInfo property) => 
                m_values.Select(value => new Variation(property, value, m_contentHashOverlays, m_fingerprintOverlays));

            /// <summary>
            /// Casts this equivalence class to <typeparamref name="TTarget" />, which should be a subtype of
            /// <typeparamref name="TValue" />.
            /// </summary>
            /// <remarks>
            /// This is to work around a lack of static array typing in GenerateArrayVariants.
            /// </remarks>
            public EquivalenceClass<TTarget> Cast<TTarget>() => new EquivalenceClass<TTarget>(m_values.Cast<TTarget>(), m_contentHashOverlays, m_fingerprintOverlays);

            public static EquivalenceClass<FileArtifact> FromFileArtifact(
                FileArtifact artifact, 
                ContentHash? contentHash = null, 
                ContentFingerprint? fingerprint = null)
            {
                var contentHashOverlays = contentHash.HasValue
                    ? new Dictionary<FileArtifact, ContentHash> { { artifact, contentHash.Value } }
                    : null;

                var fingerprintOverlays = fingerprint.HasValue
                    ? new Dictionary<FileOrDirectoryArtifact, ContentFingerprint> { { artifact, fingerprint.Value } }
                    : null;

                return new EquivalenceClass<FileArtifact>(
                    new[] { artifact }, 
                    contentHashOverlays: contentHashOverlays, 
                    fingerprintOverlays: fingerprintOverlays);
            }

            public static EquivalenceClass<DirectoryArtifact> FromDirectoryArtifact(
                DirectoryArtifact artifact,
                ContentFingerprint? fingerprint = null)
            {
                var fingerprintOverlays = fingerprint.HasValue
                    ? new Dictionary<FileOrDirectoryArtifact, ContentFingerprint> { { artifact, fingerprint.Value } }
                    : null;

                return new EquivalenceClass<DirectoryArtifact>(new[] { artifact }, fingerprintOverlays: fingerprintOverlays);
            }

            public static EquivalenceClass<TValue> Combine(params EquivalenceClass<TValue>[] classes)
            {
                var contentHashOverlays = new Dictionary<FileArtifact, ContentHash>();
                var fingerprintOverlays = new Dictionary<FileOrDirectoryArtifact, ContentFingerprint>();
                var values = new List<TValue>();

                foreach (var ec in classes)
                {
                    if (ec.ContentHashOverlays != null)
                    {
                        foreach (var kvp in ec.ContentHashOverlays)
                        {
                            Contract.Assume(
                                !contentHashOverlays.ContainsKey(kvp.Key),
                                "Equivalence classes with conflicting content hash overlays cannot be merged.");
                            contentHashOverlays.Add(kvp.Key, kvp.Value);
                        }
                    }

                    if (ec.FingerprintOverlays != null)
                    {
                        foreach (var kvp in ec.FingerprintOverlays)
                        {
                            Contract.Assume(
                                !fingerprintOverlays.ContainsKey(kvp.Key),
                                "Equivalence classes with conflicting fingerprint overlays cannot be merged.");
                            fingerprintOverlays.Add(kvp.Key, kvp.Value);
                        }
                    }

                    values.AddRange(ec.Values.Cast<TValue>());
                }

                return new EquivalenceClass<TValue>(values, contentHashOverlays, fingerprintOverlays);
            }

            public string ToString(PathTable pathTable) => 
                I($"Equivalence ({typeof(TValue).Name}) <{Variation.GetValueString(pathTable, m_values, m_contentHashOverlays, m_fingerprintOverlays)}>");
        }

        private sealed class Variation
        {
            private readonly PropertyInfo m_property;
            private readonly object m_value;
            private readonly Dictionary<FileArtifact, ContentHash> m_contentHashOverlays;
            private readonly Dictionary<FileOrDirectoryArtifact, ContentFingerprint> m_fingerprintOverlays;

            public Variation(
                PropertyInfo property, 
                object value, 
                Dictionary<FileArtifact, ContentHash> hashOverlays = null,
                Dictionary<FileOrDirectoryArtifact, ContentFingerprint> fingerprintOverlays = null)
            {
                m_property = property;
                m_value = value;
                m_contentHashOverlays = hashOverlays;
                m_fingerprintOverlays = fingerprintOverlays;
            }

            public PropertyInfo Property => m_property;

            public object Value => m_value;

            public Dictionary<FileArtifact, ContentHash> ContentHashOverlays => m_contentHashOverlays;

            public Dictionary<FileOrDirectoryArtifact, ContentFingerprint> FingerprintOverlays => m_fingerprintOverlays;

            public string ToString(PathTable pathTable)
            {
                int contentHashOverlaysCount = m_contentHashOverlays != null ? m_contentHashOverlays.Count : 0;
                int fingerprintOverlaysCount = m_fingerprintOverlays != null ? m_fingerprintOverlays.Count : 0;
                return I($"[{m_property.Name}] => '{GetValueString(pathTable, m_value, m_contentHashOverlays, m_fingerprintOverlays)}' ({contentHashOverlaysCount} content hash overlays, {fingerprintOverlaysCount} fingerprint overlays)");
            }

            internal static string GetValueString(
                PathTable pathTable,
                object value,
                Dictionary<FileArtifact, ContentHash> contentHashOverlays = null,
                Dictionary<FileOrDirectoryArtifact, ContentFingerprint> fingerprintOverlays = null)
            {
                if (value is FileArtifact fileArtifact)
                {
                    var contentHashString = contentHashOverlays != null && contentHashOverlays.TryGetValue(fileArtifact, out ContentHash contentHash)
                        ? contentHash.ToString()
                        : "default";
                    var fingerprintString = fingerprintOverlays != null && fingerprintOverlays.TryGetValue(fileArtifact, out ContentFingerprint fingerprint)
                        ? fingerprint.ToString()
                        : "default";

                    return I($"{fileArtifact.Path.ToString(pathTable)}:{fileArtifact.RewriteCount} (file) @ (content hash: {contentHashString}, fingerprint: {fingerprintString})");
                }

                if (value is DirectoryArtifact directoryArtifact)
                {
                    var fingerprintString = fingerprintOverlays != null && fingerprintOverlays.TryGetValue(directoryArtifact, out ContentFingerprint fingerprint)
                        ? fingerprint.ToString()
                        : "default";

                    return I($"{directoryArtifact.Path.ToString(pathTable)}:{directoryArtifact.PartialSealId} (directory) @ (fingerprint: {fingerprintString})");
                }

                if (value is AbsolutePath path)
                {
                    return I($"{path.ToString(pathTable)}");
                }

                if (value is StringId stringId)
                {
                    return I($"{stringId.ToString(pathTable.StringTable)}");
                }

                if (value is ReadOnlyArray<FileArtifact> fileArtifacts)
                {
                    return "{" + string.Join(", ", fileArtifacts.Select(v => GetValueString(pathTable, v, contentHashOverlays, fingerprintOverlays))) + "}";
                }

                if (value is ReadOnlyArray<DirectoryArtifact> directoryArtifacts)
                {
                    return "{" + string.Join(", ", directoryArtifacts.Select(v => GetValueString(pathTable, v, contentHashOverlays, fingerprintOverlays))) + "}";
                }

                if (value is SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> sortedFileArtifacts)
                {
                    return "{" + string.Join(", ", sortedFileArtifacts.Select(v => GetValueString(pathTable, v, contentHashOverlays, fingerprintOverlays))) + "}";
                }

                if (value.GetType().IsArray)
                {
                    return "{" + string.Join(", ", ((Array)value).Cast<object>().Select(v => GetValueString(pathTable, v, contentHashOverlays, fingerprintOverlays))) + "}";
                }

                return value.ToString();
            }

            public static bool IsListOfT(TypeInfo type) => type.IsGenericType && type.GetGenericTypeDefinition().TypeHandle.Equals(typeof(List<>).TypeHandle);

            public static bool IsReadOnlyListOfT(TypeInfo type) => type.IsGenericType && type.GetGenericTypeDefinition().TypeHandle.Equals(typeof(IReadOnlyList<>).TypeHandle);
        }
        
        private sealed class VariationSource<TPip>
            where TPip : Pip
        {
            private readonly IDictionary<PropertyInfo, object> m_baseValues;
            private readonly Variation m_overlayVariation;
            private readonly IFingerprintingTypeDescriptor[] m_fingerprintingTypeDescriptors;
            private readonly PathTable m_pathTable;
            private bool m_learning;

            public VariationSource(PathTable pathTable, IFingerprintingTypeDescriptor[] fingerprintingTypeDescriptors)
            {
                Contract.Requires(pathTable != null);
                Contract.Requires(fingerprintingTypeDescriptors != null);

                m_pathTable = pathTable;
                m_baseValues = new Dictionary<PropertyInfo, object>();
                m_fingerprintingTypeDescriptors = fingerprintingTypeDescriptors;
                m_overlayVariation = null;
            }

            private VariationSource(
                PathTable pathTable,
                IFingerprintingTypeDescriptor[] fingerprintingTypeDescriptors,
                IDictionary<PropertyInfo, object> baseValues,
                Variation overlayVariation)
            {
                Contract.Requires(pathTable != null);
                Contract.Requires(fingerprintingTypeDescriptors != null);
                Contract.Requires(baseValues != null);
                Contract.Requires(overlayVariation != null);

                Contract.Assume(baseValues.ContainsKey(overlayVariation.Property));
                m_baseValues = baseValues;
                m_fingerprintingTypeDescriptors = fingerprintingTypeDescriptors;
                m_pathTable = pathTable;
                m_overlayVariation = overlayVariation;
            }

            public PathTable PathTable => m_pathTable;

            public VariationSource<TPip> WithVariation(Variation variation)
            {
                Contract.Requires(variation != null);

                return new VariationSource<TPip>(m_pathTable, m_fingerprintingTypeDescriptors, m_baseValues, variation);
            }

            public TResult Vary<TResult>(Expression<Func<TPip, TResult>> expr)
            {
                PropertyInfo exprProperty = GetPropertyInfo(expr);

                if (m_learning)
                {
                    IFingerprintingTypeDescriptor typeDescriptor = GetFingerprintTypeDescriptor(
                        exprProperty.PropertyType,
                        m_fingerprintingTypeDescriptors);
                    m_baseValues.Add(exprProperty, typeDescriptor.Base);
                }
                else
                {
                    Contract.Assume(
                        m_baseValues.ContainsKey(exprProperty),
                        "Requested a property which was not learned during LearnVariedProperties: " + exprProperty.Name);
                }

                object value = (m_overlayVariation != null && m_overlayVariation.Property == exprProperty)
                    ? m_overlayVariation.Value
                    : m_baseValues[exprProperty];

                return (TResult)value;
            }

            /// <summary>
            /// Calls the given factory function with this variation source such that the set of properties varied by the factory is
            /// learned.
            /// This learning phase must be performed before the factory can be called normally, such as with overlaid variations.
            /// </summary>
            public ICollection<PropertyInfo> LearnVariedProperties(Func<VariationSource<TPip>, TPip> factory)
            {
                Contract.Requires(factory != null);

                Contract.Assume(m_learning == false && m_baseValues.Count == 0, "Should only call LearnVariedProperties once.");
                m_learning = true;

                try
                {
                    Analysis.IgnoreResult(factory(this));
                }
                finally
                {
                    m_learning = false;
                }

                return m_baseValues.Keys;
            }

            public FileContentInfo LookupContentHash(FileArtifact artifact)
            {
                var hash = 
                    m_overlayVariation != null && 
                    m_overlayVariation.ContentHashOverlays != null && 
                    m_overlayVariation.ContentHashOverlays.TryGetValue(artifact, out ContentHash contentHash)
                    ? contentHash
                    : GetDefaultHash(artifact);

                return FileContentInfo.CreateWithUnknownLength(hash);
            }

            public ContentFingerprint? LookupArtifactFingerprint(FileOrDirectoryArtifact artifact)
            {
                return m_overlayVariation != null &&
                       m_overlayVariation.FingerprintOverlays != null &&
                       m_overlayVariation.FingerprintOverlays.TryGetValue(artifact, out ContentFingerprint fingerprint)
                       ? fingerprint
                       : GetDefaultFingerprint(artifact);
            }

            public ContentFingerprint LookupDirectoryFingerprint(DirectoryArtifact directory) => LookupArtifactFingerprint(directory) ?? ContentFingerprint.Zero;

            private static ContentHash GetDefaultHash(FileArtifact artifact)
            {
                Contract.Requires(artifact.IsValid);

                int hashCode = artifact.GetHashCode();
                var hashBytes = new byte[ContentHashingUtilities.HashInfo.ByteLength];
                hashBytes[0] = (byte)(hashCode & 0xFF);
                hashBytes[1] = (byte)((hashCode >> 8) & 0xFF);
                hashBytes[2] = (byte)((hashCode >> 16) & 0xFF);
                hashBytes[3] = (byte)((hashCode >> 24) & 0xFF);

                return ContentHashingUtilities.CreateFrom(hashBytes);
            }

            private static ContentFingerprint GetDefaultFingerprint(FileOrDirectoryArtifact artifact)
            {
                Contract.Requires(artifact.IsValid);

                int hashCode = artifact.IsFile ? artifact.FileArtifact.GetHashCode() : artifact.DirectoryArtifact.GetHashCode();
                var hashBytes = new byte[FingerprintUtilities.FingerprintLength];
                hashBytes[0] = (byte)(hashCode & 0xFF);
                hashBytes[1] = (byte)((hashCode >> 8) & 0xFF);
                hashBytes[2] = (byte)((hashCode >> 16) & 0xFF);
                hashBytes[3] = (byte)((hashCode >> 24) & 0xFF);

                return new ContentFingerprint(new Fingerprint(hashBytes));
            }

            private static PropertyInfo GetPropertyInfo<TResult>(Expression<Func<TPip, TResult>> expr)
            {
                Contract.Requires(expr != null);
                Contract.Ensures(Contract.Result<PropertyInfo>() != null);

                var memberExpr = expr.Body as MemberExpression;
                Contract.Assume(memberExpr != null, "Expressions given to Vary must be simple member accesses. Don't compute anything.");
                var propertyInfo = memberExpr.Member as PropertyInfo;
                Contract.Assume(propertyInfo != null, "Expressions given to Vary must access a property.");

                // PropertyInfo instances can differ in ReflectedType (i.e., the type actually used to fetch the PropertyInfo).
                // Since elsewhere we use PropertyInfo for comparison, we need to make sure that we uniformly use PropertyInfos
                // as retrieved from TPip rather than a base, such as Pip itself. PropertyInfos from lambdas tend can be the latter,
                // whereas properties retrieved via typeof(TPip).GetProperties() is the former.
                propertyInfo = typeof(TPip).GetProperty(propertyInfo.Name);
                Contract.Assume(
                    propertyInfo.ReflectedType == typeof(TPip),
                    "PropertyInfos must agree on reflected type (see GetPropertiesWithPipCachingAttributes)");

                return propertyInfo;
            }
        }
    }

    internal static class Combinatorics
    {
        /// <summary>
        /// This returns all possible count-length subsequences of seq.
        /// There are 'seq.Length choose count' such subsequences.
        /// </summary>
        public static IEnumerable<T[]> Choose<T>(int count, T[] seq, int valIndex = 0, int resultSize = 0)
        {
            Contract.Requires(count >= 0 && count <= seq.Length);

            return ChooseInternal(count, seq, 0, 0);
        }

        /// <summary>
        /// This returns all possible count-length subsequences of seq starting at valIndex 
        /// There are 'seq.Length choose count' such subsequences.
        /// Each returned subsequence is a concatenation of two parts:
        /// - Head(seq) :: Choose(count - 1, Tail(seq)) (i.e., suppose head is chosen)
        /// - Choose(count, Tail(seq)) (i.e., suppose it isn't)
        /// </summary>
        private static IEnumerable<T[]> ChooseInternal<T>(int count, T[] seq, int valIndex, int resultIndex)
        {
            Contract.Requires(count >= 0 && count <= seq.Length);
            Contract.Requires(count == 0 || (valIndex >= 0 && valIndex < seq.Length));
            Contract.Requires(resultIndex >= 0);

            if (count == 0)
            {
                yield return new T[resultIndex];
                yield break;
            }

            // Including this value
            foreach (var partialResult in ChooseInternal(count - 1, seq, valIndex: valIndex + 1, resultIndex: resultIndex + 1))
            {
                partialResult[resultIndex] = seq[valIndex];
                yield return partialResult;
            }

            // Only skip this value if there's a successor to fill that empty slot.
            if (valIndex + count < seq.Length)
            {
                // Skipping this value
                foreach (var partialResult in ChooseInternal(count, seq, valIndex: valIndex + 1, resultIndex: resultIndex))
                {
                    yield return partialResult;
                }
            }
        }

        public static IEnumerable<TResult[]> ZipLongest<TResult, TSeq>(TSeq[] sequences, Func<TSeq, TResult[]> resultSelector)
        {
            var buffer = new TResult[sequences.Length];

            for (int valIdx = 0;; valIdx++)
            {
                bool newValueUsed = false;

                for (int seqIdx = 0; seqIdx < sequences.Length; seqIdx++)
                {
                    TResult[] sequence = resultSelector(sequences[seqIdx]);
                    if (valIdx < sequence.Length)
                    {
                        newValueUsed = true;
                    }

                    buffer[seqIdx] = sequence[Math.Min(valIdx, sequence.Length - 1)];
                }

                if (newValueUsed)
                {
                    yield return buffer.ToArray();
                }
                else
                {
                    yield break;
                }
            }
        }

        /// <summary>
        /// For a sequence of sets, generates the cartesian product (e.g. [a, b] * [c, d] * [e, f] => [a, c, e], [a, c, f], [a, d,
        /// e]...)
        /// This is like having a dynamic nesting of 'for' loops generating indices i,j,k...
        /// </summary>
        /// <remarks>
        /// The approach below assigns an index to each possible combination (of which there are ∏(set.Length)) and enumerates over
        /// them (i.e., the state required is a single integer). This is most easily understood assuming that each set has a 
        /// power-of-two length; each set has a sub-index of zero or more bits, and we break apart the combined index via 
        /// repeated shifting and masking. We generalize that approach to arbitrary set lengths by instead using repeated 
        /// division and mod.
        /// </remarks>
        public static IEnumerable<T[]> CartesianProduct<T>(T[][] sets)
        {
            int combinations = 1;
            for (int i = 0; i < sets.Length; i++)
            {
                combinations = checked(combinations * sets[i].Length);
            }

            for (int combinationIndex = 0; combinationIndex < combinations; combinationIndex++)
            {
                var result = new T[sets.Length];
                int shiftedCombinationIndex = combinationIndex;
                for (int setIndex = 0; setIndex < sets.Length; setIndex++)
                {
                    result[setIndex] = sets[setIndex][shiftedCombinationIndex % sets[setIndex].Length];
                    shiftedCombinationIndex /= sets[setIndex].Length;
                }

                yield return result;
            }
        }
    }
}
