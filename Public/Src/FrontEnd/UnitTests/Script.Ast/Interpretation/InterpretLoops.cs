// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretLoops : DsTest
    {
        private const int IterationThreshold = 50;

        public InterpretLoops(ITestOutputHelper output) : base(output) { }

        protected override FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged)
        {
            var conf = base.GetFrontEndConfiguration(isDebugged);
            conf.MaxLoopIterations = IterationThreshold;
            return conf;
        }

        [Fact]
        public void EvaluateEmptyForLoopWithNoExpressions()
        {
            // This shouldn't break.
            // Bug #932837
            // The following code is invalid due to DScript language constraints: for(; ;); because initializer and condition are mandatory.
            // So the test case actually has an initializer and condition but doesn't have an expression.
            string code = "const tmp = (() => {for (let x = 0; ;x++) { break;}})(); export const r = 42;";

            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void EvaluateForOfLoop()
        {
            string code = @"
namespace M {
    function fun() {
        let x: string = ""A"";
        let xs: string[] = [""B"", ""C""];
        let y: string = """";

        for (let x of xs) {
            y = y + x;
        }

        return x + y;
    }

    export const result = fun();
}";
            var result = EvaluateExpressionWithNoErrors(code, "M.result");

            Assert.Equal("ABC", result);
        }

        [Fact]
        public void EvaluateForOfLoopWithBreak()
        {
            string code = @"
namespace M {
    function indexOf(x: number[], r: number): number {
         let result: number = -1;
         let idx = 0;

         for (let n of x) {
            if (n === r) {
              result = idx;
              break;
            }

            idx += 1;
         }

         return result;
    }
    const ar = [1, 2, 3, 4];

    export const r1 = indexOf(ar, -1); // -1
    export const r2 = indexOf(ar, 3); // 2
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");

            Assert.Equal(-1, result["M.r1"]);
            Assert.Equal(2, result["M.r2"]);
        }

        [Fact]
        public void EvaluateForLoopWithBreak()
        {
            string code = @"
namespace M {
    function indexOf(x: number[], r: number): number {
         let result: number = -1;
         let idx = 0;

         for (let n = 0; n < x.length; n+=1) {
            if (x[n] === r) {
              result = idx;
              break;
            }

            idx += 1;
         }

         return result;
    }
    const ar = [1, 2, 3, 4];

    export const r1 = indexOf(ar, -1); // -1
    export const r2 = indexOf(ar, 3); // 2
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");

            Assert.Equal(-1, result["M.r1"]);
            Assert.Equal(2, result["M.r2"]);
        }

        [Fact]
        public void EvaluateForLoop()
        {
            string code = @"
namespace M {
    function fun(count: number) {
      let r = 0;
      for (let x = 0; x < count; x += 1) { r = r + 1; }
      return r;
    }
    export const result = fun(10);
}";
            var result = EvaluateExpressionWithNoErrors(code, "M.result");

            Assert.Equal(10, result);
        }

        [Fact]
        public void EvaluateForLoopWithContinue()
        {
            string code = @"
namespace M {
    function fun(count: number) {
      let r = 0;
      for (let x = 0; x < count; x += 1) {
          if (x % 2 === 0) continue;
          r = r + 1;
      }

      return r;
    }
    export const result = fun(10);
}";
            var result = EvaluateExpressionWithNoErrors(code, "M.result");

            Assert.Equal(5, result);
        }

        [Fact]
        public void EvaluateForOfLoopWithContinue()
        {
            string code = @"
namespace M {
    function fun(count: number) {
      let arr = [];
      for (let i = 0; i < count; i += 1) {
         arr = [...arr, i];
      }

      let r = 0;
      for (let x of arr) {
         if (x % 2 === 0) continue;
          r = r + 1;
      }

      return r;
    }
    export const result = fun(10);
}";
            var result = EvaluateExpressionWithNoErrors(code, "M.result");

            Assert.Equal(5, result);
        }

        [Fact]
        public void EvaluateForLoopWithContinueStatementBlock()
        {
            string code = @"
namespace M {
    function fun(count: number) {
      let r = 0;
      for (let x = 0; x < count; x += 1) {
          if (x % 2 === 0) { continue; }
          r = r + 1;
      }

      return r;
    }
    export const result = fun(10);
}";
            var result = EvaluateExpressionWithNoErrors(code, "M.result");

            Assert.Equal(5, result);
        }

        [Fact]
        public void EvaluateForOfLoopWithContinueStatementBlock()
        {
            string code = @"
namespace M {
    function fun(count: number) {
      let arr = [];
      for (let i = 0; i < count; i += 1) {
         arr = [...arr, i];
      }

      let r = 0;
      for (let x of arr) {
         if (x % 2 === 0) { continue; }
          r = r + 1;
      }

      return r;
    }
    export const result = fun(10);
}";
            var result = EvaluateExpressionWithNoErrors(code, "M.result");

            Assert.Equal(5, result);
        }

        [Fact]
        public void ParsingForLoopWithCSharpStyleShouldNotCrash()
        {
            string code = @"
function foo() {
  for (int i = 0 ; i < 10; i = i + 1) {;}
}";
            ParseWithDiagnosticId(code, LogEventId.TypeScriptSyntaxError);
        }

        [Fact]
        public void ForLoopOverflow()
        {
            string code = @"
    function fun() {
      for (let x = 0; x < " + (IterationThreshold + 10) + @"; x++) {}
      return 42;
    }
    export const result = fun(); 
";
            var result = EvaluateWithFirstError(code, "result");
            Assert.Equal((int)LogEventId.ForLoopOverflow, result.ErrorCode);
            AssertMessageContainsLoopIterationMaximum(result.Message);
        }

        [Fact]
        public void ForLoopNoOverflow()
        {
            string code = @"
    function fun() {
      for (let x = 0; x < " + (IterationThreshold - 10) + @"; x++) {}
      return 42;
    }
    export const result = fun();
";
            var result = EvaluateExpressionWithNoErrors(code, "result");
            Assert.Equal(42, result);
        }

        [Fact]
        public void ForEachBreak()
        {
            string code = @"
    function fun() {
      let val = 0;
      while (val < 3)
      {
        for (let x of [1, 2, 3]) { break; }
        val++;
      }
      return val;
    }
    export const result = fun();
";
            var result = EvaluateExpressionWithNoErrors(code, "result");
            Assert.Equal(3, result);
        }

        [Fact]
        public void WhileNonEntry()
        {
            string code = @"
function myFunction(): number
{
  let x = 42; 
  while (false)
  { 
    const y = x; 
  }
  return x;
}

export const val = myFunction();";

            var result = EvaluateExpressionWithNoErrors(code, "val");
            Assert.Equal(42, result);
        }

        [Fact]
        public void WhileBodyOnce()
        {
            string code = @"
function myFunction(): number
{
  let x = 0; 
  while (x < 1)
  {
    x = 1;
  }
  return x;
}

export const val = myFunction();";

            var result = EvaluateExpressionWithNoErrors(code, "val");
            Assert.Equal(1, result);
        }

        [Fact]
        public void WhileBodyTenTimes()
        {
            string code = @"
function myFunction(): number
{
  let x = 0; 
  while (x < 10)
  {
    x = x + 1;
  }
  return x;
}

export const val = myFunction();";

            var result = EvaluateExpressionWithNoErrors(code, "val");
            Assert.Equal(10, result);
        }

        [Fact]
        public void WhileContinueStatement()
        {
            string code = @"
function myFunction(): number
{
  let x = 0;
  let y = 0;
  while (x < 10)
  {
    if (++x > 5) continue;
    y = y + 1;
  }
  return y;
}

export const val = myFunction();";

            var result = EvaluateExpressionWithNoErrors(code, "val");
            Assert.Equal(5, result);
        }

        [Fact]
        public void WhileContinueStatementBlock()
        {
            string code = @"
function myFunction(): number
{
  let x = 0;
  let y = 0;
  while (x < 10)
  {
    if (++x > 5) { continue; }
    y = y + 1;
  }
  return y;
}

export const val = myFunction();";

            var result = EvaluateExpressionWithNoErrors(code, "val");
            Assert.Equal(5, result);
        }

        [Fact]
        public void WhileNestedContinue()
        {
            string code = @"
function myFunction(): number
{
  let x = 0;
  let z = 1;
  while (x < 3)
  {
    x++;
    if (x === 3) { continue; } // Skip the last loop

    let y = 0;
    while (y < 3)
    {
      y++;
      if (y === 3) { continue; } // Skip the last loop
      z = z * x * y;
    }
  }
  return z;
}

export const val = myFunction();";

            var result = EvaluateExpressionWithNoErrors(code, "val");

            // (z = 1) * (1 * 1) * (1 * 2) * (2 * 1) * (2 * 2)
            Assert.Equal(16, result);
        }

        [Fact]
        public void WhileBreak()
        {
            string code = @"
function myFunction(): number
{
  let x = 0; 
  while (x < 10)
  {
    x = x + 1;
    if (x === 5)
    {
      break;
    }
  }
  return x;
}

export const val = myFunction();";

            var result = EvaluateExpressionWithNoErrors(code, "val");
            Assert.Equal(5, result);
        }

        // Break within a switch case should not affect the outer loop
        [Fact]
        public void WhileBreakCaseClause()
        {
            string code = @"
function myFunction(): number
{
  let x = 0;
  let y = 0;
  while (x < 10)
  {
    x++;
    switch (x) { case 5: { break; } }
    y = y + 1;
  }
  return y;
}

export const val = myFunction();";

            var result = EvaluateExpressionWithNoErrors(code, "val");
            Assert.Equal(10, result);
        }

        [Fact]
        public void WhileReturn()
        {
            string code = @"
function myFunction(): number
{
  let x = 0; 
  while (x < 10)
  {
    x = x + 1;
    if (x === 5)
    {
      return x;
    }
  }
  return x;
}

export const val = myFunction();";

            var result = EvaluateExpressionWithNoErrors(code, "val");
            Assert.Equal(5, result);
        }
        
        [Fact]
        public void WhileBodyError()
        {
            string code = @"
function myFunction(): number
{
  let x = 0; 
  while (x < 10)
  {
    x = x + 1;
    if (x === 5)
    {
      let y = x / 0;
    }
  }
  return x;
}

export const val = myFunction();";

            var diagnostic = EvaluateWithFirstError(code, "val");
            Assert.Equal(9229, diagnostic.ErrorCode);
        }
        
        [Fact]
        public void WhileConditionFirstTimeError()
        {
            string code = @"
function myFunction(): number
{
  let x = 0; 
  while (x < (3 / 0))
  {
    x = x + 1;
  }
  return x;
}

export const val = myFunction();";

            var diagnostic = EvaluateWithFirstError(code, "val");
            Assert.Equal(9229, diagnostic.ErrorCode);
        }
        
        [Fact]
        public void WhileConditionSecondTimeError()
        {
            string code = @"
function myFunction(): number
{
  let x = 1; 
  while (2 / x > 1)
  {
    x = x - 1;
  }
  return x;
}

export const val = myFunction();";

            var diagnostic = EvaluateWithFirstError(code, "val");
            Assert.Equal(9229, diagnostic.ErrorCode);
        }

        [Fact]
        public void WhileOverIterationThreshold()
        {
            string code = @"
function myFunction(): number
{
  let x = 0; 
  while (x < " + (IterationThreshold + 1) + @")
  {
    x = x + 1;
  }
  return x;
}

export const val = myFunction();";

            var diagnostic = EvaluateWithFirstError(code, "val");
            Assert.Equal((int)LogEventId.WhileLoopOverflow, diagnostic.ErrorCode);
            AssertMessageContainsLoopIterationMaximum(diagnostic.Message);
        }

        [Fact]
        public void WhileUnderIterationThreshold()
        {
            string code = @"
function myFunction(): number
{
  let x = 0; 
  while (x < " + (IterationThreshold - 1) + @")
  {
    x = x + 1;
  }
  return x;
}

export const val = myFunction();";

            var result = EvaluateExpressionWithNoErrors(code, "val");
            Assert.Equal(IterationThreshold - 1, result);
        }

        private static void AssertMessageContainsLoopIterationMaximum(string message)
        {
            Assert.Contains("current iteration maximum is " + IterationThreshold, message);
        }
    }
}
