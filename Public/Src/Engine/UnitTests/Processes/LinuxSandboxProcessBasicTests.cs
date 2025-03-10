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
            TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: UsingEBPFSandbox? 3 : 2);
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Process, TestProcessExe));
        }

        [Fact]
        public void CallTestvfork()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: UsingEBPFSandbox? 3 : 2);
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Process, TestProcessExe));
        }

        [Fact]
        public void CallTestclone()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: UsingEBPFSandbox? 3 : 2);
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Process, TestProcessExe));
        }

        [Fact]
        public void CallTestfexecve()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!UsingEBPFSandbox)
            {
                TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 2);
                AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
                AssertLogContains(GetRegex("__init__fork", TestProcessExe));
                AssertLogContains(GetRegex("__init__exec", TestProcessExe));
            }
            else
            {
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.ProcessExec, TestProcessExe), 1);
            }
        }

        [Fact]
        public void CallTestexecv()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!UsingEBPFSandbox)
            {
                TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 2);
                AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
                AssertLogContains(GetRegex("__init__fork", TestProcessExe));
                AssertLogContains(GetRegex("__init__exec", TestProcessExe));
            }
            else
            {
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.ProcessExec, TestProcessExe), 2);
            }
        }

        [Fact]
        public void CallTestexecve()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!UsingEBPFSandbox)
            {
                TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 2);
                AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
                AssertLogContains(GetRegex("__init__fork", TestProcessExe));
                AssertLogContains(GetRegex("__init__exec", TestProcessExe));
            }
            else
            {
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.ProcessExec, TestProcessExe), 2);
            }
        }

        [Fact]
        public void CallTestexecvp()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!UsingEBPFSandbox)
            {
                TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 2);
                AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
                AssertLogContains(GetRegex("__init__fork", TestProcessExe));
                AssertLogContains(GetRegex("__init__exec", TestProcessExe));
            }
            else
            {
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.ProcessExec, TestProcessExe), 2);
            }
        }

        [Fact]
        public void CallTestexecvpe()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!UsingEBPFSandbox)
            {
                TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 2);
                AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
                AssertLogContains(GetRegex("__init__fork", TestProcessExe));
                AssertLogContains(GetRegex("__init__exec", TestProcessExe));
            }
            else
            {
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.ProcessExec, TestProcessExe), 2);
            }
        }

        [Fact]
        public void CallTestexecl()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!UsingEBPFSandbox)
            {
                TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 2);
                AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
                AssertLogContains(GetRegex("__init__fork", TestProcessExe));
                AssertLogContains(GetRegex("__init__exec", TestProcessExe));
            }
            else
            {
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.ProcessExec, TestProcessExe), 2);
            }
        }

        [Fact]
        public void CallTestexeclp()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!UsingEBPFSandbox)
            {
                TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 2);
                AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
                AssertLogContains(GetRegex("__init__fork", TestProcessExe));
                AssertLogContains(GetRegex("__init__exec", TestProcessExe));
            }
            else
            {
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.ProcessExec, TestProcessExe), 2);
            }
        }

        [Fact]
        public void CallTestexecle()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (!UsingEBPFSandbox)
            {
                TestForFileOperation(result, ReportedFileOperation.Process, TestProcessExe, count: 2);
                AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
                AssertLogContains(GetRegex("__init__fork", TestProcessExe));
                AssertLogContains(GetRegex("__init__exec", TestProcessExe));
            }
            else
            {
                // TODO: Revisit why sometimes we get 1 and some other times 2
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.ProcessExec, TestProcessExe));
            }
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
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.Probe: ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfopen()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.Probe: ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfopen64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.Probe: ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfreopen()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.Probe: ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfreopen64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.Probe: ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfread()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            // This one gets cached on the native side and not reported back but we should still see it be intercepted
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.ReadFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfwrite()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfputc()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfputs()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestputc()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
           AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestputchar()
        {
            // This function writes to stdout, so there are no filsystem changes we care about
            // for the EBPF case
            if (!UsingEBPFSandbox)
            {
                var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
                AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            }
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
             AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox ? ReportedFileOperation.WriteFile : ReportedFileOperation.CreateFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfaccessat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox ? ReportedFileOperation.WriteFile : ReportedFileOperation.CreateFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestcreat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.CreateFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestopen64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.Probe : ReportedFileOperation.CreateFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestopen()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.Probe : ReportedFileOperation.CreateFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestopenat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.Probe : ReportedFileOperation.CreateFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestwrite()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestwritev()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestpwritev()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestpwritev2()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }   

        [Fact]
        public void CallTestpwrite()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestpwrite64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestremove()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox ? ReportedFileOperation.WriteFile : ReportedFileOperation.DeleteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTesttruncate()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestftruncate()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTesttruncate64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestftruncate64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestrmdir()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.RemoveDirectory, Path.Combine(result.rootDirectory, "testdirectory")));
        }

        [Fact]
        public void CallTestrename()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.DeleteFile, Path.Combine(result.rootDirectory, "testfile")));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.CreateFile, Path.Combine(result.rootDirectory, "testfile2")));
        }

        [Fact]
        public void CallTestrenameat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.DeleteFile, Path.Combine(result.rootDirectory, "testfile")));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.CreateFile, Path.Combine(result.rootDirectory, "testfile2")));
        }

        [Fact]
        public void CallTestrenameat2()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.DeleteFile, Path.Combine(result.rootDirectory, "testfile")));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.CreateFile, Path.Combine(result.rootDirectory, "testfile2")));
        }

        [Fact]
        public void CallTestlink()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.WriteFile : ReportedFileOperation.CreateHardlinkSource, Path.Combine(result.rootDirectory, "testfile")));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.WriteFile : ReportedFileOperation.CreateHardlinkDest, Path.Combine(result.rootDirectory, "testfile2")));
        }

        [Fact]
        public void CallTestlinkat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.WriteFile : ReportedFileOperation.CreateHardlinkSource, Path.Combine(result.rootDirectory, "testfile")));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.WriteFile : ReportedFileOperation.CreateHardlinkDest, Path.Combine(result.rootDirectory, "testfile2")));
        }

        [Fact]
        public void CallTestunlink()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.WriteFile : ReportedFileOperation.DeleteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestunlinkat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.WriteFile : ReportedFileOperation.DeleteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestsymlink()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (UsingEBPFSandbox)
            {
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.CreateFile, Path.Combine(result.rootDirectory, "testfile")));
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile2")));
            }
            else
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTestsymlinkat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (UsingEBPFSandbox)
            {
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.CreateFile, Path.Combine(result.rootDirectory, "testfile")));
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile2")));
            }
            else
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), Path.Combine(result.rootDirectory, "testfile")));
            }
        }

        [Fact]
        public void CallTestreadlink()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.ReadFile : ReportedFileOperation.Readlink, Path.Combine(result.rootDirectory, "testfile2")));
        }

        [Fact]
        public void CallTestreadlinkat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.ReadFile : ReportedFileOperation.Readlink, Path.Combine(result.rootDirectory, "testfile2")));
        }

        [Fact]
        public void CallTestrealpath()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox? ReportedFileOperation.ReadFile : ReportedFileOperation.Readlink, Path.Combine(result.rootDirectory, "testfile2")));
        }

        [Fact]
        public void CallTestopendir()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Probe, result.rootDirectory));
        }

        [Fact]
        public void CallTestfdopendir()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Probe, result.rootDirectory));
        }

        [Fact]
        public void CallTestutime()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestutimes()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestutimensat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfutimesat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfutimens()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestmkdir()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.CreateDirectory, Path.Combine(result.rootDirectory, "testdirectory")));
        }

        [Fact]
        public void CallTestmkdirat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.CreateDirectory, Path.Combine(result.rootDirectory, "testdirectory")));
        }

        [Fact]
        public void CallTestmknod()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.CreateFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestmknodat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.CreateFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfprintf()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestvfprintf()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestvdprintf()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestchmod()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfchmod()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfchmodat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestchown()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfchown()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestlchown()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestfchownat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestsendfile()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestsendfile64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestcopy_file_range()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.WriteFile, Path.Combine(result.rootDirectory, "testfile2")));
        }

        [Fact]
        public void CallTestname_to_handle_at()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
             AssertLogContains(GetAccessReportRegex(UsingEBPFSandbox ? ReportedFileOperation.WriteFile : ReportedFileOperation.DeleteFile, Path.Combine(result.rootDirectory, "testfile")));
        }

        [Fact]
        public void CallTestdup()
        {
            // For EBPF we don't care about tracking fd creation (they don't have an effect in terms of read/writes)
            if (!UsingEBPFSandbox)
            {
                var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
                AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            }
        }

        [Fact]
        public void CallTestdup2()
        {
            // For EBPF we don't care about tracking fd creation (they don't have an effect in terms of read/writes)
            if (!UsingEBPFSandbox)
            {
                var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
                AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            }
        }

        [Fact]
        public void CallTestdup3()
        {
            // For EBPF we don't care about tracking fd creation (they don't have an effect in terms of read/writes)
            if (!UsingEBPFSandbox)
            {
                var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
                AssertLogContains(caseSensitive: false, GetSyscallName(MethodBase.GetCurrentMethod().Name));
            }
        }

        [Fact]
        public void CallTestscandir()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (UsingEBPFSandbox)
            {
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Probe, result.rootDirectory));
            }
            else
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
            }
        }

        [Fact]
        public void CallTestscandir64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (UsingEBPFSandbox)
            {
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Probe, result.rootDirectory));
            }
            else
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
            }
        }

        [Fact]
        public void CallTestscandirat()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            if (UsingEBPFSandbox)
            {
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Probe, result.rootDirectory));
            }
            else
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
            }
        }

        [Fact]
        public void CallTestscandirat64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
           if (UsingEBPFSandbox)
            {
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Probe, result.rootDirectory));
            }
            else
            {
                AssertLogContains(GetRegex(GetSyscallName(MethodBase.GetCurrentMethod().Name), result.rootDirectory));
            }
        }

        [Fact]
        public void CallTeststatx()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Probe, result.rootDirectory));
        }

        [Fact]
        public void CallTestclosedir()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Probe, result.rootDirectory));
        }

        [Fact]
        public void CallTestreaddir()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Probe, result.rootDirectory));
        }

        [Fact]
        public void CallTestreaddir64()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Probe, result.rootDirectory));
        }

        [Fact]
        public void CallTestreaddir_r()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Probe, result.rootDirectory));
        }

        [Fact]
        public void CallTestreaddir64_r()
        {
            var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name));
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Probe, result.rootDirectory));
        }

        [Fact]
        public void CallTestclone3()
        {
            // This test only applies to ptrace and ebpf because we can't interpose clone3 (with interpose)
            // clone3 was introduced in Linux kernel 5.3
            if (OperatingSystemHelperExtension.IsLinuxKernelVersionSameOrNewer(5, 3, 0))
            {
                var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name), unconditionallyEnableLinuxPTraceSandbox: !UsingEBPFSandbox);
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Process, TestProcessExe), count: UsingEBPFSandbox? 3 : null);
            }
        }

        [Fact]
        public void CallTestclone3WithProbe()
        {
            // clone3 was introduced in Linux kernel 5.3
            if (OperatingSystemHelperExtension.IsLinuxKernelVersionSameOrNewer(5, 3, 0))
            {
                // We use the pip main executable as a fallback when we couldn't resolve the case of missing process start events properly
                // So let's wrap in bash the test, so we can then verify we actually resolved this scenario
                var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name), wrapInBash: true);

                // Retrieve the absent probe that happened under clone3
                var probe = result.result.FileAccesses.Single(
                    access => access.Operation == ReportedFileOperation.Probe && access.GetPath(Context.PathTable).EndsWith("absentFile"));

                // The probe should get assigned the parent process path
                Assert.Equal(TestProcessExe, probe.Process.Path);

                // Just being defensive: the spawned process that ran the probe was created via clone3, and therefore we shouldn't have seen
                // the process start for it
                Assert.False(result.result.AllUnexpectedFileAccesses.Any(
                    access => access.Operation == ReportedFileOperation.Process && access.Process.ProcessId == probe.Process.ProcessId));

                // We shouldn't get an unknown pid report
                AssertVerboseEventLogged(global::BuildXL.Processes.Tracing.LogEventId.ReceivedReportFromUnknownPid, 0);
            }
        }

        [Fact]
        public void CallTestclone3Nested()
        {
            // clone3 was introduced in Linux kernel 5.3
            if (OperatingSystemHelperExtension.IsLinuxKernelVersionSameOrNewer(5, 3, 0))
            {
                var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name), wrapInBash: true);

                // Retrieve the directory probe that happened under the (nested) clone3
                var probe = result.result.FileAccesses.Single(
                    access => access.Operation == ReportedFileOperation.Probe && access.GetPath(Context.PathTable).EndsWith("absentFile"));

                // The probe should get assigned the ancestor process path
                Assert.Equal(TestProcessExe, probe.Process.Path);

                // Just being defensive: the spawned processes were created via clone3, and therefore we shouldn't have seen
                // the process start for them
                Assert.False(result.result.AllUnexpectedFileAccesses.Any(
                    access => access.Operation == ReportedFileOperation.Process && access.Process.ProcessId == probe.Process.ProcessId));

                // We shouldn't get an unknown pid report
                AssertVerboseEventLogged(global::BuildXL.Processes.Tracing.LogEventId.ReceivedReportFromUnknownPid, 0);
            }
        }

        [Fact]
        public void CallTestclone3NestedAndExec()
        {
            // clone3 was introduced in Linux kernel 5.3
            if (OperatingSystemHelperExtension.IsLinuxKernelVersionSameOrNewer(5, 3, 0))
            {
                var result = RunNativeTest(GetNativeTestName(MethodBase.GetCurrentMethod().Name), wrapInBash: true);

                var expectedOperation = UsingEBPFSandbox? ReportedFileOperation.ProcessExec : ReportedFileOperation.Process;
                // Retrieve the exec echo that happened under the (nested) clone3. This exec is such that it is the first report
                // that happens with a given pid (for which we missed the process start for)
                var exec = result.result.FileAccesses.Single(
                    access => access.Operation == expectedOperation && access.GetPath(Context.PathTable).EndsWith("echo"));

                // The exec process should get the right path assigned to.
                Assert.EndsWith("echo", exec.Process.Path);

                // We shouldn't get an unknown pid report
                AssertVerboseEventLogged(global::BuildXL.Processes.Tracing.LogEventId.ReceivedReportFromUnknownPid, 0);
            }
        }
    }
}
