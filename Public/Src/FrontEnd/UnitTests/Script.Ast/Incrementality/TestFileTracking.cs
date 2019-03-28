// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Engine;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.FrontEnd.Script.Constants.Names;

namespace Test.DScript.Ast.Incrementality
{
    public class TestFileTracking : DsTestWithCacheBase
    {
        public TestFileTracking(ITestOutputHelper output)
            : base(output, usePassThroughFileSystem: true)
        {
        }

        /// <summary>
        /// For any <see cref="BuildDefinition"/>, the following should hold true:
        ///   - the first time it's built (as is, without changing any specs) --> graph cache miss is expected
        ///   - the next time it's built (again as is, without changing anything) --> graph cache hit is expected
        ///   - then for each spec in the build definition (whatever it may be: a config file, a file list, a module config, a project spec, etc.),
        ///       - when the spec is changed the following build should result in a graph cache miss;
        ///       - a build immediately after that (without changing anything) should result in a graph cache hit.
        /// </summary>
        [MemberData(nameof(GetVariousBuildDefinitions))]
        [Theory]
        public void TestInputChangesTriggerGraphCacheMiss(BuildDefinition buildDefinition)
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                // The same testRootDirectory is used for all invocations, so the spec cache can be reused.
                var testRoot = tempFiles.GetUniqueDirectory(PathTable).ToString(PathTable);

                // run once to begin with and assert cache miss
                RunAndAssertGraphCacheMiss(WriteSpecs(testRoot, buildDefinition), appDeployment);

                // for each spec
                foreach (var specTuple in buildDefinition.Specs)
                {
                    // run again without changing anything and assert graph cache hit
                    RunAndAssertGraphCacheHit(WriteSpecs(testRoot, buildDefinition), appDeployment);

                    // change selected spec and assert graph cache miss
                    buildDefinition.AppendToSpecContent(specPath: specTuple.SpecPath, contentToAppend: " ");
                    RunAndAssertGraphCacheMiss(WriteSpecs(testRoot, buildDefinition), appDeployment);
                }
            }
        }

        public static IEnumerable<object[]> GetVariousBuildDefinitions()
        {
            // simple with legacy names
            yield return new[]
            {
                new BuildDefinition
                {
                    [ConfigDsc] = "config({});",
                    [PackageConfigDsc] = ModuleConfigurationBuilder.V1Module("MyModule"),
                    [PackageDsc]       = "export const x = 42;"
                }
            };

            var myModuleConfigFile = "MyModule/" + ModuleConfigBm;
            var myModuleProjectFile = "MyModule/build.dsc";
            var myModuleContent = ModuleConfigurationBuilder.V2Module("MyModule");

            // simple with V2 names
            yield return new[]
            {
                new BuildDefinition
                {
                    [ConfigBc] = "config({});",
                    [myModuleConfigFile]       = myModuleContent,
                    [myModuleProjectFile]      = "@@public export const x = 42;"
                }
            };

            // with list file in module config
            yield return new[]
            {
                new BuildDefinition
                {
                    [ConfigBc] = "config({});",
                    [myModuleConfigFile]       = ModuleConfigurationBuilder.V2Module("MyModule").WithExtraFields("projects: importFile(f`prjs.bl`).projects"),
                    ["MyModule/prjs.bl"]       = "export const projects = [f`build.bp`];",
                    ["MyModule/build.bp"]      = "@@public export const x = 42;"
                }
            };

            // with build list in primary config
            var preludeCfg = SpecEvaluationBuilder.PreludePackageConfigRelativePathDsc;
            yield return new[]
            {
                new BuildDefinition
                {
                    [ConfigBc] = "config({ modules: importFile(f`cfg.bl`).modules });",
                    ["cfg.bl"]                 = $"export const modules = [f`{preludeCfg}`, f`{myModuleConfigFile}`];",
                    [myModuleConfigFile]       = ModuleConfigurationBuilder.V2Module("MyModule").WithExtraFields("projects: importFile(f`prjs.bl`).projects"),
                    ["MyModule/prjs.bl"]       = "export const projects = [f`build.bp`];",
                    ["MyModule/build.bp"]      = "@@public export const x = 42;"
                }
            };
        }

        [Fact]
        public void FileProbShouldInvalidateTheCache()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                // The same testRootDirectory is used for all invocations, so the spec cache can be reused.
                var testRoot = tempFiles.GetUniqueDirectory(PathTable).ToString(PathTable);

                // Spec probs the file existence.
                string spec = @"export const r = File.exists(f`../foo/a.txt`);";
                var buildDefinition = CreateDefinition(spec);

                RunAndAssertGraphCacheMiss(WriteSpecs(testRoot, buildDefinition), appDeployment);

                // Adding a probbed file should lead to a cache miss
                buildDefinition["../foo/a.txt"] = "";

                RunAndAssertGraphCacheMiss(WriteSpecs(testRoot, buildDefinition), appDeployment);
            }
        }

        [Fact]
        public void RemovingTheFileShouldInvalidateTheCache()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                // The same testRootDirectory is used for all invocations, so the spec cache can be reused.
                var testRoot = tempFiles.GetUniqueDirectory(PathTable).ToString(PathTable);

                // Spec probs the file existence.
                string spec = @"export const r = File.exists(f`foo/a.txt`);";
                var buildDefinition = CreateDefinition(spec);
                buildDefinition["foo/a.txt"] = "1";

                RunAndAssertGraphCacheMiss(WriteSpecs(testRoot, buildDefinition), appDeployment);

                // Creating another definition without "foo/a.txt".
                // but since we are reusing the same testroot we have to delete thefile that is going missing.
                File.Delete(Path.Combine(this.RelativeSourceRoot, "Foo", "a.txt"));
                // This should lead to a cache miss.
                buildDefinition = CreateDefinition(spec);
                RunAndAssertGraphCacheMiss(WriteSpecs(testRoot, buildDefinition), appDeployment);
            }
        }

        [Fact]
        public void FileProbShouldNotInvalidateTheCacheIfAnotherFileWasAdded()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                // The same testRootDirectory is used for all invocations, so the spec cache can be reused.
                var testRoot = tempFiles.GetUniqueDirectory(PathTable).ToString(PathTable);

                string spec = @"export const r = File.exists(f`../foo/a.txt`);";
                var buildDefinition = CreateDefinition(spec);

                RunAndAssertGraphCacheMiss(WriteSpecs(testRoot, buildDefinition), appDeployment);

                // Adding a random file (that was not probed) should not affect the cache.
                buildDefinition["../foo/b.txt"] = "";

                RunAndAssertGraphCacheHit(WriteSpecs(testRoot, buildDefinition), appDeployment);
            }
        }

        [Fact]
        public void PerturbingFileChangeTrackerShouldNotAffectMatchingInput()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);
                var testRoot = tempFiles.GetUniqueDirectory(PathTable).ToString(PathTable);
                const string SpecFile = "spec.dsc";
                var buildDefinition = CreateDefinition(@"export const x = 42;", SpecFile);
                var configuration = WriteSpecs(testRoot, buildDefinition);
                RunAndAssertGraphCacheMiss(configuration, appDeployment, rememberAllChangedTrackedInputs: true);

                // Copy tracker.
                var trackerPath = Path.Combine(
                    configuration.Layout.EngineCacheDirectory.ToString(FrontEndContext.PathTable),
                    EngineSerializer.PreviousInputsJournalCheckpointFile);
                var copiedTrackerPath = trackerPath + ".copy";
                XAssert.IsTrue(File.Exists(trackerPath));
                File.Copy(trackerPath, copiedTrackerPath);

                // Modify spec.
                buildDefinition.AppendToSpecContent(SpecFile, " ");
                RunAndAssertGraphCacheMiss(WriteSpecs(testRoot, buildDefinition), appDeployment, rememberAllChangedTrackedInputs: true);

                // Perturb tracker, by moving the old one to its original location, and thus will have wrong envelope id.
                File.Delete(trackerPath);
                File.Move(copiedTrackerPath, trackerPath);

                // Modify spec again.
                buildDefinition.AppendToSpecContent(SpecFile, " ");
                var hostController = RunEngineAndGetFrontEndHostController(
                    WriteSpecs(testRoot, buildDefinition), 
                    appDeployment, 
                    null, 
                    true,
                    engineTestHooksData => {
                        XAssert.IsTrue(ContainsFileName(engineTestHooksData.GraphReuseResult.InputChanges.ChangedPaths, SpecFile));
                        XAssert.IsFalse(ContainsFileName(engineTestHooksData.GraphReuseResult.InputChanges.UnchangedPaths.Keys, SpecFile));
                    });
                AssertLogged(LogEventId.EndSerializingPipGraph);
                hostController.Dispose();
            }
        }

        private static BuildDefinition CreateDefinition(string specContent, string specFile = "spec.dsc")
        {
            return new BuildDefinition
            {
                [ConfigBc] = "config({projects: [f`" + specFile + "`]});",
                [specFile] = specContent,
            };
        }

        private ICommandLineConfiguration WriteSpecs(string testRoot, BuildDefinition contents)
        {
            SpecEvaluationBuilder specBuilder = contents
                .Specs
                .Aggregate(
                    seed: Build().TestRootDirectory(testRoot),
                    func: (acc, specTuple) => acc.AddSpec(specTuple.SpecPath, specTuple.SpecContent));
            var config = (CommandLineConfiguration)specBuilder.PersistSpecsAndGetConfiguration();
            config.Cache.AllowFetchingCachedGraphFromContentCache = false;
            return config;
        }

        private bool RunAndAssertGraphCacheMiss(ICommandLineConfiguration config, AppDeployment appDeployment, bool rememberAllChangedTrackedInputs = false)
        {
            using (var hostController = RunEngineAndGetFrontEndHostController(config, appDeployment, null, rememberAllChangedTrackedInputs))
            {
                AssertLogged(LogEventId.EndSerializingPipGraph);
                return hostController.Workspace != null;
            }
        }

        private void RunAndAssertGraphCacheHit(ICommandLineConfiguration config, AppDeployment appDeployment, bool rememberAllChangedTrackedInputs = false)
        {
            using (var hostController = RunEngineAndGetFrontEndHostController(config, appDeployment, null, rememberAllChangedTrackedInputs))
            {
                AssertNotLogged(LogEventId.EndSerializingPipGraph);
                XAssert.IsNull(hostController.Workspace);
            }
        }

        private void AssertLogged(LogEventId eventId) => AssertInformationalEventLogged(eventId, count: 1);

        private void AssertNotLogged(LogEventId eventId) => AssertInformationalEventLogged(eventId, count: 0);

        private bool ContainsFileName(IEnumerable<string> fullPaths, string fileName)
        {
            var set = new HashSet<string>(fullPaths.Select(p => Path.GetFileName(p)), StringComparer.OrdinalIgnoreCase);
            return set.Contains(fileName);
        }
    }

    /// <summary>
    /// A simple build definition consisting of a SpecPath -> SpecContent dictionary.
    ///
    /// SpecContent is represented with <see cref="StringBuilder"/> so that it can be modified over time
    /// (which is useful for testing graph caching build over build)
    /// </summary>
    public sealed class BuildDefinition
    {
        private readonly Dictionary<string, StringBuilder> m_specs = new Dictionary<string, StringBuilder>();

        public IEnumerable<(string SpecPath, string SpecContent)> Specs
            => m_specs.Select(kvp => (SpecPath: kvp.Key, SpecContent: kvp.Value.ToString()));

        public void AppendToSpecContent(string specPath, string contentToAppend)
        {
            bool found = m_specs.TryGetValue(specPath, out StringBuilder specContent);
            if (!found)
            {
                var knownSpecs = string.Join(", ", m_specs.Keys);
                XAssert.Fail($"Spec '{specPath}' not found; known specs are: {knownSpecs}");
            }

            specContent.Append(contentToAppend);
        }

        public string this[string key]
        {
            get
            {
                return m_specs[key].ToString();
            }
            set
            {
                m_specs[key] = new StringBuilder(value);
            }
        }
    }
}
