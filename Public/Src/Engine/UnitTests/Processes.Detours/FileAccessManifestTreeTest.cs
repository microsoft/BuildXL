// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes.Detours
{
    public class FileAccessManifestTreeTest : XunitBuildXLTest
    {
        /// <inheritdoc />
        public FileAccessManifestTreeTest(ITestOutputHelper output)
            : base(output)
        {
            // Debugger.Launch(); // uncomment this line to enable native debugging
        }

        /// <summary>
        /// Exercise that the API can tolerate some edge cases: in this case,
        /// a manifest which contains nothing but the root node, and an empty path.
        /// </summary>
        [Fact]
        public void EmptyRecordEmptyPathManifestTest()
        {
            var pt = new PathTable();
            var fam =
                new FileAccessManifest(pt)
                {
                    FailUnexpectedFileAccesses = false,
                    IgnoreCodeCoverage = false,
                    ReportFileAccesses = false,
                    ReportUnexpectedFileAccesses = false,
                    MonitorChildProcesses = false
                };

            byte[] manifestTreeBytes = fam.GetManifestTreeBytes();

            uint conePolicyValue;
            uint nodePolicyValue;
            uint pathIdValue;
            Usn expectedUsn;

            bool success =
                global::BuildXL.Native.Processes.Windows.ProcessUtilitiesWin.FindFileAccessPolicyInTree(
                    manifestTreeBytes,
                    string.Empty,
                    new UIntPtr(0),
                    out conePolicyValue,
                    out nodePolicyValue,
                    out pathIdValue,
                    out expectedUsn);

            XAssert.IsTrue(success, "Unable to find path in manifest.");
            XAssert.AreEqual((uint)FileAccessPolicy.Deny, nodePolicyValue);
            XAssert.AreEqual((uint)FileAccessPolicy.Deny, conePolicyValue);
            XAssert.AreEqual((uint)0, pathIdValue);
            XAssert.AreEqual(ReportedFileAccess.NoUsn, expectedUsn);
        }

        [Fact]
        public void EmptyRecordSimplePathManifestTest()
        {
            var pt = new PathTable();
            var fam =
                new FileAccessManifest(pt)
                {
                    FailUnexpectedFileAccesses = false,
                    IgnoreCodeCoverage = false,
                    ReportFileAccesses = false,
                    ReportUnexpectedFileAccesses = false,
                    MonitorChildProcesses = false
                };

            byte[] manifestTreeBytes = fam.GetManifestTreeBytes();

            uint conePolicyValue;
            uint nodePolicyValue;
            uint pathIdValue;
            Usn expectedUsn;

            var path = @"C:\Windows\System32\cmd.exe";
            bool success =
                global::BuildXL.Native.Processes.Windows.ProcessUtilitiesWin.FindFileAccessPolicyInTree(
                    manifestTreeBytes,
                    path,
                    new UIntPtr((uint)path.Length),
                    out conePolicyValue,
                    out nodePolicyValue,
                    out pathIdValue,
                    out expectedUsn);

            XAssert.IsTrue(success, "Unable to find path in manifest.");
            XAssert.AreEqual((uint)FileAccessPolicy.Deny, nodePolicyValue);
            XAssert.AreEqual((uint)FileAccessPolicy.Deny, conePolicyValue);
            XAssert.AreEqual((uint)0, pathIdValue);
            XAssert.AreEqual(ReportedFileAccess.NoUsn, expectedUsn);
        }

        /// <summary>
        /// This test exercises the ability of the manifest to tolerate trailing
        /// backslashes for directory leaves.
        /// </summary>
        [Fact]
        public void TestTrailingSeparatorsManifestTest()
        {
            var pt = new PathTable();
            var fam =
                new FileAccessManifest(pt)
                {
                    FailUnexpectedFileAccesses = false,
                    IgnoreCodeCoverage = false,
                    ReportFileAccesses = false,
                    ReportUnexpectedFileAccesses = false,
                    MonitorChildProcesses = false
                };

            var vac = new ValidationDataCreator(fam, pt);

            AbsolutePath windows = vac.AddScope(@"C:\Windows\", FileAccessPolicy.AllowAll);
            AbsolutePath sys32 = vac.AddScope(@"C:\Windows\System32\", FileAccessPolicy.AllowReadAlways);
            vac.AddScopeCheck(@"C:\Windows", windows, FileAccessPolicy.AllowAll);
            vac.AddScopeCheck(@"C:\Windows\", windows, FileAccessPolicy.AllowAll);
            vac.AddScopeCheck(@"C:\windows\", windows, FileAccessPolicy.AllowAll);
            vac.AddScopeCheck(@"C:\Windows\System32", sys32, FileAccessPolicy.AllowReadAlways);
            vac.AddScopeCheck(@"C:\Windows\System32\", sys32, FileAccessPolicy.AllowReadAlways);

            TestManifestRetrieval(vac.DataItems, fam);
        }

        /// <summary>
        /// This test will exercise a simple manifest to locate the policies for
        /// completely disjoint paths and scopes. Also tests case-insensitive matching
        /// by using lower and upper case in paths to find. No paths added have trailing '\\'.
        /// </summary>
        [Fact]
        public void SimpleRecordManifestTest()
        {
            var pt = new PathTable();
            var fam =
                new FileAccessManifest(pt)
                {
                    FailUnexpectedFileAccesses = false,
                    IgnoreCodeCoverage = false,
                    ReportFileAccesses = false,
                    ReportUnexpectedFileAccesses = false,
                    MonitorChildProcesses = false
                };

            var vac = new ValidationDataCreator(fam, pt);

            AbsolutePath windows = vac.AddScope(@"c:\windows", FileAccessPolicy.Deny, FileAccessPolicy.AllowAll);
            AbsolutePath programFiles = vac.AddScope(@"c:\program files", FileAccessPolicy.AllowRead, FileAccessPolicy.AllowAll);
            vac.AddPath(@"c:\utils\foo.exe", FileAccessPolicy.ReportAccess);
            vac.AddScope(@"e:\dev\buildxl\out", FileAccessPolicy.AllowWrite, FileAccessPolicy.MaskNothing);

            vac.AddScopeCheck(
                @"c:\program files\microsoft visual studio 14.0\common7\packages\debugger\x64\msdia120.dll",
                programFiles,
                FileAccessPolicy.AllowRead);
            vac.AddScopeCheck(
                @"c:\program files\microsoft visual studio 14.0\common7\packages\debugger\x64",
                programFiles,
                FileAccessPolicy.AllowRead);
            vac.AddScopeCheck(
                @"c:\program files\microsoft visual studio 14.0\common7\packages\debugger\x64\",
                programFiles,
                FileAccessPolicy.AllowRead);
            vac.AddScopeCheck(
                @"C:\Windows\System32\cmd.exe",
                windows,
                FileAccessPolicy.Deny);

            TestManifestRetrieval(vac.DataItems, fam);
        }

        [Fact]
        public void TestLinkExeManifestExample()
        {
            var pt = new PathTable();
            var fam =
                new FileAccessManifest(pt)
                {
                    FailUnexpectedFileAccesses = false,
                    IgnoreCodeCoverage = false,
                    ReportFileAccesses = false,
                    ReportUnexpectedFileAccesses = false,
                    MonitorChildProcesses = false
                };

            var vac = new ValidationDataCreator(fam, pt);

            fam.AddScope(AbsolutePath.Invalid, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowReadIfNonexistent);
            vac.AddScope(@"C:\Users\User\AppData\Local\Microsoft\BuildXL\RestrictedTemp", FileAccessPolicy.Deny);
            vac.AddScope(@"C:\Users\User\AppData\Local\Microsoft\VsCommon\12.0\SQM", FileAccessPolicy.AllowAll);
            vac.AddScope(@"C:\Users\User\AppData\Local\Microsoft\Windows\INetCache", FileAccessPolicy.AllowAll);
            vac.AddScope(@"C:\Windows\", FileAccessPolicy.AllowAll);

            vac.AddPath(@"C:\windows\Globalization\Sorting\sortdefault.nls", FileAccessPolicy.AllowReadAlways, expectedEffectivePolicy: FileAccessPolicy.AllowAll);
            vac.AddPath(@"C:\windows\System32\tzres.dll", FileAccessPolicy.AllowReadAlways, expectedEffectivePolicy: FileAccessPolicy.AllowAll);
            vac.AddPath(@"E:\dev\BuildXL\Out\CoreXtPkgs\VisualCpp.BuildXL.12.0.30110.1\bin\x64\1033\LinkUI.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\CoreXtPkgs\VisualCpp.BuildXL.12.0.30110.1\bin\x64\Link.exe", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\CoreXtPkgs\VisualCpp.BuildXL.12.0.30110.1\bin\x64\mspdbsrv.exe", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-X64\Detours-lib\cl\detours.obj", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-X64\Detours-lib\cl_0\creatwth.obj", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-X64\Detours-lib\cl_1\disasm.obj", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-X64\Detours-lib\cl_2\image.obj", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-X64\Detours-lib\cl_3\modules.obj", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-X64\Detours-lib\Link\Detours.lib", FileAccessPolicy.AllowReadAlways);

            TestManifestRetrieval(vac.DataItems, fam);
        }

        [Fact]
        public void TestStyleCopCmdExeManifestExample()
        {
            var pt = new PathTable();
            var fam =
                new FileAccessManifest(pt)
                {
                    FailUnexpectedFileAccesses = false,
                    IgnoreCodeCoverage = false,
                    ReportFileAccesses = false,
                    ReportUnexpectedFileAccesses = false,
                    MonitorChildProcesses = false
                };

            var vac = new ValidationDataCreator(fam, pt);
            fam.AddScope(AbsolutePath.Invalid, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowReadIfNonexistent);

            AbsolutePath windowsPath = vac.AddScope(@"C:\windows", FileAccessPolicy.AllowAll);

            // check case-sensitivity on leading paths in non-exact path matches (canonical paths don't account for this in scopes)
            vac.AddScopeCheck(@"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscoreei.dll", windowsPath, FileAccessPolicy.AllowAll);

            vac.AddScope(@"C:\Users\User\AppData\Local\Microsoft\BuildXL\RestrictedTemp", FileAccessPolicy.Deny);
            vac.AddScope(@"C:\Users\User\AppData\Local\Microsoft\Windows\INetCache", FileAccessPolicy.AllowAll);
            vac.AddScope(@"C:\windows", FileAccessPolicy.AllowAll); // intentionally redundant, matches the order in the policy of the actual tool
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\StyleCop.BuildXL.4.7.48.0", FileAccessPolicy.AllowAll);
            vac.AddPath(@"E:\dev\BuildXL\External\StyleCop\Settings.StyleCop", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXL-Processes-dll\StyleCopCmd\StyleCopErrors.xml", FileAccessPolicy.AllowAll);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\StyleCopCmd-deployment\deployment\mscorlib.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\StyleCopCmd-deployment\deployment\StyleCop.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\StyleCopCmd-deployment\deployment\StyleCopCmd.exe", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\StyleCopCmd-deployment\deployment\StyleCopCmd.pdb", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\StyleCopCmd-deployment\deployment\System.Core.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\StyleCopCmd-deployment\deployment\System.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\StyleCopCmd-deployment\deployment\System.XML.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\ExitCodes.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\FileAccessManifest.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\FileAccessPolicy.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\FileAccessSetup.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\FileAccessStatus.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\Internal\AsyncStreamReader.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\Internal\BinaryPaths.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\Internal\DetouredProcess.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\Internal\NativeMethods.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\Internal\Pipes.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\Internal\SafeIOCompletionPortHandle.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\Internal\SafeNullHandle.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\Internal\SafeProcessHandle.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\Internal\SafeThreadHandle.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\Internal\SafeWaitHandleFromSafeHandle.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\ISandboxedProcessFileStorage.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\JobObject.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\PipEnvironment.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\Properties\AssemblyInfo.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\ReportedFileAccess.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\ReportedProcess.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\ReportType.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\SandboxedProcess.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\SandboxedProcessFile.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\SandboxedProcessInfo.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\SandboxedProcessOutput.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\SandboxedProcessOutputBuilder.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\SandboxedProcessPipExecutor.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\SandboxedProcessResult.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Processes\SandboxedProcessTimes.cs", FileAccessPolicy.AllowReadAlways);

            TestManifestRetrieval(vac.DataItems, fam);
        }

        [Fact]
        public void TestCscExeTestRunnerGenManifestExample()
        {
            var pt = new PathTable();
            var fam =
                new FileAccessManifest(pt)
                {
                    FailUnexpectedFileAccesses = false,
                    IgnoreCodeCoverage = false,
                    ReportFileAccesses = false,
                    ReportUnexpectedFileAccesses = false,
                    MonitorChildProcesses = false
                };

            var vac = new ValidationDataCreator(fam, pt);
            fam.AddScope(AbsolutePath.Invalid, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowReadIfNonexistent);

            vac.AddScope(@"C:\ProgramData\Microsoft\Crypto\RSA\MachineKeys", FileAccessPolicy.AllowAll);
            vac.AddScope(@"C:\Users\User\AppData\Local\Microsoft\BuildXL\RestrictedTemp", FileAccessPolicy.Deny);
            vac.AddScope(@"C:\Users\User\AppData\Local\Microsoft\Windows\INetCache", FileAccessPolicy.AllowAll);
            vac.AddScope(@"C:\windows", FileAccessPolicy.AllowAll);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\DotNetFxRefAssemblies.4.5.1.1\mscorlib.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\DotNetFxRefAssemblies.4.5.1.1\System.Core.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\DotNetFxRefAssemblies.4.5.1.1\System.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\MsBuild.12.0.21005.7\v12.0\bin\1033\alinkui.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\MsBuild.12.0.21005.7\v12.0\bin\1033\cscui.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\MsBuild.12.0.21005.7\v12.0\bin\csc.exe", FileAccessPolicy.AllowReadAlways, expectedUsn: new Usn(123));
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\MsBuild.12.0.21005.7\v12.0\bin\csc.exe.config", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\MsBuild.12.0.21005.7\v12.0\bin\csc.rsp", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\MsBuild.12.0.21005.7\v12.0\bin\default.win32manifest", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\VisualStudio.UnitTest.12.0.0\Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLRunnerGen-exe\ccrewrite\BuildXLRunnerGen.exe", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXL-Utilities-dll\ccrewrite\BuildXL.Utilities.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\Test-BuildXLRunnerGen-dll\csc\Test.BuildXLRunnerGen.dll", FileAccessPolicy.AllowAll);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\Test-BuildXLRunnerGen-dll\csc\Test.BuildXLRunnerGen.pdb", FileAccessPolicy.AllowAll);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\Test-BuildXL-TestUtilities-dll\ccrewrite\Test.BuildXL.TestUtilities.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\Support\BuildXL.DevKey.snk", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\Test.BuildXLRunnerGen\EmitTests.cs", FileAccessPolicy.AllowReadAlways, expectedUsn: new Usn(456));
            vac.AddPath(@"E:\dev\BuildXL\src\Test.BuildXLRunnerGen\EmitTestsBase.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\Test.BuildXLRunnerGen\ImplicitFlagRewriterTests.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\Test.BuildXLRunnerGen\LeaderElectionVisitorTests.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\Test.BuildXLRunnerGen\ParserTestBase.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\Test.BuildXLRunnerGen\ParserTests.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\Test.BuildXLRunnerGen\Properties\AssemblyInfo.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\Test.BuildXLRunnerGen\SymbolTableBuilderTests.cs", FileAccessPolicy.AllowReadAlways);

            TestManifestRetrieval(vac.DataItems, fam);
        }

        [Fact]
        public void TestGenManifestExample()
        {
            var pt = new PathTable();
            var fam =
                new FileAccessManifest(pt)
                {
                    FailUnexpectedFileAccesses = false,
                    IgnoreCodeCoverage = false,
                    ReportFileAccesses = false,
                    ReportUnexpectedFileAccesses = false,
                    MonitorChildProcesses = false
                };

            var vac = new ValidationDataCreator(fam, pt);
            fam.AddScope(AbsolutePath.Invalid, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowReadIfNonexistent);

            vac.AddScope(@"C:\ProgramData\Microsoft\NetFramework\BreadcrumbStore", FileAccessPolicy.AllowAll);
            vac.AddScope(@"C:\Users\User\AppData\Local\Microsoft\BuildXL\RestrictedTemp", FileAccessPolicy.Deny);
            vac.AddScope(@"C:\Users\User\AppData\Local\Microsoft\Windows\INetCache", FileAccessPolicy.AllowAll);
            vac.AddScope(@"C:\windows", FileAccessPolicy.AllowAll);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\CloudBuild.Cache.BondRpc.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\CloudBuild.Cache.Client.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\CloudBuild.Cache.Configuration.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\CloudBuild.Cache.Core.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\CloudBuild.Cache.Interfaces.Bond.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\CloudBuild.Cache.Interfaces.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\CloudBuild.Cache.ObjectStore.Client.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\CloudBuild.Cache.ObjectStore.Schema.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\CloudBuild.Cache.Service.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\CloudBuild.Cache.Velocity.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\CloudBuild.Utils.Base.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\CloudBuild.Utils.Bond.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\CloudBuild.Utils.FileSystem.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\CloudBuild.Utils.Logging.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\log4net.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\Microsoft.ApplicationInsights.Telemetry.Services.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\Microsoft.ApplicationInsights.Telemetry.Web.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\Microsoft.ApplicationServer.Caching.Client.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\Microsoft.ApplicationServer.Caching.Core.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\Microsoft.Bond.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\Microsoft.Bond.Interfaces.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\Microsoft.Bond.Rpc.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\Microsoft.Search.ObjectStore.Client.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\Microsoft.Search.TplExtensions.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\Microsoft.WindowsFabric.Common.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\Microsoft.WindowsFabric.Data.Common.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\CloudBuild.1.0.16\Release\ObjectStore.BondClient.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\DotNetFxRefAssemblies.4.5.1.1\mscorlib.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\DotNetFxRefAssemblies.4.5.1.1\System.Core.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\DotNetFxRefAssemblies.4.5.1.1\System.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\DotNetFxRefAssemblies.4.5.1.1\System.Runtime.Serialization.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Pkgs\DotNetFxRefAssemblies.4.5.1.1\System.XML.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXL-Pips-dll\ccrewrite\BuildXL.Pips.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXL-Storage-dll\ccrewrite\BuildXL.Storage.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddScope(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXL-Utilities-dll\BuildXLGen\", FileAccessPolicy.AllowAll);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXL-Utilities-dll\BuildXLGen\BuildXL.g.cs", FileAccessPolicy.AllowAll);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXL-Utilities-dll\BuildXLGen\BuildXL.g.xsd", FileAccessPolicy.AllowAll);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXL-Transformers-Shared-dll\ccrewrite\BuildXL.Transformers.Shared.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXL-Utilities-dll\ccrewrite\BuildXL.Utilities.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\BuildXL.ToolSupport.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\BuildXL.ToolSupport.pdb", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\BuildXL.Transformers.Shared.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\BuildXL.Transformers.Shared.pdb", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\BuildXL.Utilities.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\BuildXL.Utilities.pdb", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\BuildXLGen.exe", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\BuildXLGen.exe.config", FileAccessPolicy.AllowAll);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\BuildXLGen.pdb", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\Microsoft.CodeAnalysis.CSharp.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\Microsoft.CodeAnalysis.CSharp.pdb", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\Microsoft.CodeAnalysis.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\Microsoft.CodeAnalysis.pdb", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\mscorlib.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Collections.Concurrent.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Collections.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Collections.Immutable.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.ComponentModel.Annotations.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.ComponentModel.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.ComponentModel.EventBasedAsync.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Core.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Diagnostics.Contracts.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Diagnostics.Debug.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Diagnostics.Tools.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Diagnostics.Tracing.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Dynamic.Runtime.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Globalization.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.IO.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Linq.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Linq.Expressions.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Linq.Parallel.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Linq.Queryable.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Net.NetworkInformation.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Net.Primitives.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Net.Requests.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.ObjectModel.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Reflection.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Reflection.Emit.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Reflection.Emit.ILGeneration.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Reflection.Emit.Lightweight.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Reflection.Extensions.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Reflection.Metadata.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Reflection.Metadata.pdb", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Reflection.Primitives.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Resources.ResourceManager.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Runtime.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Runtime.Extensions.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Runtime.InteropServices.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Runtime.InteropServices.WindowsRuntime.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Runtime.Numerics.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Runtime.Serialization.Json.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Runtime.Serialization.Primitives.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Runtime.Serialization.Xml.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Security.Principal.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.ServiceModel.Duplex.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.ServiceModel.Http.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.ServiceModel.NetTcp.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.ServiceModel.Primitives.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.ServiceModel.Security.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Text.Encoding.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Text.Encoding.Extensions.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Text.RegularExpressions.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Threading.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Threading.Tasks.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Threading.Tasks.Parallel.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Threading.Timer.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.XML.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Xml.ReaderWriter.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Xml.XDocument.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\Out\Objects\Debug-AnyCpu\BuildXLGen-deployment\deployment\System.Xml.XmlSerializer.dll", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\CoreBuildXLList.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\CoreBuildXLMap.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Describer.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\BuildXLList`1.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\BuildXLList`2.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\BuildXLMapKeyValuePair.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\BuildXLMap`2.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\BuildXLMap`3.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\EnvironmentContext.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\EvaluationError.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\EvaluatorContext.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\GlobalSuppressions.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\ICallable.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\IIndexable.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\IKeyedIndexable.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\IMemberable.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\IQualifierValue.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\IReportableContext.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\ITransformer.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\ITransformerBuilder.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\ITransformerContextData.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\IValue.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\IValueBuilder.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\ContextInfo.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\EnumerationType.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\Env.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\Env.ValueData.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\LambdaClosure.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\LambdaDescriptor.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\LambdaDescriptorContext.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\LambdaException.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\ListOperation.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\LogicRegistrar.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\MemberDescriptor.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\MemberOccurrence.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\PrimitiveValueResolver.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\ReadOnlyBitArray.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\RegistrationAttribute.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\TransformerType.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\TypeData.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\ValueOperationType.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\ValueType.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\Visibility.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Logic\VisitationColor.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\MemberableBase.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Paths.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\PipBuilder.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\ProcessOutputs.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Properties\AssemblyInfo.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\QualifierId.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\QualifierTable.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Registration.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\TransformerContext.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\TransformerException.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\UnqualifiedLazy.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\UnqualifiedValue.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\ValueBase.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Values\IClrConfig.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Values\IExecutableDeployment.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Values\IRuntimeEnvironment.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Values\ITemplateBase.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Values\IVersion.cs", FileAccessPolicy.AllowReadAlways);
            vac.AddPath(@"E:\dev\BuildXL\src\BuildXL.Transformers\Values\StaticDirectory.cs", FileAccessPolicy.AllowReadAlways);

            TestManifestRetrieval(vac.DataItems, fam);
        }

        /// <summary>
        /// This test attempts to reproduce the access pattern in the test project
        /// which has 1 project and 100 C# files. (Access pattern made by csc.exe.)
        /// This is intended to be a stress test.
        /// </summary>
        private void TestSolutionMockupManifestTest(int numFiles)
        {
            var pt = new PathTable();
            var fam =
                new FileAccessManifest(pt)
                {
                    FailUnexpectedFileAccesses = false,
                    IgnoreCodeCoverage = false,
                    ReportFileAccesses = false,
                    ReportUnexpectedFileAccesses = false,
                    MonitorChildProcesses = false
                };

            var vac = new ValidationDataCreator(fam, pt);

            fam.AddScope(AbsolutePath.Invalid, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowReadIfNonexistent);
            vac.AddScopeCheck(@"C:\", AbsolutePath.Create(pt, @"C:\"), FileAccessPolicy.AllowReadIfNonexistent);

            AbsolutePath binPath = vac.AddScope(@"C:\Program Files (x86)\MsBuild\14.0\bin", FileAccessPolicy.AllowAll);
            vac.AddScopeCheck(@"C:\Program Files (x86)\MsBuild\14.0\bin\", binPath, FileAccessPolicy.AllowAll);
            vac.AddScopeCheck(@"C:\Program Files (x86)\MsBuild\14.0\bin\1033\cscui.dll", binPath, FileAccessPolicy.AllowAll);

            vac.AddPath(@"C:\Program Files (x86)\MsBuild\14.0\bin\csc.exe", FileAccessPolicy.AllowReadAlways, expectedEffectivePolicy: FileAccessPolicy.AllowAll);
            vac.AddScope(@"C:\Users\Default\AppData\Local\Microsoft\BuildXL\RestrictedTemp", FileAccessPolicy.Deny);
            vac.AddScope(@"C:\Users\Default\AppData\Local\Microsoft\Windows\INetCache", FileAccessPolicy.AllowAll);
            vac.AddScope(@"C:\Windows", FileAccessPolicy.AllowAll);

            vac.AddPath(@"E:\dev\BuildXL\TestSolution\Out\Objects\Debug-X64\Project0\csc\Project0.dll", FileAccessPolicy.AllowAll);
            vac.AddPath(@"E:\dev\BuildXL\TestSolution\Out\Objects\Debug-X64\Project0\csc\Project0.pdb", FileAccessPolicy.AllowAll);

            for (int i = 1; i <= numFiles; ++i)
            {
                vac.AddPath(
                    Path.Combine(@"E:\dev\BuildXL\TestSolution\Project0", string.Format("Class{0}.cs", i)),
                    FileAccessPolicy.AllowReadAlways);
            }

            vac.AddScope(@"E:\dev\BuildXL\TestSolution\Project0\Properties\AssemblyInfo.cs", FileAccessPolicy.AllowReadAlways);

            TestManifestRetrieval(vac.DataItems, fam);
        }

        [Fact]
        public void TestSolution100()
        {
            TestSolutionMockupManifestTest(100);
        }

        [Fact]
        public void TestSolution1000()
        {
            TestSolutionMockupManifestTest(1000);
        }

        [Fact]
        public void TestSolution5000()
        {
            TestSolutionMockupManifestTest(5000);
        }

        [Fact]
        public void TestSolution10000()
        {
            TestSolutionMockupManifestTest(10000);
        }

        private static void TestManifestRetrieval(IEnumerable<ValidationData> validationData, FileAccessManifest fam)
        {
            foreach (var line in fam.Describe())
            {
                Console.WriteLine(line);
            }

            byte[] manifestTreeBytes = fam.GetManifestTreeBytes();

            foreach (ValidationData dataItem in validationData)
            {
                uint nodePolicy;
                uint conePolicy;
                uint pathId;
                Usn expectedUsn;

                bool success =
                    global::BuildXL.Native.Processes.Windows.ProcessUtilitiesWin.FindFileAccessPolicyInTree(
                        manifestTreeBytes,
                        dataItem.Path,
                        new UIntPtr((uint)dataItem.Path.Length),
                        out conePolicy,
                        out nodePolicy,
                        out pathId,
                        out expectedUsn);

                XAssert.IsTrue(success, "Unable to find path in manifest");
                XAssert.AreEqual(
                    unchecked((uint)dataItem.PathId),
                    pathId,
                    string.Format("PathId for '{0}' did not match", dataItem.Path));

                if (dataItem.NodePolicy.HasValue)
                {
                    XAssert.AreEqual(
                        unchecked((uint)dataItem.NodePolicy.Value),
                        nodePolicy,
                        string.Format("Policy for '{0}' did not match", dataItem.Path));
                }

                if (dataItem.ConePolicy.HasValue)
                {
                    XAssert.AreEqual(
                        unchecked((uint)dataItem.ConePolicy.Value),
                        conePolicy,
                        string.Format("Policy for '{0}' did not match", dataItem.Path));
                }

                XAssert.AreEqual(
                    dataItem.ExpectedUsn,
                    expectedUsn,
                    string.Format("Usn for '{0}' did not match", dataItem.Path));
            }
        }

        private struct ValidationData
        {
            public string Path { get; set; }

            public FileAccessPolicy? NodePolicy { get; set; }

            public FileAccessPolicy? ConePolicy { get; set; }

            public int PathId { get; set; }

            public Usn ExpectedUsn { get; set; }
        }

        private class ValidationDataCreator
        {
            private readonly FileAccessManifest m_manifest;
            private readonly PathTable m_pathTable;

            public List<ValidationData> DataItems { get; private set; }

            public ValidationDataCreator(FileAccessManifest manifest, PathTable pathTable)
            {
                m_manifest = manifest;
                m_pathTable = pathTable;
                DataItems = new List<ValidationData>();
            }

            public AbsolutePath AddScope(
                string path,
                FileAccessPolicy values,
                FileAccessPolicy mask = FileAccessPolicy.Deny,
                FileAccessPolicy basePolicy = FileAccessPolicy.Deny)
            {
                AbsolutePath scopeAbsolutePath = AbsolutePath.Create(m_pathTable, path);
                var dataItem =
                    new ValidationData
                    {
                        Path = path,
                        PathId = scopeAbsolutePath.Value.Value,
                        NodePolicy = (basePolicy & mask) | values,
                        ConePolicy = null,
                        ExpectedUsn = ReportedFileAccess.NoUsn
                    };

                DataItems.Add(dataItem);
                m_manifest.AddScope(scopeAbsolutePath, mask, values);

                return scopeAbsolutePath;
            }

            public void AddPath(
                string path,
                FileAccessPolicy policy,
                FileAccessPolicy? expectedEffectivePolicy = null,
                Usn? expectedUsn = null)
            {
                AbsolutePath absolutePath = AbsolutePath.Create(m_pathTable, path);
                var dataItem =
                    new ValidationData
                    {
                        Path = path,
                        PathId = absolutePath.Value.Value,
                        ConePolicy = null,
                        NodePolicy = expectedEffectivePolicy ?? policy,
                        ExpectedUsn = expectedUsn ?? ReportedFileAccess.NoUsn
                    };

                DataItems.Add(dataItem);
                m_manifest.AddPath(absolutePath, values: policy, mask: FileAccessPolicy.MaskNothing, expectedUsn: expectedUsn);
            }

            public void AddScopeCheck(string path, AbsolutePath scopePath, FileAccessPolicy policy)
            {
                DataItems.Add(
                    new ValidationData
                    {
                        Path = path,
                        PathId = scopePath.Value.Value,
                        ConePolicy = policy,
                        NodePolicy = null,
                        ExpectedUsn = ReportedFileAccess.NoUsn
                    });
            }
        }
    }
}
