// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientDebugTests : DsTest
    {
        public AmbientDebugTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [MemberData(nameof(Test1Data))]
        public void TestDebugDumpArgs(string dumpArgs, string expandPathsStr)
        {
            var result = EvaluateSpec(
                $@"
namespace M {{
  export const x = Debug.dumpArgs([{string.Join(", ", dumpArgs)}]);
  export const y = Debug.expandPaths(""{expandPathsStr}"");
  export const eq = x === y;
}}
", new[] {"M.x", "M.y", "M.eq"});

            result.ExpectNoError();
            Assert.Equal(result.Values[0], result.Values[1]);
            Assert.True((bool) result.Values[2]);
        }

        public static IEnumerable<object[]> Test1Data =>
            new object[][]
            {
                new object[]
                {
                    @"{name: ""/f:"", value: 123}, 
                          {name: ""/in:"", value: {path: p`hi.txt`, kind: ArtifactKind.input}},
                          {name: true ? ""/yes"" : undefined, value: undefined}, 
                          {name: false ? ""/no"" : undefined, value: undefined}",
                    @"/f:123 /in:{hi.txt} /yes"
                },
                new object[]
                {
                    @"{name: """", value: {path: p`hi.txt`, kind: ArtifactKind.input}}",
                    @"{hi.txt}"
                },
                new object[]
                {
                    @"{name: """", value: {values: [{path: p`hi.txt`, kind: ArtifactKind.input}, {path: p`bye.txt`, kind: ArtifactKind.input}], separator: "";""}}",
                    @"{hi.txt};{bye.txt}"
                },
                new object[]
                {
                    @"{name: """", value: {values: [{path: p`hi.txt`, kind: ArtifactKind.input}, {path: p`bye.txt`, kind: ArtifactKind.input}], separator: """"}}",
                    @"{hi.txt}{bye.txt}"
                },
                new object[]
                {
                    @"{name: """", value: {path: p`hi.txt`, kind: ArtifactKind.input}},
                          {name: """", value: {path: p`bye.txt`, kind: ArtifactKind.input}}",
                    @"{hi.txt} {bye.txt}"
                },
            };
    }
}
