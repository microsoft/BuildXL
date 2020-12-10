// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Reflection;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.VBCSCompilerLogger
{
    public class VBCSCompilerLoggerTests : TemporaryStorageTestBase
    {
        public VBCSCompilerLoggerTests(ITestOutputHelper output) : base(output)
        {}

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