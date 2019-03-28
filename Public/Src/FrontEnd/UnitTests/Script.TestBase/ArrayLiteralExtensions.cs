// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
