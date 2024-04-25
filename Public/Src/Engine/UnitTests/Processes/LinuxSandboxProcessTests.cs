// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Runs native tests for the Linux Sandbox.
    /// </summary>
    /// <remarks>
    /// To add a new test here, first add a function to 'Public/Src/Sandbox/Linux/UnitTests/TestProcesses/TestProcess/main.cpp' with the test to run.
    /// Ensure that the test name used is the same as the function name of the test that is being on the native process.
    /// </remarks>
    [TestClassIfSupported(requiresLinuxBasedOperatingSystem: true)]
    public sealed class LinuxSandboxProcessTests : SandboxedProcessTestBase
    {
        private ITestOutputHelper TestOutput { get; }
        private string TestProcessExe => Path.Combine(TestBinRoot, "LinuxTestProcesses", "LinuxTestProcess");

        public LinuxSandboxProcessTests(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            TestOutput = output;
        }

        [Fact]
        public void CallTestfork()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 3);
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), TestProcessExe));
        }

        [Fact]
        public void CallTestvfork()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 3);
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), TestProcessExe));
        }

        [Fact]
        public void CallTestclone()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 2);
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), TestProcessExe));
        }

        [Fact]
        public void CallTestfexecve()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 4);
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("__init__", TestProcessExe));
        }

        [Fact]
        public void CallTestexecv()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 4);
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("__init__", TestProcessExe));
        }

        [Fact]
        public void CallTestexecve()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 4);
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("__init__", TestProcessExe));
        }

        [Fact]
        public void CallTestexecvp()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 4);
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("__init__", TestProcessExe));
        }

        [Fact]
        public void CallTestexecvpe()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 4);
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("__init__", TestProcessExe));
        }

        [Fact]
        public void CallTestexecl()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 4);
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("__init__", TestProcessExe));
        }

        [Fact]
        public void CallTestexeclp()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 4);
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("__init__", TestProcessExe));
        }

        [Fact]
        public void CallTestexecle()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 4);
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("__init__", TestProcessExe));
        }

        [Fact]
        public void CallTest__lxstat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!SkipStatTests())
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTest__lxstat64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!SkipStatTests())
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTest__xstat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!SkipStatTests())
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTest__xstat64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!SkipStatTests())
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTest__fxstat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!SkipStatTests())
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTest__fxstatat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!SkipStatTests())
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTest__fxstat64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!SkipStatTests())
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTest__fxstatat64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!SkipStatTests())
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTeststat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!SkipStatTests())
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTeststat64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!SkipStatTests())
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTestlstat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!SkipStatTests())
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTestlstat64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!SkipStatTests())
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTestfstat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!SkipStatTests())
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTestfstat64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!SkipStatTests())
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTestfdopen()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfopen()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfopen64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfreopen()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfreopen64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfread()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            // This one gets cached on the native side and not reported back but we should still see it be intercepted
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestfwrite()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestfputc()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestfputs()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestputc()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestputchar()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestputs()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestaccess()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfaccessat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestcreat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("CreateFileOpen", Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestopen64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("CreateFileOpen", Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestopen()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("CreateFileOpen", Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestopenat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("CreateFileOpen", Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestwrite()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestwritev()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestpwritev()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestpwritev2()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestpwrite()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestpwrite64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestremove()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTesttruncate()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestftruncate()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTesttruncate64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestftruncate64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestrmdir()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testdirectory")));
        }

        [Fact]
        public void CallTestrename()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            // Rename is redirected to renameat
            AssertLogContains(GetRegex("handle_renameat", Path.Combine(result.rootDirectory, "testfile")));
            AssertLogContains(GetRegex("CreateFileOpen", Path.Combine(result.rootDirectory, "testfile2")));
        }

        [Fact]
        public void CallTestrenameat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("handle_renameat", Path.Combine(result.rootDirectory, "testfile")));
            AssertLogContains(GetRegex("CreateFileOpen", Path.Combine(result.rootDirectory, "testfile2")));
        }

        [Fact]
        public void CallTestrenameat2()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("handle_renameat", Path.Combine(result.rootDirectory, "testfile")));
            AssertLogContains(GetRegex("CreateFileOpen", Path.Combine(result.rootDirectory, "testfile2")));
        }

        [Fact]
        public void CallTestlink()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            AssertLogContains(caseSensitive: false, "testfile2");
        }

        [Fact]
        public void CallTestlinkat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            AssertLogContains(caseSensitive: false, "testfile2");
        }

        [Fact]
        public void CallTestunlink()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestunlinkat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestsymlink()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestsymlinkat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestreadlink()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestreadlinkat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestrealpath()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
        }

        [Fact]
        public void CallTestopendir()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
        }

        [Fact]
        public void CallTestfdopendir()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
        }

        [Fact]
        public void CallTestutime()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestutimes()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestutimensat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfutimesat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfutimens()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestmkdir()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("ReportCreate", Path.Combine(result.rootDirectory, "testdirectory")));
        }

        [Fact]
        public void CallTestmkdirat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("ReportCreate", Path.Combine(result.rootDirectory, "testdirectory")));
        }

        public void CallTestmknod()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("ReportCreate", Path.Combine(result.rootDirectory, "testfile")));
        }

        public void CallTestmknodat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("ReportCreate", Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestprintf()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestfprintf()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, "Intercepted fprintf");
        }

        [Fact]
        public void CallTestdprintf()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            // dprintf is forwarded to vdprintf
            AssertLogContains(GetRegex("vdprintf", Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestvprintf()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestvfprintf()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestvdprintf()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestchmod()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfchmod()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfchmodat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestchown()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfchown()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestlchown()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfchownat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestsendfile()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestsendfile64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("sendfile", Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestcopy_file_range()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestname_to_handle_at()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex("CreateFileOpen", Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestdup()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestdup2()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestdup3()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestscandir()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
        }

        [Fact]
        public void CallTestscandir64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
        }

        [Fact]
        public void CallTestscandirat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
        }

        [Fact]
        public void CallTestscandirat64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
        }

        [Fact]
        public void CallTeststatx()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
        }

        [Fact]
        public void CallTestclosedir()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
        }

        [Fact]
        public void CallTestreaddir()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
        }

        [Fact]
        public void CallTestreaddir64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
        }

        [Fact]
        public void CallTestreaddir_r()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
        }

        [Fact]
        public void CallTestreaddir64_r()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
        }

        [Fact]
        public void CallTestAnonymousFile()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var process = CreateTestProcess(
                    Context.PathTable,
                    tempFiles,
                    GetNativeTestName(MethodBase.GetCurrentMethod().Name),
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                var processInfo = ToProcessInfo(process);
                processInfo.FileAccessManifest.ReportFileAccesses = true;
                processInfo.FileAccessManifest.MonitorChildProcesses = true;
                processInfo.FileAccessManifest.FailUnexpectedFileAccesses = false;

                var result = RunProcess(processInfo).Result;

                XAssert.AreEqual(result.ExitCode, 0, $"{Environment.NewLine}Standard Output: '{result.StandardOutput.ReadValueAsync().Result}'{Environment.NewLine}Standard Error: '{result.StandardError.ReadValueAsync().Result}'");

                XAssert.IsFalse(
                    result.FileAccesses.Select(f => f.ManifestPath.ToString(Context.PathTable)).Contains("/memfd:testFile (deleted)"),
                    $"Anonymous file reported by sandbox, file accesses:{Environment.NewLine}{string.Join(Environment.NewLine, result.FileAccesses.Select(f => f.ManifestPath.ToString(Context.PathTable)).ToArray())}");
            }
        }

        [Fact]
        public void CallTestclone3()
        {
            // This test only applies to ptrace because we can't interpose clone3
            // clone3 was introduced in Linux kernel 5.3
            if (OperatingSystemHelperExtension.IsLinuxKernelVersionSameOrNewer(5, 3, 0))
            {
                var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name), unconditionallyEnableLinuxPTraceSandbox: true);
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), TestProcessExe));
            }
        }

        /// <summary>
        /// Some stat tests don't run depending on the glibc version on the machine
        /// </summary>
        private bool SkipStatTests()
        {
            string originalLog = EventListener.GetLog();

            // Stat tests will call open first to create a file to stat, if this doesn't exist then stat won't be called
            return !originalLog.Contains("CreateFileOpen");
        }

        private string GetNativeTestName(string functionName)
        {
            return functionName.Replace("Call", "");
        }

        private string GetSyscallName(string functionName)
        {
            return functionName.Replace("CallTest", "");
        }

        private (SandboxedProcessResult result, string rootDirectory) RunNativeTest(string testName, bool unconditionallyEnableLinuxPTraceSandbox = false)
        {
            using var tempFiles = new TempFileStorage(canGetFileNames: true);
            var process = CreateTestProcess(
                Context.PathTable,
                tempFiles,
                testName,
                inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

            var processInfo = ToProcessInfo(process, workingDirectory: tempFiles.RootDirectory);
            processInfo.FileAccessManifest.ReportFileAccesses = true;
            processInfo.FileAccessManifest.MonitorChildProcesses = true;
            processInfo.FileAccessManifest.FailUnexpectedFileAccesses = false;
            processInfo.FileAccessManifest.EnableLinuxSandboxLogging = true;
            processInfo.FileAccessManifest.UnconditionallyEnableLinuxPTraceSandbox = unconditionallyEnableLinuxPTraceSandbox;

            var result = RunProcess(processInfo).Result;

            string message = $"Test terminated with exit code {result.ExitCode}.{Environment.NewLine}stdout: {result.StandardOutput.ReadValueAsync().Result}{Environment.NewLine}stderr: {result.StandardError.ReadValueAsync().Result}";
            XAssert.IsTrue(result.ExitCode == 0, message);

            return (result, tempFiles.RootDirectory);
        }

        private Process CreateTestProcess(
            PathTable pathTable,
            TempFileStorage tempFileStorage,
            string testName,
            ReadOnlyArray<FileArtifact> inputFiles,
            ReadOnlyArray<DirectoryArtifact> inputDirectories,
            ReadOnlyArray<FileArtifactWithAttributes> outputFiles,
            ReadOnlyArray<DirectoryArtifact> outputDirectories,
            ReadOnlyArray<AbsolutePath> untrackedScopes)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(tempFileStorage != null);
            Contract.Requires(testName != null);
            Contract.Requires(Contract.ForAll(inputFiles, artifact => artifact.IsValid));
            Contract.Requires(Contract.ForAll(inputDirectories, artifact => artifact.IsValid));
            Contract.Requires(Contract.ForAll(outputFiles, artifact => artifact.IsValid));
            Contract.Requires(Contract.ForAll(outputDirectories, artifact => artifact.IsValid));

            XAssert.IsTrue(File.Exists(TestProcessExe));

            FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, TestProcessExe));
            var untrackedList = new List<AbsolutePath>(CmdHelper.GetCmdDependencies(pathTable));
            var allUntrackedScopes = new List<AbsolutePath>(untrackedScopes);
            allUntrackedScopes.AddRange(CmdHelper.GetCmdDependencyScopes(pathTable));

            var inputFilesWithExecutable = new List<FileArtifact>(inputFiles) { executableFileArtifact };

            var arguments = new PipDataBuilder(pathTable.StringTable);
            arguments.Add("-t");
            arguments.Add(testName);

            return new Process(
                executableFileArtifact,
                AbsolutePath.Create(pathTable, tempFileStorage.RootDirectory),
                arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                FileArtifact.Invalid,
                PipData.Invalid,
                ReadOnlyArray<EnvironmentVariable>.Empty,
                FileArtifact.Invalid,
                FileArtifact.Invalid,
                FileArtifact.Invalid,
                tempFileStorage.GetUniqueDirectory(pathTable),
                null,
                null,
                dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(inputFilesWithExecutable.ToArray()),
                outputs: outputFiles,
                directoryDependencies: inputDirectories,
                directoryOutputs: outputDirectories,
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.From(untrackedList),
                untrackedScopes: ReadOnlyArray<AbsolutePath>.From(allUntrackedScopes),
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(0),
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(Context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                options: default);
        }

        private void TestForFileOperation((SandboxedProcessResult result, string rootDirectory) result, ReportedFileOperation op, string path, int count = 1)
        {
            var matches = result.result.FileAccesses.Where(access => access.Operation == op);
            var assertString = $"{op}";
            if (!string.IsNullOrEmpty(path))
            {
                matches = matches.Where(access => access.ManifestPath.ToString(Context.PathTable) == path);
                assertString += $":{path}";
            }

            XAssert.IsTrue(matches.ToList().Count == count,
                $"Did not find expected count ({count}) of file access '{assertString}'{Environment.NewLine}Reported Accesses:{Environment.NewLine}{string.Join(Environment.NewLine, result.result.FileAccesses.Select(fa => $"{fa.Operation}:{fa.ManifestPath.ToString(Context.PathTable)}").ToList())}");
        }

        private Regex GetRegex(string fileOperation, string path) => new Regex($@".*\(\( *{fileOperation}: *[0-9]* *\)\).*{Regex.Escape(path)}.*", RegexOptions.IgnoreCase);
    }
}
