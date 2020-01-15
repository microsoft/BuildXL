// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.FrontEnd.Script.Values;

#pragma warning disable 1591

namespace Test.DScript.Ast.Utilities
{
    public static class ArrayLiteralExtensions
    {
        public static object[] ValuesAsObjects(this ArrayLiteral arrayLiteral)
        {
            return arrayLiteral.Values.Select(v => v.Value).ToArray();
        }
    }
}
