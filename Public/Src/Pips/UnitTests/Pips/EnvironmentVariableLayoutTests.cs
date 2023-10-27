// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Pips
{
    public sealed class EnvironmentVariableLayoutTests : XunitBuildXLTest
    {
        private readonly ITestOutputHelper _output;
        public EnvironmentVariableLayoutTests(ITestOutputHelper output)
            : base(output)
        {
            _output = output;
        }

        [Fact]
        public void PipDataStoredCorrectly()
        {
            // EnvironmentVariable flattens PipData structure, so its important to make sure
            // that if PipData structure changes EnvironmentVariable is changed as well.
            // This test ensures the consistency.
            var pipData = PipData.Invalid;
            var envVar = new EnvironmentVariable(StringId.UnsafeCreateFrom(42), pipData, isPassThrough: true);

            // Don't use XUnit Equals method for pip data comparison because it will treat pip data as a collection.
            // Note that pip data implements IEnumerable, and XUnit will use collection comparison logic. This will cause
            // System.NullReferenceException because invalid pip data may have some null entries.
            XAssert.SimpleEqual(pipData, envVar.Value);

            var pipDataEntry = new PipDataEntry(PipDataFragmentEscaping.NoEscaping, PipDataEntryType.NestedDataHeader, 42);
            pipData = PipData.CreateInternal(
                pipDataEntry,
                PipDataEntryList.FromEntries(new[] {pipDataEntry}),
                StringId.UnsafeCreateFrom(1));
            envVar = new EnvironmentVariable(StringId.UnsafeCreateFrom(42), pipData, isPassThrough: false);
            XAssert.SimpleEqual(pipData, envVar.Value);
        }

        [Fact]
        public void EnvironmentVariableSizeIs32()
        {
            // EnvironmentVariable structs are stored for every pip and reducing the size of such structs
            // reasonably reduces the peak memory consumption.
            
            // Flattening the layout saves 30% of space compared to the naive old version.
            var layout = ObjectLayoutInspector.TypeLayout.GetLayout<EnvironmentVariable>();
            _output.WriteLine(layout.ToString());

            Assert.Equal(32, layout.Size);
            var oldLayout = ObjectLayoutInspector.TypeLayout.GetLayout<OldEnvironmentVariable>();
            _output.WriteLine(oldLayout.ToString());
            Assert.True(layout.Size < oldLayout.Size);
        }

        // Using StringIdStable and not StringId, because StringId layout is different for debug builds.
        private record struct OldEnvironmentVariable(StringIdStable Name, PipData Value, bool IsPassThrough);

        private record struct StringIdStable(int Value);
    }
}
