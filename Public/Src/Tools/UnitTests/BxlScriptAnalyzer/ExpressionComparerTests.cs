// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Script.Analyzer.Analyzers;
using BuildXL.FrontEnd.Script.Analyzer.Utilities;
using BuildXL.FrontEnd.Sdk;
using TypeScript.Net.Extensions;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using Xunit;

namespace Test.Tool.DScript.Analyzer
{
    public class ExpressionComparerTests
          : AnalyzerTest<PrettyPrint>
    {
        [Fact]
        public void Empty()
        {
            TestSort(@"[]", @"[]");
        }

        [Fact]
        public void SingleString()
        {
            TestSort(@"['a']", @"['a']");
        }

        [Fact]
        public void NumbersBeforeString()
        {
            TestSort(@"['a', 1]", @"[1, 'a']");
        }

        [Fact]
        public void Numbers()
        {
            TestSort(@"[140, 11, 399, 3]", @"[
    3,
    11,
    140,
    399,
]");
        }

        [Fact]
        public void String()
        {
            TestSort(@"['140', '11', '399', '3']", @"[
    '11',
    '140',
    '3',
    '399',
]");
        }

        [Fact]
        public void StringKinds()
        {
            TestSort(@"[""b"", 'a', `b`, 'b', `a`, ""a""]", @"[
    'a',
    'b',
    ""a"",
    ""b"",
    `a`,
    `b`,
]");
        }

        [Fact]
        public void Files()
        {
            TestSort(@"[f`b.cs`, f`/c/d`, f`/c`, f`b`, f`b/folder/b.cs`, f`/g`, f`c.cs`, f`b/folder`, f`a.cs`]", @"[
    f`a.cs`,
    f`b`,
    f`b.cs`,
    f`c.cs`,
    f`b/folder`,
    f`b/folder/b.cs`,
    f`/c`,
    f`/g`,
    f`/c/d`,
]");
        }

        [Fact]
        public void FileInterpollations()
        {
            TestSort(@"[f`a/b/f1.cs`, f`a/${buildAlt}/f2.cs`, f`/a/b/f3.cs`, f`${objRoot}/a/b/f3.cs`, f`${objRoot}/a/${buildAlt}/f3.cs`, f`/a/${buildAlt}/f3.cs`]", @"[
    f`a/b/f1.cs`,
    f`a/${buildAlt}/f2.cs`,
    f`/a/b/f3.cs`,
    f`/a/${buildAlt}/f3.cs`,
    f`${objRoot}/a/b/f3.cs`,
    f`${objRoot}/a/${buildAlt}/f3.cs`,
]");
        }

        [Fact]
        public void SpreadOperator()
        {
            TestSort(@"[...lst1, A.B.c2, ...A.B.lst, A.b1, ...A.lst, ...lst2]", @"[
    A.b1,
    A.B.c2,
    ...lst1,
    ...lst2,
    ...A.lst,
    ...A.B.lst,
]");
        }

        [Fact]
        public void DifferentTypes()
        {
            TestSort(@"[-1, f`f1/f1`, `i2`, d`d1/d1`, r`r1/r1`, r`r1/r2`, f`f2`, d`d1`, `i1`, 's1', 's2', 0, A.B.C1.d1, ...A.B.lst, d`/d1/d1`, ...A.lst, f`f0/f3`, a`a2`, r`r3`, a`a1`, a1, A.b1, 1]", @"[
    -1,
    0,
    1,
    's1',
    's2',
    `i1`,
    `i2`,
    a`a1`,
    a`a2`,
    d`d1`,
    d`d1/d1`,
    d`/d1/d1`,
    f`f2`,
    f`f0/f3`,
    f`f1/f1`,
    r`r3`,
    r`r1/r1`,
    r`r1/r2`,
    a1,
    A.b1,
    A.B.C1.d1,
    ...A.lst,
    ...A.B.lst,
]");
        }

        private void TestSort(string input, string expected)
        {
            var testSource = $"export const x = {input};";

            var context = FrontEndContext.CreateInstanceForTesting();
            Workspace workspace;
            var kv = LoadAndTypecheckFile(
                context,
                testSource,
                CommonSources,
                null,
                out workspace);

            var array = kv.Value
                .Statements
                .ToList()
                .First(s => !s.IsInjectedForDScript()) // When parallel evaluation is happenign we don't know if this file is the first, so if it has the injected qualifier node.
                .As<IVariableStatement>()
                .DeclarationList
                .Declarations
                .First()
                .As<IVariableDeclaration>()
                .Initializer
                .As<IArrayLiteralExpression>();

            array.Elements.Sort(ExpressionComparers.CompareExpression);

            var actualText = array.GetFormattedText();
            Assert.Equal(expected, actualText);
        }

        private string[] CommonSources => new[]
        {
            @"
export const objRoot = d`.`;
export const buildAlt = a`lalala`;
export const a1 = 'a1';
export const a2 = 'a2';
export const a3 = 'a3';
export const lst1 = ['l11', 'l12'];
export const lst2 = ['l21', 'l22'];
namespace A {
    export const b1 = 'b1';
    export const b2 = 'b2';
    export const b3 = 'b3';
    export const lst = ['l1', 'l2'];
    namespace B {
        export const c1 = 'c1';
        export const c2 = 'c2';
        export const c3 = 'c3';
        export const lst = ['l1', 'l2'];
        namespace C1 {
            export const d1 = 'd1';
            export const d2 = 'd2';
            export const d3 = 'd3';
            export const lst = ['l1', 'l2'];
        }
        namespace C2 {
            export const d1 = 'd1';
            export const d2 = 'd2';
            export const d3 = 'd3';
        }
    }
}

namespace PrettyPrint {
    export function sortable() : (a: any) => any {
        return x => x;
    }
}

export interface Arguments {
    normal?: string[],
    @@PrettyPrint.sortable()
    canSort?: string[],
    nested?: {
       @@PrettyPrint.sortable()
       normalNest?: string[],
       canSortNest?: string[],
    }
}

export function test(args: Arguments) : string {
    return 'x';
}
"
        };
    }
}
