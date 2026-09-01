// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    public sealed class GraphSemistableFingerprintTests : PipTestBase
    {
        public GraphSemistableFingerprintTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void ConfiguredFingerprintOverridesComputedFingerprint()
        {
            const string FingerprintText = "my human-readable graph identifier";
            var configuration = new ConfigurationImpl();
            configuration.Schedule.GraphSemistableFingerprint = FingerprintText;
            ResetPipGraphBuilder(configuration);

            var graph = PipGraphBuilder.Build();

            XAssert.AreEqual(new ContentFingerprint(FingerprintUtilities.Hash(FingerprintText)), graph.SemistableFingerprint);
        }
    }
}
