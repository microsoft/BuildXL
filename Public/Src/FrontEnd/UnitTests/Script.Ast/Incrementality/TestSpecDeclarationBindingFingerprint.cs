// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using Test.BuildXL.TestUtilities.Xunit;
using TypeScript.Net.Binding;
using TypeScript.Net.DScript;
using TypeScript.Net.Incrementality;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using Xunit;

namespace Test.DScript.Ast.Incrementality
{
    public class TestSpecDeclarationBindingFingerprint
    {
        [Fact]
        public void NamespaceDeclarationsArePartOfTheFingerprint()
        {
            string code = @"
namespace X.Y {}
";

            ParseCodeAndValidateSymbols(code, Ns("X.Y"));
        }

        [Fact]
        public void DecoratorsAreNotPartOfTheFingerprint()
        {
            string code = @"
@@Tool.option(someName.value)
interface Foo {
}
";

            ParseCodeAndValidateSymbols(code, "Foo");
        }

        [Fact]
        public void OnlyVisibleConstDeclarationsArePartOfTheFingerprint()
        {
            string code = @"
namespace X.Y {
    const privateValue = 42;
    export const internalValue = 42;
    @@public
    export const publicValue = 42;
}

export const internalStandAlone = 42;
";

            ParseCodeAndValidateSymbols(code, "X.Y", "X.Y.internalValue.internal", "X.Y.publicValue.public", "internalStandAlone.internal");
        }

        [Fact]
        public void AllTypeDeclarationsShouldBePartOfTheFingerprint()
        {
            string code = @"
namespace X.Y {
  type TypeAlias = number;
  type TypeUnion = number | string;
  interface CustomInterface {  }
  const enum ConstEnum {}
}

interface CustomInterface2 {}
";

            // All type declarations are part of the fingerprint.
            ParseCodeAndValidateSymbols(code, "X.Y", "X.Y.TypeAlias", "X.Y.TypeUnion", "X.Y.CustomInterface", "X.Y.ConstEnum", "CustomInterface2");
        }

        [Fact]
        public void InterfaceMemberShouldBePartOfTheFingerprint()
        {
            string code = @"
interface CustomInterface {
  name: string;
}
";

            // All interface members are part of the fingerprint
            ParseCodeAndValidateSymbols(code, IFace("CustomInterface"), IFaceMember("CustomInterface.name"));
        }

        [Fact]
        public void FunctionDeclarationLocalsAndParametersAreNotPartOfTheFingerprint()
        {
            string code = @"
function foo(n: number) : SomeType {
  let y = z;
  for(let x in []) {}
  return undefined;
}";

            // Only function name is part of a declaration fingerprint
            ParseCodeAndValidateSymbols(code, Func("foo"));
        }

        [Fact]
        public void IndentifiersUsedInsideAnonymousFunctionAreNotPartOfTheFingerprint()
        {
            string code = @"
namespace N {
  export const x = (() => {
    return someName.bar;
  })();
}";
            
            ParseCodeAndValidateSymbols(code, Ns("N"), Var("N.x.internal"));
        }

        [Fact]
        public void AllIdentifiersReferencedByLocalsAreNotPartOfTheFingerprint()
        {
            string code = @"
const x = 42;
const y = x;
const z = fromAnotherFile.someField;
";

            // Only exported/public declarations are part of the declaration fingerprint.
            ParseCodeAndValidateSymbols(code, new InteractionSymbol[0]);
        }

        [Fact]
        public void ImportDeclarationsAreNotPartOfTheFingerprint()
        {
            string code = @"
import * as foo from 'anotherPackage';
import {tool} from 'tool';
";

            // Very important to add all referenced identifiers to a fingerprint
            ParseCodeAndValidateSymbols(code, new InteractionSymbol[0]);
        }
        
        [Fact]
        public void ExportIntroducesAReferenceButIsNotPartOfTheFingerprint()
        {
            string code = @"
export {name};
";

            ParseCodeAndValidateSymbols(code, new InteractionSymbol[0]);
        }

        [Fact]
        public void ImportAliasesAreNotPartOfTheFingerprint()
        {
            string code = @"
import {tool as blah} from 'somePackage';
";

            // Very important to add all referenced identifiers to a fingerprint
            ParseCodeAndValidateSymbols(code, new InteractionSymbol[0]);
        }

        [Fact]
        public void EnumMembersArePartOfTheFingerprint()
        {
            string code = @"
namespace Y {
    const enum X {
        y = 1,
    }
}
";

            // Enum declaration and all the values are part of the fingerprint
            ParseCodeAndValidateSymbols(code, Ns("Y"), Enum("Y.X"), EnumValue("Y.X.y"));
        }

        [Fact]
        public void BaseTypeIsNotPartOfTheFingerprint()
        {
            string code = @"
interface Foo extends X.Bar {}";

            // the base type is not part of the declaration fingerprint.
            ParseCodeAndValidateSymbols(code, "Foo");
        }

        [Fact]
        public void TestFingerprintForEmptyInterface()
        {
            string code = @"
interface Foo {;}";

            ParseCodeAndValidateSymbols(code, "Foo");
        }

        [Fact]
        public void IdentifiersReferencedInObjectLiteralAreNotPartOfTheFingerprint()
        {
            string code = @"
const x = {field: y, z, fooBar: foo.bar}";

            ParseCodeAndValidateSymbols(code, new InteractionSymbol[0]);
        }

        [Fact]
        public void TypesUsedInLocalsAreNotPartOfTheFingerprint()
        {
            string code = @"
namespace N {
  const x: {field: X.Y.Z} = undefined;
}";

            ParseCodeAndValidateSymbols(code, Ns("N"));
        }

        [Fact]
        public void IdentifiersUsedAsArgumentsAreNotPartOfTheFingerprint()
        {
            string code = @"
const x = foo(a,b)";

            ParseCodeAndValidateSymbols(code, new InteractionSymbol[0]);
        }

        [Fact]
        public void TypesUsedInFunctionSignatureAreNotPartOfTheFingerprint()
        {
            string code = @"
function foo(x: { prop: Y.Z }): { z: Z.K } {return undefined;}";
            ParseCodeAndValidateSymbols(code, Func("foo"));
        }

        [Fact]
        public void TwoFilesWithDifferentReferencesHasTheSameFingerprint()
        {
            string code1 = @"
export const dll = Sdk.build(a.dll);";
            string code2 = @"
export const dll = Sdk.build(b.dll);";

            var code1Fingerprint = CreateFingerprint(code1);
            var code2Fingerprint = CreateFingerprint(code2);
            XAssert.SetEqual(code1Fingerprint.DeclaredSymbols, code2Fingerprint.DeclaredSymbols, InteractionSymbolEqualityComparer.Instance);
            XAssert.AreEqual(code1Fingerprint.DeclaredSymbolsFingerprint, code2Fingerprint.DeclaredSymbolsFingerprint);
        }

        [Fact]
        public void TwoFilesWithDifferentIdentifiersHasTheSameDifferentFingerprints()
        {
            string code1 = @"
export const dll = Sdk.build({sources: [f`a.cs`]});";
            string code2 = @"
export const dll = Sdk.build({sources: [f`a.cs`, p`b.cs`], anotherField});";

            var code1Fingerprint = CreateFingerprint(code1);
            var code2Fingerprint = CreateFingerprint(code2);
            XAssert.SetEqual(code1Fingerprint.DeclaredSymbols, code2Fingerprint.DeclaredSymbols);
            XAssert.AreEqual(code1Fingerprint.DeclaredSymbolsFingerprint, code2Fingerprint.DeclaredSymbolsFingerprint);
        }
        
        private SpecBindingSymbols CreateFingerprint(string code)
        {
            var source = ParseAndEnsureNodeIsNotNull(code);
            return SpecBindingSymbols.Create(source, keepSymbols: true);
        }

        private void ParseCodeAndValidateSymbols(string code, params string[] expectedSymbols)
        {
            var fingerprint = CreateFingerprint(code);
            var members = fingerprint.DeclaredSymbols.Select(t => t.FullName).ToList();

            XAssert.SetEqual(expectedSymbols, members);
        }

        private InteractionSymbol Func(string fullName) => Symbol(SymbolKind.FunctionDeclaration, fullName);
        private InteractionSymbol Param(string fullName) => Symbol(SymbolKind.ParameterDeclaration, fullName);
        private InteractionSymbol Var(string fullName) => Symbol(SymbolKind.VariableDeclaration, fullName);
        private InteractionSymbol IFace(string fullName) => Symbol(SymbolKind.InterfaceDeclaration, fullName);
        private InteractionSymbol IFaceMember(string fullName) => Symbol(SymbolKind.InterfaceMemberDeclaration, fullName);
        private InteractionSymbol Ns(string fullName) => Symbol(SymbolKind.NamespaceDeclaration, fullName);
        private InteractionSymbol Enum(string fullName) => Symbol(SymbolKind.EnumDeclaration, fullName);
        private InteractionSymbol EnumValue(string fullName) => Symbol(SymbolKind.EnumValueDeclaration, fullName);

        private InteractionSymbol Symbol(SymbolKind kind, string fullName)
        {
            return new InteractionSymbol(kind, fullName);
        }

        private void ParseCodeAndValidateSymbols(string code, params InteractionSymbol[] expectedMembers)
        {
            var fingerprint = CreateFingerprint(code);
            var members = fingerprint.DeclaredSymbols;

            XAssert.SetEqual(expectedMembers, members);
        }

        private static ISourceFile ParseAndEnsureNodeIsNotNull(string code)
        {
            var parser = new Parser();
            ISourceFile node = parser.ParseSourceFile(
                "fakeFileName.ts",
                code,
                ScriptTarget.Es2015,
                syntaxCursor: null,
                setParentNodes: false,
                parsingOptions: ParsingOptions.DefaultParsingOptions);

            Assert.NotNull(node);

            // Node should be bound in order to get fingerprint.
            Binder.Bind(node, CompilerOptions.Empty);

            return node;
        }
    }
}
