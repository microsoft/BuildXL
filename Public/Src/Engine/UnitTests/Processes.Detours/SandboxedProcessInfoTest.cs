// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.Processes;
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

        [Fact]
        public void SerializeSandboxedProcessInfo()
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

            var standardFiles = new SandboxedProcessStandardFiles(A("C", "pip", "pip.out"), A("C", "pip", "pip.err"));
            var envVars = new Dictionary<string, string>()
            {
                ["Var1"] = "Val1",
                ["Var2"] = "Val2",
            };
            IBuildParameters buildParameters = BuildParameters.GetFactory().PopulateFromDictionary(envVars);

            SandboxedProcessInfo info = new SandboxedProcessInfo(
                pt,
                new StandardFileStorage(standardFiles),
                A("C", "tool", "tool.exe"),
                fam,
                true,
                null)
            {
                Arguments = @"/arg1:val1 /arg2:val2",
                WorkingDirectory = A("C", "Source"),
                EnvironmentVariables = buildParameters,
                Timeout = TimeSpan.FromMinutes(15),
                PipSemiStableHash = 0x12345678,
                PipDescription = nameof(SerializeSandboxedProcessInfo),
                ProcessIdListener = null,
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
            XAssert.AreEqual(info.ProcessIdListener, readInfo.ProcessIdListener);
            XAssert.AreEqual(info.TimeoutDumpDirectory, readInfo.TimeoutDumpDirectory);
            XAssert.AreEqual(info.SandboxKind, readInfo.SandboxKind);

            XAssert.AreEqual(info.AllowedSurvivingChildProcessNames.Length, readInfo.AllowedSurvivingChildProcessNames.Length);
            for (int i = 0; i < info.AllowedSurvivingChildProcessNames.Length; ++i)
            {
                XAssert.AreEqual(info.AllowedSurvivingChildProcessNames[i], readInfo.AllowedSurvivingChildProcessNames[i]);
            }

            XAssert.AreEqual(info.NestedProcessTerminationTimeout, readInfo.NestedProcessTerminationTimeout);
            XAssert.AreEqual(info.StandardInputSourceInfo, readInfo.StandardInputSourceInfo);
            XAssert.IsNotNull(readInfo.SandboxedProcessStandardFiles);
            XAssert.AreEqual(standardFiles.StandardOutput, readInfo.SandboxedProcessStandardFiles.StandardOutput);
            XAssert.AreEqual(standardFiles.StandardError, readInfo.SandboxedProcessStandardFiles.StandardError);
            XAssert.AreEqual(standardFiles.StandardOutput, readInfo.FileStorage.GetFileName(SandboxedProcessFile.StandardOutput));
            XAssert.AreEqual(standardFiles.StandardError, readInfo.FileStorage.GetFileName(SandboxedProcessFile.StandardError));
            XAssert.IsFalse(readInfo.ContainerConfiguration.IsIsolationEnabled);

            ValidationDataCreator.TestManifestRetrieval(vac.DataItems, readInfo.FileAccessManifest, false);
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
