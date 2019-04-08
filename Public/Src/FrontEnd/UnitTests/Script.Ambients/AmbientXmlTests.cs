// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using BuildXL.Pips.Operations;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation.Ambients
{
    public class AmbientXmlTests : DsTest
    {
        public AmbientXmlTests(ITestOutputHelper output)
                   : base(output)
        {
        }

        [Fact]
        public void TestWriteApi()
        {
            var spec = @"
namespace M {
    const f = _PreludeAmbientHack_Xml.write(p`out.txt`, { kind: 'document', nodes: [{kind: 'element', name: {local:'elem'}, nodes: []}]});

    const r1: PathAtom = f.extension;
    const r2: PathAtom = a`.txt`;
    export const result = (r1 === r2);
}";
            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionWithNoErrors("M.result");
            Assert.Equal(true, result);
        }

        [Theory]
        [InlineData(@"<root />")]
        [InlineData(@"<root attr1=""value1"" attr2=""value2"" />")]
        [InlineData(@"<root>test</root>")]
        [InlineData(@"<!--beforeroot-->
<root>
  <c>
    <!--inc-->
  </c>
  <!--betweenCAndD-->
  <d>
    <!--beforeDText-->text</d>
</root>
<!--afterRoot-->")]
        [InlineData(@"<root><![CDATA[

test

]]></root>")]
        [InlineData(@"<root>
  <child1 />
  <child2 />
</root>")]
        [InlineData(@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<root>
</root>")]
        public void TestReadThenWrte(string xml)
        {
            var spec = @"
namespace M {
    const result = _PreludeAmbientHack_Xml.read(f`in.xml`);

}";
            var result = Build()
                .AddFile("in.xml", xml)
                .AddSpec(spec)
                .EvaluateExpressionWithNoErrors("M.result");

            var objectLiteral = result as ObjectLiteral;
            Assert.NotNull(objectLiteral);

            var renderer = new PipFragmentRenderer(
                absPath => {
                    var path = absPath.ToString(PathTable, PathFormat.Script);
                    var testRoot = TestRoot.Replace('\\', '/') + "/TestSln/src/";
                    if (path.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return "##" + path.Substring(testRoot.Length) + "##";
                    }
                    return "##" + path + "##";
                },
                StringTable,
                x => "@@" + x + "@@");
            var pipData = AmbientXml.CreatePipData(StringTable, objectLiteral, new PipDataBuilder(StringTable));

            var canonicalPipData = pipData.ToString(renderer).Replace("\r\n", "\n").Replace("/", "\\\\");
            var canonicalExpected = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + xml.Replace("\r\n", "\n").Replace("/", "\\\\");

            // skip read-write test if xml contains DOCTYPE because our writer doesn't generate <!DOCTYPE>
            if (!xml.ToUpperInvariant().Contains("DOCTYPE"))
            {
                Assert.Equal(canonicalExpected, canonicalPipData);
            }
        }
    }
}
