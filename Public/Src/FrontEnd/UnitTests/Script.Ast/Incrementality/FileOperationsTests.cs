// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Incrementality
{
    public class FileOperationsTests : DsTestWithCacheBase
    {
        public FileOperationsTests(ITestOutputHelper output)
            : base(output, usePassThroughFileSystem: true)
        {
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void TestFileAndDirectoryExistence()
        {
            // This test makes sure that real input tracker works fine with file/directory existence logic
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                var testRoot = tempFiles.GetUniqueDirectory(PathTable).ToString(PathTable);

                string spec = @"
const fileShouldExist = File.exists(f`foo/a.txt`);
export const r1 = (()=>{Contract.assert(fileShouldExist === true);})();

const fileShouldNotExist = File.exists(f`foo/missing.txt`);
export const r2 = (()=>{Contract.assert(fileShouldNotExist === false);})();

const directoryShouldExist = Directory.exists(d`foo`);
export const r3 = (()=>{Contract.assert(directoryShouldExist === true);})();

const directoryShouldNotExist = Directory.exists(d`bar`);
export const r4 = (()=>{Contract.assert(directoryShouldNotExist === false);})();
";

                var config = Build()
                    .EmptyConfiguration()
                    .TestRootDirectory(testRoot)
                    .AddSpec("spec1.dsc", spec)
                    .AddFile("foo/a.txt", "")
                    .PersistSpecsAndGetConfiguration();

                var specs = RunAndRetrieveSpecs(config, appDeployment);
            }
        }

        protected override FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged)
        {
            var conf = base.GetFrontEndConfiguration(isDebugged);
            conf.MaxFrontEndConcurrency = 1;
            conf.DebugScript = isDebugged;
            conf.PreserveFullNames = true;
            conf.NameResolutionSemantics = NameResolutionSemantics.ImplicitProjectReferences;
            conf.UseSpecPublicFacadeAndAstWhenAvailable = true;
            conf.ConstructAndSaveBindingFingerprint = true;
            conf.ReloadPartialEngineStateWhenPossible = true;
            return conf;
        }
    }
}
