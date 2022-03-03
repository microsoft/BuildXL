// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Engine;
using BuildXL.Storage.ChangeJournalService.Protocol;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine
{
    public class GraphInputArtifactChangesTest : XunitBuildXLTest
    {
        public GraphInputArtifactChangesTest(ITestOutputHelper output) : base(output) { }

        private CounterCollection<ReadJournalCounter> EmptyStats { get; } = new CounterCollection<ReadJournalCounter>();

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ChangesToGvfsProjectionsInvalidatePossiblyChangedPaths(bool changeGvfsProjection)
        {
            var gvfsProjections = new[]
            {
                X("/c/.gvfs/GVFS_projection"),
                X("/d/.gvfs/GVFS_projection")
            };
            var otherFile = X("/c/whatever");

            var graphChanges = new GraphInputArtifactChanges(LoggingContext, gvfsProjections);
            graphChanges.OnInit();
            if (changeGvfsProjection)
            {
                graphChanges.OnNext(new ChangedPathInfo(gvfsProjections[0], PathChanges.Removed));
            }
            graphChanges.OnNext(new ChangedPathInfo(otherFile, PathChanges.DataOrMetadataChanged));
            graphChanges.OnCompleted(ScanningJournalResult.Success(EmptyStats));

            if (changeGvfsProjection)
            {
                XAssert.IsNull(graphChanges.ChangedDirs);
                XAssert.IsNull(graphChanges.PossiblyChangedPaths);
            }
            else
            {
                XAssert.SetEqual(new string[0], graphChanges.ChangedDirs);
                XAssert.SetEqual(new[] { otherFile }, graphChanges.PossiblyChangedPaths);
            }
        }

        [Fact]
        public void EnvironmentVariableGraphInputIsCaseSensitive()
        {
            string varName = "Foo";
            string lowercaseVarValue = "bar";
            string uppercaseVarValue = lowercaseVarValue.ToUpperInvariant();
            XAssert.AreEqual(
                new EnvironmentVariableInput(varName, lowercaseVarValue),
                new EnvironmentVariableInput(varName, lowercaseVarValue));
            XAssert.AreNotEqual(
                new EnvironmentVariableInput(varName, lowercaseVarValue),
                new EnvironmentVariableInput(varName, uppercaseVarValue));
        }
    }
}
