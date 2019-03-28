// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Ambients.Exceptions;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using BuildXL.Pips.Operations;
using Xunit;
using Xunit.Abstractions;

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
    }
}
