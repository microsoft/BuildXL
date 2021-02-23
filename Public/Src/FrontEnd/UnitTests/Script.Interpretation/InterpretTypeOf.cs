// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretTypeOf : DsTest
    {
        /// <summary>
        /// Some of the expressions below use simplified type annotations that don't match with the actual Transformer SDK
        /// </summary>
        protected override bool DisableInBoxSDKResolver => true;

        public InterpretTypeOf(ITestOutputHelper output)
            : base(output)
        { }

        // Type casting doesn't change runtime behavior, so those tests are almost syntactical
        [Theory]
        [InlineData("undefined", "undefined")]
        [InlineData("1", "number")]
        [InlineData("'a'", "string")]
        [InlineData("true", "boolean")]
        [InlineData("[]", "array")]
        [InlineData("{}", "object")]
        [InlineData("Set.empty<string>()", "Set")]
        [InlineData("Map.empty<string, string>()", "Map")]
        [InlineData("f`a`", "File")]
        [InlineData("d`a`", "Directory")]
        [InlineData("p`a`", "Path")]
        [InlineData("r`a`", "RelativePath")]
        [InlineData("a`a`", "PathAtom")]
        [InlineData("Transformer.sealDirectory(d`.`, [])", "FullStaticContentDirectory")]
        [InlineData("Transformer.sealPartialDirectory(d`.`, [])", "PartialStaticContentDirectory")]
        [InlineData("Transformer.sealSourceDirectory(d`.`, Transformer.SealSourceDirectoryOption.allDirectories)", "SourceAllDirectory")]
        [InlineData("Transformer.sealSourceDirectory(d`.`, Transformer.SealSourceDirectoryOption.topDirectoryOnly)", "SourceTopDirectory")]
        [InlineData("Transformer.composeSharedOpaqueDirectories(d`.`, [], {kind:\"Include\", regex:\".*\"})", "SharedOpaqueDirectory")]
        [InlineData("Transformer.filterSharedOpaqueDirectory(d`.`, {kind:\"Include\", regex:\".*\"})", "SharedOpaqueDirectory")]
		[InlineData("Transformer.getSharedOpaqueSubDirectory(Transformer.composeSharedOpaqueDirectories(d`.`, []), p`foo`, {kind:\"Include\", regex:\".*\"})", "SharedOpaqueDirectory")]
        [InlineData("Transformer.execute({tool: {exe: f`myExe`}, arguments:[], workingDirectory: d`.`, outputs: [{kind: 'exclusive', directory: d`Out`}]}).getOutputDirectory(d`Out`)", "ExclusiveOpaqueDirectory")]
        public void InterpretTypeOfTest(string expr, string typeName)
        {
            // Currently, DScript doesn't support initializers that are not numbers.
            string spec = $@"
import {{Transformer}} from 'Sdk.Transformers';

namespace M {{
    export const result = typeof({expr}) === ""{typeName}"";
}}";

            var result = EvaluateExpressionsWithNoErrors(spec, "M.result");

            Assert.Equal(true, result["M.result"]);
        }
    }
}
