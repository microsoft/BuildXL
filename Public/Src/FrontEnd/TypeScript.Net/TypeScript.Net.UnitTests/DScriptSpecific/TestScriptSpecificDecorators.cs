// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;

namespace Test.DScript.DScriptSpecific
{
    public sealed class TestScriptSpecificDecorators
    {
        [Fact]
        public void FunctionDeclarationsSupportDecorators()
        {
            @"
function dec(b: string) { return c => c; }

@@dec('hello')
export function bar() {return 42;}"
            .TypeCheckAndAssertNoErrors();
        }

        [Fact]
        public void NamespaceDeclarationsSupportDecorators()
        {
            @"
export declare const qualifier: {}; // withQualifier(qualifier) is injected into A and we don't have the prelude here

@@42
namespace A{
    export const x = 42;
}"
            .TypeCheckAndAssertNoErrors();
        }
    }
}
