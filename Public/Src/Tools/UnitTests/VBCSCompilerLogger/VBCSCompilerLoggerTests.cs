// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Reflection;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Necessary to not collide with this file's namespace.
using Logger = VBCSCompilerLogger.VBCSCompilerLogger;

namespace Test.VBCSCompilerLogger
{
    public class VBCSCompilerLoggerTests : TemporaryStorageTestBase
    {
        private const string CscArgs = @"/out:MyProgram.exe /target:exe Program.cs";

        private const string VbcArgs = @"/out:MyProgram.exe /target:exe Program.vb";

        public static IEnumerable<object[]> HappyArgumentParsingData =>  RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? HappyArgumentParsingDataWindows() 
            : HappyArgumentParsingDataLinux();

        private static IEnumerable<object[]> HappyArgumentParsingDataWindows()
        {
            yield return new object[] { true, @"C:some\path\to\csc.exe " + CscArgs };
            yield return new object[] { true, @"C:\Program Files\dotnet\dotnet.exe exec ""C:some\path\to\csc.dll"" " + CscArgs };
            yield return new object[] { false, @"C:some\path\to\vbc.exe " + VbcArgs };
            yield return new object[] { false, @"C:\Program Files\dotnet\dotnet.exe exec ""C:some\path\to\vbc.dll"" " + VbcArgs };
        }

        private static IEnumerable<object[]> HappyArgumentParsingDataLinux()
        {
            yield return new object[] { true, @"/some/path/to/csc " + CscArgs };
            yield return new object[] { true, @"/usr/bin/dotnet exec ""/some/path/to/csc.dll"" " + CscArgs };
            yield return new object[] { false, @"/some/path/to/vbc " + VbcArgs };
            yield return new object[] { false, @"/usr/bin/dotnet exec ""/some/path/to/vbc.dll"" " + VbcArgs };
        }

        public static IEnumerable<object[]> ErroneousArgumentParsingData => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ErroneousArgumentParsingDataWindows() 
            : ErroneousArgumentParsingDataLinux();

        private static IEnumerable<object[]> ErroneousArgumentParsingDataWindows()
        {
            yield return new object[] { true, @"C:some\path\to\csc.abc " + CscArgs };
            yield return new object[] { true, @"C:some\path\to\csc.exe" };
            yield return new object[] { true, @"C:\Program Files\dotnet\dotnet.exe exec ""C:some\path\to\csc.dll" };
            yield return new object[] { true, @"C:\Program Files\dotnet\dotnet.exe exec ""C:some\path\to\csc.abc"" " + CscArgs };
            yield return new object[] { true, @"C:\Program Files\dotnet\dotnet.exe exec ""C:some\path\to\csc.abc" };
            yield return new object[] { false, @"C:some\path\to\vbc.abc " + VbcArgs };
            yield return new object[] { false, @"C:some\path\to\vbc.exe" };
            yield return new object[] { false, @"C:\Program Files\dotnet\dotnet.exe exec ""C:some\path\to\vbc.dll" };
            yield return new object[] { false, @"C:\Program Files\dotnet\dotnet.exe exec ""C:some\path\to\vbc.abc"" " + VbcArgs };
            yield return new object[] { false, @"C:\Program Files\dotnet\dotnet.exe exec ""C:some\path\to\vbc.abc" };
        }

        private static IEnumerable<object[]> ErroneousArgumentParsingDataLinux()
        {
            yield return new object[] { true, @"/some/path/to/csc.abc " + CscArgs };
            yield return new object[] { true, @"/some/path/to/csc" };
            yield return new object[] { true, @"/usr/bin/dotnet exec ""/some/path/to/csc.dll""" };
            yield return new object[] { true, @"/usr/bin/dotnet exec ""/some/path/to/csc.abc"" " + CscArgs };
            yield return new object[] { true, @"/usr/bin/dotnet exec ""/some/path/to/csc.abc""" };
            yield return new object[] { false, @"/some/path/to/vbc.abc " + VbcArgs };
            yield return new object[] { false, @"/some/path/to/vbc" };
            yield return new object[] { false, @"/usr/bin/dotnet exec ""/some/path/to/vbc.dll"""};
            yield return new object[] { false, @"/usr/bin/dotnet exec ""/some/path/to/vbc.abc"" " + VbcArgs };
            yield return new object[] { false, @"/usr/bin/dotnet exec ""/some/path/to/vbc.abc""" };
        }

        public VBCSCompilerLoggerTests(ITestOutputHelper output) : base(output)
        {}

        [Theory]
        [MemberData(nameof(HappyArgumentParsingData))]
        public void HappyArgumentParsing(bool isCscTask, string commandLine)
        {
            XAssert.IsTrue(Logger.TryGetArgumentsFromCommandLine(isCscTask ? "Csc" : "Vbc", commandLine, out string arguments, out string error));
            XAssert.AreEqual(isCscTask ? CscArgs : VbcArgs, arguments);
            XAssert.IsEmpty(error);
        }

        [Theory]
        [MemberData(nameof(ErroneousArgumentParsingData))]
        public void ErroneousArgumentParsing(bool isCscTask, string commandLine)
        {
            XAssert.IsFalse(Logger.TryGetArgumentsFromCommandLine(isCscTask ? "Csc" : "Vbc", commandLine, out string arguments, out string error));
            XAssert.IsNull(arguments);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void NewSwitchMakesLoggerFail() 
        {
            // AnalyzerConfigFile is an option that the older version of CodeAnalysis does not support. However this is supported
            // by the csc version in use.
            
            // Create an empty analyzer config file
            string pathToEmptyAnalyzerConfig = GetFullPath("dummy.config");
            File.WriteAllText(pathToEmptyAnalyzerConfig, string.Empty);
            
            var result = RunMSBuild($"AnalyzerConfigFiles='{pathToEmptyAnalyzerConfig}'", out string standardOutput);
            
            // The run should fail
            XAssert.AreNotEqual(0, result);

            // The reason should be because the logger detected an unrecognized option for csc.exe (CS2007)
            XAssert.Contains(standardOutput, "InvalidOperationException");
            XAssert.Contains(standardOutput, "CS2007");
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void IncorrectSwitchDoesNotFailLogger()
        {
            var result = RunMSBuild($"Win32ManifestFile='does/not/exist'", out string standardOutput);

            // The run should fail
            XAssert.AreNotEqual(0, result);

            // The reason should be because an unexpected task attribute (MSB4064), but not because of a logger failure
            XAssert.ContainsNot(standardOutput, "InvalidOperationException");
            XAssert.Contains(standardOutput, "MSB4064");
        }

        /// <summary>
        /// <c>/sdkpath:</c> (added in dotnet/roslyn#79911) is accepted by newer csc/vbc, so an older
        /// Microsoft.CodeAnalysis emitting CS2007 for it must be treated as non-blocking.
        /// </summary>
        [Theory]
        [InlineData(@"/sdkpath:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\", false)]
        [InlineData(@"-sdkpath:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\", false)]
        [InlineData(@"/SDKPATH:C:\some\dir", false)] // case-insensitive
        [InlineData(@"/sdkpath:""C:\path with space""", false)] // quoted value
        [InlineData(@"/sdkpath:", false)] // empty value (still recognized as /sdkpath form)
        [InlineData(@"/somethingelse:foo", true)] // unrelated unknown switch must still fail
        [InlineData(@"/sdkpathish:foo", true)] // partial-name match must not be allowlisted
        [InlineData(@"/analyzerconfig:foo", true)] // existing real-switch case still fails
        public void IsBlockingBadSwitchRecognizesSdkPath(string unknownArg, bool expectedBlocking)
        {
            var descriptor = new DiagnosticDescriptor(
                id: "CS2007",
                title: "Unrecognized option",
                messageFormat: "Unrecognized option: '{0}'",
                category: "Compiler",
                defaultSeverity: DiagnosticSeverity.Error,
                isEnabledByDefault: true);
            Diagnostic diagnostic = Diagnostic.Create(descriptor, location: null, messageArgs: unknownArg);

            XAssert.AreEqual(expectedBlocking, Logger.IsBlockingBadSwitch(diagnostic));
        }

        /// <summary>
        /// A non-CS2007/BC2007 diagnostic is never blocking, even if its message mentions <c>/sdkpath:</c>.
        /// </summary>
        [Fact]
        public void IsBlockingBadSwitchIgnoresNon2007Diagnostics()
        {
            var descriptor = new DiagnosticDescriptor(
                id: "CS5001",
                title: "Some other error",
                messageFormat: "Unrecognized option: '{0}'",
                category: "Compiler",
                defaultSeverity: DiagnosticSeverity.Error,
                isEnabledByDefault: true);
            Diagnostic diagnostic = Diagnostic.Create(descriptor, location: null, messageArgs: "/sdkpath:C:\\some\\dir");

            XAssert.IsFalse(Logger.IsBlockingBadSwitch(diagnostic));
        }

        /// <summary>
        /// Pins the CS2007 message format the filter relies on against a live Roslyn invocation.
        /// </summary>
        [Fact]
        public void CS2007MessageFormatIsAsExpected()
        {
            const string Unknown = "/definitely-not-a-real-csc-switch:foo";
            var args = new[] { Unknown, "/out:Out.exe", "Program.cs" };
            CommandLineArguments parsed = CSharpCommandLineParser.Default.Parse(
                args,
                baseDirectory: System.IO.Path.GetTempPath(),
                sdkDirectory: System.IO.Path.GetTempPath());

            Diagnostic cs2007 = parsed.Errors.FirstOrDefault(d => d.Id == "CS2007");
            XAssert.IsNotNull(cs2007);

            // The filter relies on the unknown arg appearing single-quoted in the message.
            XAssert.Contains(cs2007.GetMessage(), "'" + Unknown + "'");
        }

        /// <summary>
        /// End-to-end repro: the bundled Microsoft.CodeAnalysis (older than dotnet/roslyn#79911) emits
        /// CS2007 for a real <c>/sdkpath:</c> argument, and the filter must classify it as non-blocking.
        /// </summary>
        [Fact]
        public void SdkPathSwitchDoesNotFailLogger()
        {
            string tempDir = GetFullPath("sdkpath-end-to-end");
            Directory.CreateDirectory(tempDir);
            string project = Path.Combine(tempDir, "p.csproj");
            File.WriteAllText(project, "<Project/>");

            string arguments = $"/sdkpath:\"{tempDir}\" /out:Out.dll /target:library Program.cs";

            CommandLineArguments parsed = global::VBCSCompilerLogger.CompilerUtilities.GetParsedCommandLineArguments(
                LanguageNames.CSharp, arguments, project, out _);

            Diagnostic[] cs2007Errors = parsed.Errors.Where(d => d.Id == "CS2007").ToArray();

            // Guard against a vacuous pass: if a package bump teaches the parser /sdkpath, this fires
            // so the test is revisited rather than silently no longer reproducing the bug.
            XAssert.IsTrue(cs2007Errors.Length > 0, "Expected bundled Microsoft.CodeAnalysis to emit CS2007 for /sdkpath.");

            foreach (Diagnostic cs2007 in cs2007Errors)
            {
                XAssert.IsFalse(
                    Logger.IsBlockingBadSwitch(cs2007),
                    $"Expected CS2007 for /sdkpath to be non-blocking, but the filter rejected: {cs2007.GetMessage()}");
            }
        }

        #region Helpers

        // Keep in sync with deployment
        private string PathToCscTaskDll() => Path.Combine(TestDeploymentDir, "Compilers", "net472", "tools", "Microsoft.Build.Tasks.CodeAnalysis.dll").Replace("\\", "/");
        private string PathToMSBuild() => Path.Combine(TestDeploymentDir, "msbuild", "net472", "msbuild.exe");
        private string PathToVBCSCompilerLogger() => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Logger", "VBCSCompilerLoggerOldCodeAnalysis.dll");
        private string HellowWorldProgram() =>
@"
using System;
namespace CscCompilation
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(""Hello World!"");
        }
    }
}";

        private string CscProject(string extraArgs = null) =>
$@"<Project DefaultTargets='Build'>
    <UsingTask TaskName='Csc' AssemblyFile = '{PathToCscTaskDll()}'/>

    <Target Name='Build'>
      <Csc
        OutputAssembly='Out.exe'
        TargetType='exe'
        EmitDebugInformation='true'
        Sources='Program.cs' 
        {extraArgs ?? string.Empty}
      />
    </Target>
</Project>";

        private int RunMSBuild(string extraArgs, out string stdOut)
        {
            string project = CscProject(extraArgs);
            string pathToProject = GetFullPath("project.csproj");

            File.WriteAllText(GetFullPath("Program.cs"), HellowWorldProgram());
            File.WriteAllText(pathToProject, project);

            string args = @$"-logger:""{PathToVBCSCompilerLogger()}"" -nodeReuse:false -m:1 ""{pathToProject}""";

            // We don't really need this to run in a sandboxed process, but we need to fake the presence of detours since the logger will try to
            // report augmented accesses
            using (FileStream fs = new FileStream(GetFullPath("detours.fake"), FileMode.CreateNew, FileAccess.Write, FileShare.Write | FileShare.Inheritable))
            {
                string detoursHandleAsString = fs.SafeFileHandle.DangerousGetHandle().ToString();

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = PathToMSBuild(),
                        Arguments = args,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };

                // CODESYNC: Keep variable name in sync with DetoursServices on the C++ side
                process.StartInfo.Environment.Add("BUILDXL_AUGMENTED_MANIFEST_HANDLE", detoursHandleAsString);

                process.Start();
                process.WaitForExit();

                stdOut = process.StandardOutput.ReadToEnd();
                return process.ExitCode;
            }
        }
        #endregion
    }
}