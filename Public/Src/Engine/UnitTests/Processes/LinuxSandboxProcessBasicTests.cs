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
    /// Runs native tests for the Linux Sandbox, exercising the basic reporting behavior for every system call we interpose
    /// </summary>
    /// <remarks>
    /// Tests exercising complex behavior should be added to <see cref="LinuxSandboxProcessTests"/>.
    /// </remarks>
    [TestClassIfSupported(requiresLinuxBasedOperatingSystem: true)]
    public sealed class LinuxSandboxProcessBasicTests : LinuxSandboxProcessTestsBase
    {
        public LinuxSandboxProcessBasicTests(ITestOutputHelper output)
            : base(output)
        {
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
    }
}
