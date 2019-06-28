// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading;
using BuildXL.Engine;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Core.Incrementality;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Incrementality
{
    public class PublicFacadeAndAstOptimizationTests : DsTestWithCacheBase
    {
        public PublicFacadeAndAstOptimizationTests(ITestOutputHelper output)
            : base(output, usePassThroughFileSystem: true)
        {
        }

        /// <summary>
        /// Flag for using public facades and serialized AST when available is on
        /// </summary>
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

        [Fact]
        public void AllSpecsAreSerialized()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                var config = Build()
                    .EmptyConfiguration()
                    .AddSpec("spec1.dsc", "export const x = 42;")
                    .AddSpec("spec2.dsc", "export const y = 53;")
                    .AddSpec("spec3.dsc", "export const z = 64;")
                    .RootSpec("spec1.dsc")
                    .PersistSpecsAndGetConfiguration(enableSpecCache: true);

                var specs = RunAndRetrieveSpecs(config, appDeployment);

                // Now that the controller is disposed, all public facades + asts have been serialized

                // We create a new public facade provider to verify everything was saved. A basic front end engine abstraction should be fine, since
                // the retrieve method only relies on the engine abstraction to compute the proper hashes.
                var publicFacadeProvider = GetPublicFacadeProvider(config);

                foreach (var spec in specs)
                {
                    var result = publicFacadeProvider.TryGetPublicFacadeWithAstAsync(spec);
                    Assert.True(result != null);
                }
            }
        }

        [FactIfSupported(requiresJournalScan: true, Skip = "Flaky test")]
        public void PublicFacadeAndAstIsReusedForCleanSpecs()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                // The same testRootDirectory is used for both invocations, so the spec cache can be reused.
                var testRoot = Path.Combine(TestOutputDirectory, Guid.NewGuid().ToString("N"));

                var config = BuildWithoutDefautlLibraries()
                    .AddPrelude()
                    .EmptyConfiguration()
                    .TestRootDirectory(testRoot)
                    .AddSpec("spec1.dsc", "export const x = 42;")
                    .AddSpec("spec2.dsc", "export const y = x;")
                    .AddSpec("spec3.dsc", "export const w = y;")
                    .AddSpec("spec4.dsc", "export const z = w;")
                    .PersistSpecsAndGetConfiguration(enableSpecCache: true);

                RunAndRetrieveSpecs(config, appDeployment);

                // Now we change spec3 in a way the binding fingerprint does not change for it
                config = BuildWithoutDefautlLibraries()
                    .AddPrelude()
                    .EmptyConfiguration()
                    .TestRootDirectory(testRoot)
                    .AddSpec("spec3.dsc", "export const w = y;//safe change")
                    .PersistSpecsAndGetConfiguration(cleanExistingDirectory: false, enableSpecCache: true);

                // Since the spec2spec binding should be spec1 <- spec2 <- spec3 <-spec 4, by changing spec3 without changing the binding information, spec3 and 4 
                // should become dirty. On the other hand, spec1 and spec2 should be reused from the previous run
                using (var controller = RunEngineAndGetFrontEndHostController(config, appDeployment, testRoot, rememberAllChangedTrackedInputs: true))
                {
                  
                    var workspace = controller.Workspace;
                    var sourceFiles = workspace.GetAllSourceFiles();
                    foreach (var sourceFile in sourceFiles)
                    {
                        var fileName = Path.GetFileName(sourceFile.Path.AbsolutePath);
                        if (fileName.Equals("spec1.dsc", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Equals("spec2.dsc", StringComparison.OrdinalIgnoreCase))
                        {
                            Assert.True(sourceFile.IsPublicFacade);
                        }
                        else if (fileName.Equals("spec3.dsc", StringComparison.OrdinalIgnoreCase) ||
                                 fileName.Equals("spec4.dsc", StringComparison.OrdinalIgnoreCase))
                        {
                            Assert.False(sourceFile.IsPublicFacade, $"{sourceFile.Path.AbsolutePath} should not be a public facade.");
                        }
                    }
                }
            }
        }

        [FactIfSupported(requiresJournalScan: true, Skip = "Failed often")]
        public void PublicFacadeAndAstIsReusedForSpecsWithoutPublicSurfaceChanges()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                // The same testRootDirectory is used for both invocations, so the spec cache can be reused.
                var testRoot = tempFiles.GetUniqueDirectory(PathTable).ToString(PathTable);

                var config = BuildWithoutDefautlLibraries()
                    .AddPrelude()
                    .EmptyConfiguration()
                    .TestRootDirectory(testRoot)
                    .AddSpec("spec1.dsc", "export const x = 42;")
                    .AddSpec("spec2.dsc", "export const y = x;")
                    .AddSpec("spec3.dsc", "export const w = y;")
                    .AddSpec("spec4.dsc", "export const z = w;")
                    .AddSpec("spec5.dsc", "export const notUsedYet = 42;")
                    .PersistSpecsAndGetConfiguration(enableSpecCache: true);

                RunAndRetrieveSpecs(config, appDeployment);

                // Now we change spec3 in a way the binding fingerprint does not change for it
                config = BuildWithoutDefautlLibraries()
                    .AddPrelude()
                    .EmptyConfiguration()
                    .TestRootDirectory(testRoot)
                    .AddSpec("spec3.dsc", "export const w = notUsedYet;")
                    .PersistSpecsAndGetConfiguration(cleanExistingDirectory: false, enableSpecCache: true);

                // Even though the spec3 has changed reasonably and now has another dependency
                // it should not prevent us from using public facades for unafected specs.
                using (var controller = RunEngineAndGetFrontEndHostController(config, appDeployment, testRoot, rememberAllChangedTrackedInputs: true))
                {
                    var workspace = controller.Workspace;
                    var sourceFiles = workspace.GetAllSourceFiles();
                    foreach (var sourceFile in sourceFiles)
                    {
                        if (Path.GetFileName(sourceFile.Path.AbsolutePath).EndsWith("spec1.dsc") ||
                            Path.GetFileName(sourceFile.Path.AbsolutePath).EndsWith("spec2.dsc") ||
                            Path.GetFileName(sourceFile.Path.AbsolutePath).EndsWith("spec5.dsc"))
                        {
                            Assert.True(sourceFile.IsPublicFacade, $"{sourceFile.Path.AbsolutePath} should be a public facade.");
                        }
                    }
                }
            }
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void PublicFacadeAndAstAreNotReusedForSpecsWithPublicSurfaceChanges()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                // The same testRootDirectory is used for both invocations, so the spec cache can be reused.
                var testRoot = tempFiles.GetUniqueDirectory(PathTable).ToString(PathTable);

                var config = Build()
                    .EmptyConfiguration()
                    .TestRootDirectory(testRoot)
                    .AddSpec("spec1.dsc", "export const x = 42;")
                    .AddSpec("spec2.dsc", "export const y = x;")
                    .AddSpec("spec3.dsc", "export const w = y;")
                    .AddSpec("spec4.dsc", "export const z = w;")
                    .AddSpec("spec5.dsc", "export const notUsedYet = 42;")
                    .PersistSpecsAndGetConfiguration(enableSpecCache: true);

                RunAndRetrieveSpecs(config, appDeployment);

                // Changing the declartion fingerprint for the spec 3.
                config = Build()
                    .EmptyConfiguration()
                    .TestRootDirectory(testRoot)
                    .AddSpec("spec3.dsc", "export const w = y; export const another = 42;; //unsafe change")
                    .PersistSpecsAndGetConfiguration(cleanExistingDirectory: false, enableSpecCache: true);

                // Now all the specs should be fully parsed.
                using (var controller = RunEngineAndGetFrontEndHostController(config, appDeployment, testRoot, rememberAllChangedTrackedInputs: true))
                {
                    var workspace = controller.Workspace;
                    var sourceFiles = workspace.GetAllSourceFiles();
                    foreach (var sourceFile in sourceFiles)
                    {
                        Assert.False(sourceFile.IsPublicFacade, $"{sourceFile.Path.AbsolutePath} should not be a public facade.");
                    }
                }
            }
        }

        #region helpers
        private FrontEndPublicFacadeAndAstProvider GetPublicFacadeProvider(ICommandLineConfiguration config)
        {
            return new FrontEndPublicFacadeAndAstProvider(
                new BasicFrontEndEngineAbstraction(PathTable, FileSystem),
                LoggingContext,
                config.Layout.EngineCacheDirectory.ToString(PathTable),
                config.FrontEnd.LogStatistics,
                PathTable,
                new FrontEndStatistics(),
                new CancellationToken(false));
        }
        #endregion
    }
}
