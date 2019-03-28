// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretTypeOf : DsTest
    {
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
        [InlineData("Transformer.composeSharedOpaqueDirectories(d`.`, [])", "SharedOpaqueDirectory")]
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
