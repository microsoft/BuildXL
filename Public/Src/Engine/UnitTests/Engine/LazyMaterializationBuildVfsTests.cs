// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Engine;
using BuildXL.Native.IO;
using BuildXL.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

using static BuildXL.Tracing.LogEventId;

namespace Test.BuildXL.Engine
{
    [Trait("Category", "LazyMaterializationBuildTests")]
    [TestClassIfSupported(TestRequirements.WindowsProjFs)]
    [Feature(Features.LazyOutputMaterialization)]
    public class LazyMaterializationBuildVfsTests : LazyMaterializationBuildTests, ILogMessageObserver
    {
        private CacheInitializer m_cacheInitializer;

        // NOTE: Uncomment when debugging and looking at test logs
        // to remove some superfluous logging. Some tests fail because
        // they expect some diagnostic messages to be logged so this needs
        // to remain commented unless debugging.
        //protected override bool CaptureAllDiagnosticMessages => false;

        protected override EventMask ListenerEventMask { get; } = new EventMask(null, disabledEvents: new[]
        {
            (int)Status
        });

        public LazyMaterializationBuildVfsTests(ITestOutputHelper output)
            : base(output)
        {
            Logger.Log.AddObserver(this);
            EtwOnlyTextLogger.EnableGlobalEtwLogging(LoggingContext);
            Configuration.Cache.VfsCasRoot = Combine(Configuration.Layout.CacheDirectory, "vfs");

            try
            {
                // These tests validate the right ACLs are set on particular files. We need the real cache for that.
                m_cacheInitializer = GetRealCacheInitializerForTests();
                ConfigureCache(m_cacheInitializer);
            }
            catch (Exception ex)
            {
                TestOutput.WriteLine($"Could not initialize cache initializer: {ex.ToStringDemystified()}");
            }
        }

        // Disable this test because it creates its own cache setup
        public override Task TestHintPropagation()
        {
            return Task.CompletedTask;
        }

        public void OnMessage(Diagnostic diagnostic)
        {
            if (diagnostic.ErrorCode == (int)SharedLogEventId.TextLogEtwOnly)
            {
                TestOutput.WriteLine(diagnostic.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            Logger.Log.RemoveObserver(this);

            if (m_cacheInitializer != null)
            {
                var closeResult = m_cacheInitializer.Close();
                if (!closeResult.Succeeded)
                {
                    throw new BuildXLException("Unable to close the cache session: " + closeResult.Failure.DescribeIncludingInnerFailures());
                }
                m_cacheInitializer.Dispose();
                m_cacheInitializer = null;
            }

            base.Dispose(disposing);
        }
    }
}