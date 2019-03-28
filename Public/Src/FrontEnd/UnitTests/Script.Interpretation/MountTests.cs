// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class MountTests : DsTest
    {
        public MountTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void GetMountReturnsRegisteredMount()
        {
            string config = @"
config({
  mounts: [
     { name: PathAtom.create(""foo""), path: p`path` },
  ]
});";
            string spec = @"
export const r = Context.getMount(""foo"").path;
export const expectedPath = p`path`;";

            var result = Build()
                .LegacyConfiguration(config)
                .AddSpec("build.dsc", spec)
                .EvaluateExpressionsWithNoErrors("r", "expectedPath");

            Assert.Equal(result["expectedPath"], result["r"]);
        }

        [Fact]
        public void GetMountWorksFineWithStringAsMountname()
        {
            // Configuration conversion is more permissive (i.e., less safe),
            // and allows to use strings instead of PathAtom.
            // This test proofs that this is our current behavior.
            string config = @"
config({
  mounts: [
     { name: a`foo`, path: p`path` },
  ]
});";
            string spec = @"
export const r = Context.getMount(""foo"").path;
export const expectedPath = p`path`;";

            var result = Build()
                .LegacyConfiguration(config)
                .AddSpec("build.dsc", spec)
                .EvaluateExpressionsWithNoErrors("r", "expectedPath");

            Assert.Equal(result["expectedPath"], result["r"]);
        }

        [Fact]
        public void GetMountWorksFineWithPathAtomAsMountname()
        {
            string config = @"
config({
  mounts: [
     { name: a`foo`, path: p`path` },
  ]
});";
            string spec = @"
export const r = Context.getMount(""foo"").path;
export const expectedPath = p`path`;";

            var result = Build()
                .LegacyConfiguration(config)
                .AddSpec("build.dsc", spec)
                .EvaluateExpressionsWithNoErrors("r", "expectedPath");

            Assert.Equal(result["expectedPath"], result["r"]);
        }

        [Fact]
        public void CaseMismatchMountShouldLeadToNonFatalError()
        {
            string config = @"
config({
  mounts: [
     { name: PathAtom.create(""Foo""), path: p`path` },
  ]
});";
            string spec = @"
export const r = Context.getMount(""foo"").path;";

            var result = Build()
                .LegacyConfiguration(config)
                .AddSpec("build.dsc", spec)
                .EvaluateWithFirstError();

            Assert.Equal(LogEventId.GetMountNameCaseMisMatch, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void UnknownMountShouldLeadToNonFatalError()
        {
            string config = @"
config({
  mounts: [
  ]
});";
            string spec = @"
export const r = Context.getMount(""unknownMount"").path;";

            var result = Build()
                .LegacyConfiguration(config)
                .AddSpec("build.dsc", spec)
                .EvaluateWithFirstError();

            Assert.Equal(LogEventId.GetMountNameNotFound, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void EmptyMountShouldLeadToNonFatalError()
        {
            string config = @"
config({
  mounts: [
  ]
});";
            string spec = @"
export const r = Context.getMount("""").path;";

            var result = Build()
                .LegacyConfiguration(config)
                .AddSpec("build.dsc", spec)
                .EvaluateWithFirstError();

            Assert.Equal(LogEventId.GetMountNameNullOrEmpty, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void HasMountReturnsTrueForKnownMountAndFalseForUnknown()
        {
            string config = @"
config({
  mounts: [
     { name: a`foo`, path: p`path` },
  ]
});";
            string spec = @"
export const shouldBeTrue = Context.hasMount(""foo"");
export const shouldBeFalse = Context.hasMount(""foo1"");";

            var result = Build()
                .LegacyConfiguration(config)
                .AddSpec("build.dsc", spec)
                .EvaluateExpressionsWithNoErrors("shouldBeTrue", "shouldBeFalse");

            Assert.Equal(true, result["shouldBeTrue"]);
            Assert.Equal(false, result["shouldBeFalse"]);
        }
    }
}
