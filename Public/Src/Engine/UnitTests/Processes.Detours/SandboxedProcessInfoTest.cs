// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Processes;
using BuildXL.Processes.Sideband;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.BuildParameters;

namespace Test.BuildXL.Processes.Detours
{
    public class SandboxedProcessInfoTest : XunitBuildXLTest
    {
        public SandboxedProcessInfoTest(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SerializeSandboxedProcessInfo(bool useNullFileStorage)
        {
            var pt = new PathTable();
            var fam =
                new FileAccessManifest(pt, CreateDirectoryTranslator())
                {
                    FailUnexpectedFileAccesses = false,
                    IgnoreCodeCoverage = false,
                    ReportFileAccesses = false,
                    ReportUnexpectedFileAccesses = false,
                    MonitorChildProcesses = false
                };

            var vac = new ValidationDataCreator(fam, pt);
            vac.AddScope(A("C", "Users", "AppData"), FileAccessPolicy.AllowAll);
            vac.AddPath(A("C", "Source", "source.txt"), FileAccessPolicy.AllowReadAlways);
            vac.AddPath(A("C", "Out", "out.txt"), FileAccessPolicy.AllowAll);

            SandboxedProcessStandardFiles standardFiles = null;
            ISandboxedProcessFileStorage fileStorage;
            if (useNullFileStorage)
            {
                fileStorage = null;
            }
            else
            {
                standardFiles = new SandboxedProcessStandardFiles(A("C", "pip", "pip.out"), A("C", "pip", "pip.err"));
                fileStorage = new StandardFileStorage(standardFiles);
            }

            var envVars = new Dictionary<string, string>()
            {
                ["Var1"] = "Val1",
                ["Var2"] = "Val2",
            };
            IBuildParameters buildParameters = BuildParameters.GetFactory().PopulateFromDictionary(envVars);

            var sidebandLogFile = A("C", "engine-cache", "sideband-logs", "log-1");
            var loggerRootDirs = new[] { A("C", "out", "dir1"), A("C", "out", "dir2") };

            var sharedOpaqueOutputLogger = new SidebandWriter(DefaultSidebandMetadata, sidebandLogFile, loggerRootDirs);

            SandboxedProcessInfo info = new SandboxedProcessInfo(
                pt,
                fileStorage,
                A("C", "tool", "tool.exe"),
                fam,
                true,
                null,
                LoggingContext,
                sidebandWriter: sharedOpaqueOutputLogger)
            {
                Arguments = @"/arg1:val1 /arg2:val2",
                WorkingDirectory = A("C", "Source"),
                EnvironmentVariables = buildParameters,
                Timeout = TimeSpan.FromMinutes(15),
                PipSemiStableHash = 0x12345678,
                PipDescription = nameof(SerializeSandboxedProcessInfo),
                TimeoutDumpDirectory = A("C", "Timeout"),
                SandboxKind = global::BuildXL.Utilities.Configuration.SandboxKind.Default,
                AllowedSurvivingChildProcessNames = new[] { "conhost.exe", "mspdbsrv.exe" },
                NestedProcessTerminationTimeout = SandboxedProcessInfo.DefaultNestedProcessTerminationTimeout,
                StandardInputSourceInfo = StandardInputInfo.CreateForData("Data"),
                StandardObserverDescriptor = new SandboxObserverDescriptor()
                {
                    WarningRegex = new ExpandedRegexDescriptor("*warn", System.Text.RegularExpressions.RegexOptions.Compiled)
                },
            };

            // Serialize and deserialize.
            SandboxedProcessInfo readInfo = null;

            using (var stream = new MemoryStream())
            {
                info.Serialize(stream);
                stream.Position = 0;
                readInfo = SandboxedProcessInfo.Deserialize(
                    stream,
                    new global::BuildXL.Utilities.Instrumentation.Common.LoggingContext("Test"),
                    null);
            }

            using (readInfo.SidebandWriter)
            {
                // Verify.
                XAssert.AreEqual(info.FileName, readInfo.FileName);
                XAssert.AreEqual(info.Arguments, readInfo.Arguments);
                XAssert.AreEqual(info.WorkingDirectory, readInfo.WorkingDirectory);
                var readEnvVars = readInfo.EnvironmentVariables.ToDictionary();
                XAssert.AreEqual(envVars.Count, readEnvVars.Count);
                foreach (var kvp in envVars)
                {
                    XAssert.AreEqual(kvp.Value, readEnvVars[kvp.Key]);
                }

                XAssert.AreEqual(info.Timeout, readInfo.Timeout);
                XAssert.AreEqual(info.PipSemiStableHash, readInfo.PipSemiStableHash);
                XAssert.AreEqual(info.PipDescription, readInfo.PipDescription);
                XAssert.AreEqual(info.TimeoutDumpDirectory, readInfo.TimeoutDumpDirectory);
                XAssert.AreEqual(info.SandboxKind, readInfo.SandboxKind);

                XAssert.AreEqual(info.AllowedSurvivingChildProcessNames.Length, readInfo.AllowedSurvivingChildProcessNames.Length);
                for (int i = 0; i < info.AllowedSurvivingChildProcessNames.Length; ++i)
                {
                    XAssert.AreEqual(info.AllowedSurvivingChildProcessNames[i], readInfo.AllowedSurvivingChildProcessNames[i]);
                }

                XAssert.AreEqual(info.NestedProcessTerminationTimeout, readInfo.NestedProcessTerminationTimeout);
                XAssert.AreEqual(info.StandardInputSourceInfo, readInfo.StandardInputSourceInfo);

                if (useNullFileStorage)
                {
                    XAssert.IsNull(readInfo.SandboxedProcessStandardFiles);
                    XAssert.IsNull(readInfo.FileStorage);
                }
                else
                {
                    XAssert.IsNotNull(readInfo.SandboxedProcessStandardFiles);
                    XAssert.AreEqual(standardFiles.StandardOutput, readInfo.SandboxedProcessStandardFiles.StandardOutput);
                    XAssert.AreEqual(standardFiles.StandardError, readInfo.SandboxedProcessStandardFiles.StandardError);
                    XAssert.AreEqual(standardFiles.StandardOutput, readInfo.FileStorage.GetFileName(SandboxedProcessFile.StandardOutput));
                    XAssert.AreEqual(standardFiles.StandardError, readInfo.FileStorage.GetFileName(SandboxedProcessFile.StandardError));
                }

                XAssert.IsFalse(readInfo.ContainerConfiguration.IsIsolationEnabled);

                XAssert.AreEqual(sidebandLogFile, readInfo.SidebandWriter.SidebandLogFile);
                XAssert.ArrayEqual(loggerRootDirs, readInfo.SidebandWriter.RootDirectories.ToArray());

                if (!OperatingSystemHelper.IsUnixOS)
                {
                    // this validator examines serialized FAM bytes using the same Windows-only native parser used by Detours
                    ValidationDataCreator.TestManifestRetrieval(vac.DataItems, readInfo.FileAccessManifest, false);
                }
            }
        }

        private DirectoryTranslator CreateDirectoryTranslator()
        {
            var translator = new DirectoryTranslator();
            translator.AddTranslation(@"E:\", @"C:\");
            translator.AddTranslation(@"D:\el\io", @"D:\sh\io");

            translator.Seal();
            return translator;
        }
    }
}
