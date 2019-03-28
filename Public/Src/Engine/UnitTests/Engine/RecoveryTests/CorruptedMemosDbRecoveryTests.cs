// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Engine;
using BuildXL.Engine.Recovery;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using System;
using System.IO;
using System.Linq;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine.RecoveryTests
{
    /// <summary>
    /// Failure recovery tests.
    /// </summary>
    public sealed class CorruptedMemosDbRecoveryTests : TemporaryStorageTestBase
    {
        public CorruptedMemosDbRecoveryTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void TestCorruptedMemosDbRecovery()
        {
            CorruptedMemosDbRecovery corruptedMemosDbRecovery = MarkFailureAndTryRecover();
            XAssert.AreEqual(1, corruptedMemosDbRecovery.GetAllCorruptedMemosDb().Count());
        }

        [Fact]
        public void TestCorruptedMemosDbRecoveryMultipleTimes()
        {

            CorruptedMemosDbRecovery corruptedMemosDbRecovery = null;

            for (int i = 0; i < 10; ++i)
            {
                corruptedMemosDbRecovery = MarkFailureAndTryRecover();
            }

            XAssert.AreEqual(5, corruptedMemosDbRecovery.GetAllCorruptedMemosDb().Count());
        }

        private CorruptedMemosDbRecovery MarkFailureAndTryRecover()
        {
            BuildXLContext context;
            IConfiguration configuration;
            CreateContextAndConfiguration(out context, out configuration);

            WriteFakeMemosDb(context, configuration);

            FailureRecoveryAggregator recovery = FailureRecoveryFactory.Create(LoggingContext, context.PathTable, configuration);
            bool markFailure = recovery.TryMarkFailure(new BuildXLException(string.Empty), ExceptionRootCause.CorruptedCache);
            XAssert.IsTrue(markFailure);

            var corruptedMemosDbRecovery = new CorruptedMemosDbRecovery(context.PathTable, configuration);
            XAssert.IsTrue(corruptedMemosDbRecovery.ShouldRecover());

            CreateContextAndConfiguration(out context, out configuration);
            recovery = FailureRecoveryFactory.Create(LoggingContext, context.PathTable, configuration);

            bool tryRecover = recovery.TryRecoverIfNeeded();
            XAssert.IsTrue(tryRecover);

            corruptedMemosDbRecovery = new CorruptedMemosDbRecovery(context.PathTable, configuration);
            XAssert.IsFalse(File.Exists(GetMemosDbPath(context, configuration)));

            return corruptedMemosDbRecovery;
        }

        private void CreateContextAndConfiguration(out BuildXLContext context, out IConfiguration configuration)
        {
            context = BuildXLContext.CreateInstanceForTesting();

            var cmdLineConfiguration = new CommandLineConfiguration();
            cmdLineConfiguration.Engine.TrackBuildsInUserFolder = false;
            cmdLineConfiguration.Startup.ConfigFile = AbsolutePath.Create(context.PathTable, Path.Combine(TemporaryDirectory, "config.dc"));
            BuildXLEngine.PopulateLoggingAndLayoutConfiguration(cmdLineConfiguration, context.PathTable, bxlExeLocation: null, inTestMode: true);

            var cacheConfigPath = Path.Combine(TemporaryDirectory, "CacheConfig.json");

            const string CacheConfig = @"
{
   ""Assembly"":""BuildXL.Cache.MemoizationStoreAdapter"",
   ""Type"":""BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory"",
   ""CacheRootPath"":""[BuildXLSelectedRootPath]"",
   ""CacheLogPath"":""[BuildXLSelectedLogPath]""
}";

            File.WriteAllText(cacheConfigPath, CacheConfig);
            cmdLineConfiguration.Cache.CacheConfigFile = AbsolutePath.Create(context.PathTable, cacheConfigPath);
            configuration = cmdLineConfiguration;
        }

        private void WriteFakeMemosDb(BuildXLContext context, IConfiguration configuration)
        {
            var memoDbPath = GetMemosDbPath(context, configuration);
            Directory.CreateDirectory(Path.GetDirectoryName(memoDbPath));
            File.WriteAllText(memoDbPath, Guid.NewGuid().ToString());
        }

        private string GetMemosDbPath(BuildXLContext context, IConfiguration configuration)
        {
            var cacheDirectory = configuration.Layout.CacheDirectory.ToString(context.PathTable);
            return Path.Combine(cacheDirectory, "Memos.db");
        }
    }
}
