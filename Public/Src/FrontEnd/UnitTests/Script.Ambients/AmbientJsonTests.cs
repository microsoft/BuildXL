// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Core;
using BuildXL.FrontEnd.Script.Ambients.Exceptions;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using BuildXL.Pips.Operations;
using Xunit;

namespace Test.DScript.Ast.Interpretation.Ambients
{
    public class AmbientJsonTests : DsTest
    {
        public AmbientJsonTests(ITestOutputHelper output)
                   : base(output)
        {
        }

        [Fact]
        public void TestApi()
        {
            var spec = @"
namespace M {
    const f = _PreludeAmbientHack_Json.write(p`out.txt`, { key: ""Awesome!""});

    const r1: PathAtom = f.extension;
    const r2: PathAtom = a`.txt`;
    export const result = (r1 === r2);
}";
            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionWithNoErrors("M.result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void EmptyObject()
        {
            var spec = @"{
}";
            ComparePipData(@"{}", spec);
        }

        [Fact]
        public void BasicTypes()
        {
            var spec = @"{
    s1: ""value1"",
    s2: 'value2',

    b1: true,

    b2: false,

    n1: 0,
    n2: 99,
    n3: -99,

    a1: a`atom`,

    r1: r`rel1`,
    r2: r`rel2/rel3`,
}";
            ComparePipData(@"{
  's1': 'value1',
  's2': 'value2',
  'b1': true,
  'b2': false,
  'n1': 0,
  'n2': 99,
  'n3': -99,
  'a1': 'atom',
  'r1': 'rel1',
  'r2': 'rel2\\rel3'
}", spec);
        }

        [Fact]
        public void Arrays()
        {
            var spec = @"{
    arr1: [
        1, 2, 3
    ],
}";
            ComparePipData(@"{
  'arr1': [
    1,
    2,
    3
  ]
}", spec);
        }

        [Fact]
        public void DynamicFieldsRecursiveAtRoot()
        {
            var spec = @"{
    __dynamicFields: [
        {name: 'n1', value: 1},
        {name: 'n2', value: 'v2'},
        {name: 'n3', value: {}},
        {name: 'n4', value: {
            staticField: 1,
            staticFieldWithDynObj: {
                __dynamicFields: [
                    {name: 'nn1', value: 'vv1'},
                ],
            },
        }},
    ]
}";
            ComparePipData(@"{
  'n1': 1,
  'n2': 'v2',
  'n3': {},
  'n4': {
    'staticField': 1,
    'staticFieldWithDynObj': {
      'nn1': 'vv1'
    }
  }
}", spec);
        }

        [Fact]
        public void MixedStaticAndDynamicFields()
        {
            var spec = @"{
    static1: 1,
    __dynamicFields: [
        {name: 'dynamic2', value: 2},
    ],
    static3: 3,
}";
            ComparePipData(@"{
  'static1': 1,
  'dynamic2': 2,
  'static3': 3
}", spec);
        }

        [Fact]
        public void NoFields()
        {
            var spec = @"{
    f1: {
        __dynamicFields: [],
    },
    f2: {
        __dynamicFields: undefined,
    }
}";
            ComparePipData(@"{
  'f1': {},
  'f2': {}
}", spec);
        }


        [Fact]
        public void NoValues()
        {
            var spec = @"{
    __dynamicFields: [
        {name: 'f1'},
        {name: 'f2', value: undefined},
    ],
}";
            ComparePipData(@"{
  'f1': null,
  'f2': null
}", spec);
        }

        [Fact]
        public void FieldsNotArray()
        {
            var spec = @"{
    __dynamicFields: 1,
}";

            Assert.Throws<JsonUnsuportedDynamicFieldsForSerializationException>(() => ComparePipData("", spec));
        }

        [Fact]
        public void KeyNotString()
        {
            var spec = @"{
    __dynamicFields: [
        {name: 1},
    ],
}";

            Assert.Throws<JsonUnsuportedDynamicFieldsForSerializationException>(() => ComparePipData("", spec));
        }
        [Fact]
        public void MissingNameField()
        {
            var spec = @"{
    __dynamicFields: [
        {name2: 1},
    ],
}";

            Assert.Throws<JsonUnsuportedDynamicFieldsForSerializationException>(() => ComparePipData("", spec));
        }

        [Fact]
        public void Sets()
        {
            var spec = @"{
    set: Set.empty<number>().add(2).add(1).add(3),
}";
            ComparePipData(@"{
  'set': [
    2,
    1,
    3
  ]
}", spec);
        }

        [Fact]
        public void Map()
        {
            var spec = @"{
    set: Map.empty<string, number>().add('b', 2).add('a', 1).add('c', 3),
}";
            ComparePipData(@"{
  'set': [
    {
      'key': 'b',
      'value': 2
    },
    {
      'key': 'a',
      'value': 1
    },
    {
      'key': 'c',
      'value': 3
    }
  ]
}", spec);
        }

        [Fact]
        public void ArraysOfPaths()
        {
            var spec = @"{
    arr1: [
        f`f1.txt`, f`f2.txt`, f`f3/f3.txt`,
    ],
}";
            ComparePipData(@"{
  'arr1': [
    '##f1.txt##',
    '##f2.txt##',
    '##f3/f3.txt##'
  ]
}", spec);
        }

        [Fact]
        public void Path()
        {
            var spec = @"{
    p: p`path.txt`,
}";
            ComparePipData(@"{
  'p': '##path.txt##'
}", spec);
        }

        [Fact]
        public void File()
        {
            var spec = @"{
    f: f`file.txt`,
}";
            ComparePipData(@"{
  'f': '##file.txt##'
}", spec);
        }

        [Fact]
        public void Directory()
        {
            var spec = @"{
    d: d`a/b/c`,
}";
            ComparePipData(@"{
  'd': '##a/b/c##'
}", spec);
        }

        [Fact]
        public void NestedObjects()
        {
            var spec = @"{
    o1: {},
    o2: {
        o3: {
            key: 42,
        }
    },
}";
            ComparePipData(@"{
  'o1': {},
  'o2': {
    'o3': {
      'key': 42
    }
  }
}", spec);
        }

        [Fact]
        public void NestedObjectsWithDifferentQuoting()
        {
            var spec = @"{
    o1: {},
    o2: {
        o3: {
            key: 42,
        }
    },
}";
            ComparePipData(@"{
  ""o1"": {},
  ""o2"": {
    ""o3"": {
      ""key"": 42
    }
  }
}", spec, "\"");
        }

        [Theory]
        [InlineData("none")]
        [InlineData("backSlashes")]
        [InlineData("escapedBackSlashes")]
        [InlineData("forwardSlashes")]
        public void TestAdditionaLJsonOptions(string option)
        {
            var spec = @"
const options : Object = {
    pathRenderingOption: """ + option + @"""
};
";
            var result = Build().AddSpec(spec).EvaluateExpressionWithNoErrors("options") as ObjectLiteral;
            Assert.True(result != null, "Expected to receive an ObjectLiteral from evaluation the data");

            var convertedResult = AmbientJson.GetAdditionalOptions(FrontEndContext, result);
            Assert.True(typeof(AmbientJson.AdditionalJsonOptions).Equals(convertedResult.GetType()));

            var writeFileOption = AmbientJson.GetWriteFileOption(convertedResult);
            switch(option)
            {
                case "none":
                    Assert.True(writeFileOption.PathRenderingOption == WriteFile.PathRenderingOption.None);
                    break;
                case "backSlashes":
                    Assert.True(writeFileOption.PathRenderingOption == WriteFile.PathRenderingOption.BackSlashes);
                    break;
                case "escapedBackSlashes":
                    Assert.True(writeFileOption.PathRenderingOption == WriteFile.PathRenderingOption.EscapedBackSlashes);
                    break;
                case "forwardSlashes":
                    Assert.True(writeFileOption.PathRenderingOption == WriteFile.PathRenderingOption.ForwardSlashes);
                    break;
            }
        }

        private void ComparePipData(string expected, string objectToSerialize, string quoteChar = "\'")
        {
            var result = Build().AddSpec("const obj = " + objectToSerialize + ";").EvaluateExpressionWithNoErrors("obj") as ObjectLiteral;
            Assert.True(result != null, "Expected to receive an ObjectLiteral from evaluation the data");

            var renderer = new PipFragmentRenderer(
                absPath => {
                    var path = absPath.ToString(PathTable, PathFormat.Script);
                    var testRoot = TestRoot.Replace('\\', '/') + "/";
                    if (path.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return "##" + path.Substring(testRoot.Length) + "##";
                    }
                    return "##" + path + "##";
                },
                StringTable,
                x => "@@" + x + "@@");
            var pipData = AmbientJson.CreatePipData(StringTable, result, quoteChar, new PipDataBuilder(StringTable));

            var canonicalPipData = pipData.ToString(renderer).Replace("\r\n", "\n").Replace("/", "\\\\");
            var canonicalExpected = expected.Replace("\r\n", "\n").Replace("/", "\\\\");

            Assert.Equal(canonicalExpected, canonicalPipData);
        }

        // ===================== Json.read tests =====================

        [Fact]
        public void ReadEmptyObject()
        {
            var spec = @"
namespace M {
    export const result = _PreludeAmbientHack_Json.read('{}');
}";
            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionWithNoErrors("M.result") as ObjectLiteral;
            Assert.NotNull(result);
            Assert.Equal(0, result.Count);
        }

        [Fact]
        public void ReadBasicStringProperty()
        {
            var spec = @"
namespace M {
    const obj = _PreludeAmbientHack_Json.read('{""key"": ""value""}');
    export const result = obj['key'];
}";
            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionWithNoErrors("M.result");
            Assert.Equal("value", result);
        }

        [Fact]
        public void ReadBasicIntegerProperty()
        {
            var spec = @"
namespace M {
    const obj = _PreludeAmbientHack_Json.read('{""num"": 42}');
    export const result = obj['num'];
}";
            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionWithNoErrors("M.result");
            Assert.Equal(42, result);
        }

        [Fact]
        public void ReadBasicBooleanProperties()
        {
            var spec = @"
namespace M {
    const obj = _PreludeAmbientHack_Json.read('{""t"": true, ""f"": false}');
    export const r1 = obj['t'];
    export const r2 = obj['f'];
}";
            var builder = Build().AddSpec(spec);
            var r1 = builder.EvaluateExpressionWithNoErrors("M.r1");
            var r2 = builder.EvaluateExpressionWithNoErrors("M.r2");
            Assert.Equal(true, r1);
            Assert.Equal(false, r2);
        }

        [Fact]
        public void ReadNullBecomesUndefined()
        {
            var spec = @"
namespace M {
    const obj = _PreludeAmbientHack_Json.read('{""n"": null}');
    export const result = obj['n'] === undefined;
}";
            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionWithNoErrors("M.result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void ReadNestedObjects()
        {
            var spec = @"
namespace M {
    const obj = _PreludeAmbientHack_Json.read('{""outer"": {""inner"": {""key"": 42}}}');
    export const result = obj['outer']['inner']['key'];
}";
            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionWithNoErrors("M.result");
            Assert.Equal(42, result);
        }

        [Fact]
        public void ReadArray()
        {
            var spec = @"
namespace M {
    const obj = _PreludeAmbientHack_Json.read('{""arr"": [1, 2, 3]}');
    export const r1 = obj['arr'][0];
    export const r2 = obj['arr'][1];
    export const r3 = obj['arr'][2];
}";
            var builder = Build().AddSpec(spec);
            Assert.Equal(1, builder.EvaluateExpressionWithNoErrors("M.r1"));
            Assert.Equal(2, builder.EvaluateExpressionWithNoErrors("M.r2"));
            Assert.Equal(3, builder.EvaluateExpressionWithNoErrors("M.r3"));
        }

        [Fact]
        public void ReadArrayOfObjects()
        {
            var spec = @"
namespace M {
    const obj = _PreludeAmbientHack_Json.read('{""items"": [{""name"": ""a""}, {""name"": ""b""}]}');
    export const r1 = obj['items'][0]['name'];
    export const r2 = obj['items'][1]['name'];
}";
            var builder = Build().AddSpec(spec);
            Assert.Equal("a", builder.EvaluateExpressionWithNoErrors("M.r1"));
            Assert.Equal("b", builder.EvaluateExpressionWithNoErrors("M.r2"));
        }

        [Fact]
        public void ReadMixedArray()
        {
            var spec = @"
namespace M {
    const obj = _PreludeAmbientHack_Json.read('{""mix"": [1, ""two"", true, null]}');
    export const r1 = obj['mix'][0];
    export const r2 = obj['mix'][1];
    export const r3 = obj['mix'][2];
    export const r4 = obj['mix'][3] === undefined;
}";
            var builder = Build().AddSpec(spec);
            Assert.Equal(1, builder.EvaluateExpressionWithNoErrors("M.r1"));
            Assert.Equal("two", builder.EvaluateExpressionWithNoErrors("M.r2"));
            Assert.Equal(true, builder.EvaluateExpressionWithNoErrors("M.r3"));
            Assert.Equal(true, builder.EvaluateExpressionWithNoErrors("M.r4"));
        }

        [Fact]
        public void ReadMultipleProperties()
        {
            var spec = @"
namespace M {
    const obj = _PreludeAmbientHack_Json.read('{""s"": ""hello"", ""n"": 99, ""b"": true}');
    export const r1 = obj['s'];
    export const r2 = obj['n'];
    export const r3 = obj['b'];
}";
            var builder = Build().AddSpec(spec);
            Assert.Equal("hello", builder.EvaluateExpressionWithNoErrors("M.r1"));
            Assert.Equal(99, builder.EvaluateExpressionWithNoErrors("M.r2"));
            Assert.Equal(true, builder.EvaluateExpressionWithNoErrors("M.r3"));
        }

        [Fact]
        public void ReadNegativeInteger()
        {
            var spec = @"
namespace M {
    const obj = _PreludeAmbientHack_Json.read('{""neg"": -42}');
    export const result = obj['neg'];
}";
            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionWithNoErrors("M.result");
            Assert.Equal(-42, result);
        }

        [Fact]
        public void ReadEmptyArray()
        {
            var spec = @"
namespace M {
    const obj = _PreludeAmbientHack_Json.read('{""arr"": []}');
    export const result = (<Array<string>>obj['arr']).length;
}";
            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionWithNoErrors("M.result");
            Assert.Equal(0, result);
        }

        [Fact]
        public void ReadDeeplyNested()
        {
            var spec = @"
namespace M {
    const obj = _PreludeAmbientHack_Json.read('{""a"": {""b"": {""c"": {""d"": ""deep""}}}}');
    export const result = obj['a']['b']['c']['d'];
}";
            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionWithNoErrors("M.result");
            Assert.Equal("deep", result);
        }

        [Fact]
        public void ReadInvalidJsonThrows()
        {
            var spec = @"
namespace M {
    export const result = _PreludeAmbientHack_Json.read('{invalid}');
}";
            var result = Build()
                .AddSpec(spec)
                .EvaluateWithFirstError("M.result");
            Assert.Equal((int)LogEventId.ReportJsonDeserializationError, result.ErrorCode);
        }

        [Fact]
        public void ReadEmptyStringThrows()
        {
            var spec = @"
namespace M {
    export const result = _PreludeAmbientHack_Json.read('');
}";
            var result = Build()
                .AddSpec(spec)
                .EvaluateWithFirstError("M.result");
            Assert.Equal((int)LogEventId.ReportJsonDeserializationError, result.ErrorCode);
        }
    }
}
