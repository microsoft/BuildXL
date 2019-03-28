// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Reflection;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using AssemblyHelper = BuildXL.Utilities.AssemblyHelper;

namespace Test.BuildXL.Processes
{
    [Trait("Category", "DetoursCrossBitnessTest")]
    [Trait("Category", "WindowsOSOnly")]
    public sealed class DetoursCrossBitnessTest
    {
        private const string DetoursCrossBitnessTestCategory = "DetoursCrossBitnessTest";
        private const string TestExecutableX64 = "DetoursCrossBitTests-X64.exe";
        private const string TestExecutableX86 = "DetoursCrossBitTests-X86.exe";
        private ITestOutputHelper _output;
        
        public DetoursCrossBitnessTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact(Skip = "Skip -- Disabled (temporarily) because fails in many machines")]
        public void TestRun64BitCmdFrom64BitProcess()
        {
            int exCode = StartProcess(true, ProcessKind.Cmd, true);
            XAssert.AreEqual(0, exCode);
        }

        [Fact(Skip = "Skip -- Disabled (temporarily) because fails in many machines")]
        public void TestRun32BitCmdFrom64BitProcess()
        {
            int exCode = StartProcess(true, ProcessKind.Cmd, false);
            XAssert.AreEqual(0, exCode);
        }

        [Fact(Skip = "Skip")]
        public void TestRun32BitCmdFrom32BitProcess()
        {
            int exCode = StartProcess(false, ProcessKind.Cmd, false);
            XAssert.AreEqual(0, exCode);
        }

        [Fact(Skip = "Skip")]
        public void TestRun64BitCmdFrom32BitProcess()
        {
            // This is successful without Detours launching UpdImports-64.
            // The reason being is WoW64 Filesystem redirection
            // redirects launching 64-bit cmd to 32-bit cmd.
            int exCode = StartProcess(false, ProcessKind.Cmd, true);
            XAssert.AreEqual(0, exCode);
        }

        [Fact]
        public void TestRun64BitSelfFrom64BitProcess()
        {
            int exCode = StartProcess(true, ProcessKind.Self, true);
            XAssert.AreEqual(0, exCode);
        }

        [Fact]
        public void TestRun32BitSelfFrom64BitProcess()
        {
            int exCode = StartProcess(true, ProcessKind.Self, false);
            XAssert.AreEqual(0, exCode);
        }

        [Fact(Skip = "Skip")]
        public void TestRun32BitSelfFrom32BitProcess()
        {
            int exCode = StartProcess(false, ProcessKind.Self, false);
            XAssert.AreEqual(0, exCode);
        }

        [Fact(Skip = "Skip")]
        public void TestRun64BitSelfFrom32BitProcess()
        {
            // This should be successful because Detours launches UpdImports-64.
            int exCode = StartProcess(false, ProcessKind.Self, true);
            XAssert.AreEqual(0, exCode);
        }

        [Fact]
        public void TestRun64BitSelfChildFrom64BitSelfFrom64BitProcess()
        {
            int exCode = StartProcess(true, ProcessKind.SelfChild64, true);
            XAssert.AreEqual(0, exCode);
        }

        [Fact]
        public void TestRun32BitSelfChildFrom64BitSelfFrom64BitProcess()
        {
            int exCode = StartProcess(true, ProcessKind.SelfChild32, true);
            XAssert.AreEqual(0, exCode);
        }

        [Fact]
        public void TestRun64BitSelfChildFrom32BitSelfFrom64BitProcess()
        {
            int exCode = StartProcess(true, ProcessKind.SelfChild64, false);
            XAssert.AreEqual(0, exCode);
        }

        [Fact]
        public void TestRun32BitSelfChildFrom32BitSelfFrom64BitProcess()
        {
            int exCode = StartProcess(true, ProcessKind.SelfChild32, false);
            XAssert.AreEqual(0, exCode);
        }

        [Fact(Skip = "Skip")]
        public void TestRun64BitSelfChildFrom64BitSelfFrom32BitProcess()
        {
            int exCode = StartProcess(false, ProcessKind.SelfChild64, true);
            XAssert.AreEqual(0, exCode);
        }

        [Fact(Skip = "Skip")]
        public void TestRun32BitSelfChildFrom64BitSelfFrom32BitProcess()
        {
            int exCode = StartProcess(false, ProcessKind.SelfChild32, true);
            XAssert.AreEqual(0, exCode);
        }

        [Fact(Skip = "Skip")]
        public void TestRun64BitSelfChildFrom32BitSelfFrom32BitProcess()
        {
            int exCode = StartProcess(false, ProcessKind.SelfChild64, false);
            XAssert.AreEqual(0, exCode);
        }

        [Fact(Skip = "Skip")]
        public void TestRun32BitSelfChildFrom32BitSelfFrom32BitProcess()
        {
            int exCode = StartProcess(false, ProcessKind.SelfChild32, false);
            XAssert.AreEqual(0, exCode);
        }

        private int StartProcess(bool isProc64, ProcessKind procKind, bool isLaunchedProc64)
        {
            int exitCode = -1;

            string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));

            if (currentCodeFolder != null)
            {
                currentCodeFolder = Path.Combine(currentCodeFolder, "DetoursCrossBitTests");
                string executable = Path.Combine(currentCodeFolder, isProc64 ? TestExecutableX64 : TestExecutableX86);
                string workingDirectory = Path.GetDirectoryName(executable);
                Contract.Assume(workingDirectory != null);
                string arguments = GetArguments(procKind, isLaunchedProc64);
                using (var process = new Process
                {
                    StartInfo =
                    {
                        FileName = executable,
                        WorkingDirectory = workingDirectory,
                        Arguments = arguments,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardError = true
                    }
                })
                {
                    process.Start();

                    // On test failure, we want stderr output from the test process as context.
                    string line;
                    while ((line = process.StandardError.ReadLine()) != null)
                    {
                        _output.WriteLine("stderr from DetoursCrossBitTests: {0}", line);
                    }

                    process.WaitForExit();
                    exitCode = process.ExitCode;
                    process.Close();
                }
            }

            return exitCode;
        }

        private string GetArguments(ProcessKind processKind, bool isLaunchedProc64)
        {
            switch (processKind)
            {
                case ProcessKind.Cmd:
                    return isLaunchedProc64 ? "cmd64" : "cmd32";
                case ProcessKind.Self:
                    return isLaunchedProc64 ? "self64" : "self32";
                case ProcessKind.SelfChild32:
                    return (isLaunchedProc64 ? "self64 " : "self32 ") + "selfChild32";
                case ProcessKind.SelfChild64:
                    return (isLaunchedProc64 ? "self64 " : "self32 ") + "selfChild64";
            }

            return string.Empty;
        }

        private enum ProcessKind
        {
            /// <summary>
            /// Cmd.exe.
            /// </summary>
            Cmd,

            /// <summary>
            /// Self test.
            /// </summary>
            Self,

            /// <summary>
            /// Self test followed by 32-bit self child.
            /// </summary>
            SelfChild32,

            /// <summary>
            /// Self test followed by 64-bit self child.
            /// </summary>
            SelfChild64
        }
    }
}
