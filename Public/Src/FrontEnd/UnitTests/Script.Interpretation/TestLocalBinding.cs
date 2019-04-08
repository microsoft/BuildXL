// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class TestLocalBinding : DsTest
    {
        public TestLocalBinding(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void BindingMemberFunctionsOnInterfaceDeclaration()
        {
            string code = @"
interface Bar {
   y: number;
   fn: (x: number, y: string) => number;
}

const z = 1;
const b: Bar = {
  y: 42,
  fn: (x, y) => {return ((x:number) => x)(z) + x + +y;}
};

export const r = b.fn(1, '2'); // 1 + 1 + 2
";
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(4, result);
        }

        [Fact]
        public void ComplexCodeThatUsesDifferentKindsOfBinding()
        {
            string code = @"
function assert(b: boolean) {
  if (!b) {
    Contract.fail('fail');
  }
}

const namespaceLevel = 42;

function bar() { return 1; }

function foo(a: number) {
  assert(a === 42);

  const l1 = namespaceLevel;
  assert(l1 === 42);

  const l2 = bar();
  assert(l2 === 1);

  let l3 = 0;
  for (let namespaceLevel of [1]) {
    l3 = namespaceLevel; // using local
    assert(l3 === 1);
  }

  const localFunction = (a: number) => {
    assert(a === 1);
    
    let l1 = l2; // can capture local from the enclosing function
    assert(l1 === 1);

    // l3 = l1; // This doesn't work right now!!!!
    {
      let l1 = namespaceLevel;
      assert(l1 === 42); // can capture global!
    }

    // This is a super weird way to check that local functions with expression body are working fine.
    return ((a: number) => a)(a);
  };

  return localFunction(1);
}

export const r = foo(42);
";

            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal(1, result);
        }
    }
}
