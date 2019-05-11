// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BuildXL;
using BuildXL.Cache;
using BuildXL.Engine;
using BuildXL.Engine.Cache;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using AssemblyHelper = BuildXL.Utilities.AssemblyHelper;

namespace Test.DScript.Ast
{
    /// <summary>
    /// Base class for tests that need to test caching behavior.
    /// </summary>
    public abstract class DsTestWithCacheBase : DsTest
    {
        private FrontEndContext m_frontEndContext;

        protected TestCache TestCache { get; }

        protected override FrontEndContext FrontEndContext => m_frontEndContext ?? base.FrontEndContext;

        public DsTestWithCacheBase(ITestOutputHelper output, bool usePassThroughFileSystem = false) : base(output, usePassThroughFileSystem)
        {
            RegisterEventSource(global::BuildXL.FrontEnd.Script.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);

            TestCache = new TestCache();
        }

        protected static AppDeployment CreateAppDeployment(TempFileStorage tempFiles)
        {
            string manifestPath = tempFiles.GetFileName(AppDeployment.DeploymentManifestFileName);
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(manifestPath), AppDeployment.DeploymentManifestFileName),
                AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
            AppDeployment appDeployment = AppDeployment.ReadDeploymentManifest(Path.GetDirectoryName(manifestPath), AppDeployment.DeploymentManifestFileName, skipManifestCheckTestHook: true);
            return appDeployment;
        }

        protected IList<AbsolutePath> RunAndRetrieveSpecs(ICommandLineConfiguration config, AppDeployment appDeployment, bool rememberAllChangedTrackedInputs = false)
        {
            IList<AbsolutePath> specs;
            using (var controller = RunEngineAndGetFrontEndHostController(config, appDeployment, null, rememberAllChangedTrackedInputs))
            {
                var workspace = controller.Workspace;
                specs = workspace.GetAllSpecFiles().Where(spec => !workspace.PreludeModule.Specs.ContainsKey(spec)).ToList();
            }

            Assert.True(specs != null);
            return specs;
        }

        protected virtual BuildXLEngine CreateEngine(ICommandLineConfiguration config,
            AppDeployment appDeployment,
            string testRootDirectory,
            bool rememberAllChangedTrackedInputs,
            Action<EngineTestHooksData> verifyEngineTestHooksData = null)
        {

            var engineContext = EngineContext.CreateNew(CancellationToken.None, PathTable, FileSystem);

            var factory = FrontEndControllerFactory.Create(
                FrontEndMode.NormalMode,
                LoggingContext,
                config,
                // Set the timeout to a large number to avoid useless performance collections in tests.
                new PerformanceCollector(TimeSpan.FromHours(1)),
                collectMemoryAsSoonAsPossible: false);

            var engine = BuildXLEngine.Create(
                LoggingContext,
                engineContext,
                config,
                factory,
                rememberAllChangedTrackedInputs: rememberAllChangedTrackedInputs
                );

            return engine;
        }

        protected virtual BuildXLEngineResult CreateAndRunEngine(
            ICommandLineConfiguration config,
            AppDeployment appDeployment,
            string testRootDirectory,
            bool rememberAllChangedTrackedInputs,
            out BuildXLEngine engine,
            Action<EngineTestHooksData> verifyEngineTestHooksData = null,
            TestCache testCache = null)
        {
            testCache = testCache ?? TestCache;

            using (EngineTestHooksData testHooks =  new EngineTestHooksData
                                                    {
                                                       AppDeployment = appDeployment,
                                                       CacheFactory = () => new EngineCache(
                                                            testCache.GetArtifacts(),
                                                            testCache.Fingerprints)
                                                    })
            {
                engine = CreateEngine(config, appDeployment, testRootDirectory, rememberAllChangedTrackedInputs, verifyEngineTestHooksData);

                // Ignore DX222 for csc.exe being outside of src directory
                IgnoreWarnings();

                engine.TestHooks = testHooks;

                var result = engine.RunForFrontEndTests(LoggingContext);
                return result;
            }
        }

        protected FrontEndHostController RunEngineAndGetFrontEndHostController(
            ICommandLineConfiguration config, 
            AppDeployment appDeployment, 
            string testRootDirectory, 
            bool rememberAllChangedTrackedInputs,
            Action<EngineTestHooksData> verifyEngineTestHooksData = null)
        {
            var result = CreateAndRunEngine(
                config,
                appDeployment,
                testRootDirectory,
                rememberAllChangedTrackedInputs,
                out var engine,
                verifyEngineTestHooksData);

            m_frontEndContext = engine.Context.ToFrontEndContext(LoggingContext);
            verifyEngineTestHooksData?.Invoke(engine.TestHooks);

            // If the engine reloaded and created a new pipGraph we need to udpate the Test file system with that new pipgraph as well
            if (!engine.TestHooks.GraphReuseResult.IsNoReuse)
            {
                FileSystem = (IMutableFileSystem)engine.Context.FileSystem;
            }

            if (!result.IsSuccess)
            {
                Assert.True(false, $"Failed to run the engine. See '{testRootDirectory ?? TestOutputDirectory}' for more details.");
            }

            return (FrontEndHostController)engine.FrontEndController;
        }
    }
}
