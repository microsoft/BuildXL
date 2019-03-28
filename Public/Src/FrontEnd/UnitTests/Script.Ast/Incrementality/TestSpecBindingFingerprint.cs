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
    public class TestSpecBindingFingerprint
    {
        [Fact]
        public void NamespaceDeclarationsArePartOfTheFingerprint()
        {
            string code = @"
namespace X.Y {}
";

            // Namespace itself is a declaration and the first class citizen.
            // So it is part of the fingerprint.
            ParseCodeAndValidateSymbols(code, Ns("X.Y"));
        }

        [Fact]
        public void DecoratorsArePartOfTheFingerprint()
        {
            string code = @"
@@Tool.option(someName.value)
interface Foo {
}
";

            ParseCodeAndValidateSymbols(code, "Foo", "Tool.option", "someName.value");
        }

        [Fact]
        public void AllConstDeclarationsArePartOfTheFingerprint()
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

            // All variable declaration could change the overload resolution of the file.
            // That's why all the variables are part of the interaction fingerprint.
            ParseCodeAndValidateSymbols(code, "X.Y", "X.Y.privateValue", "X.Y.internalValue.internal", "X.Y.publicValue.public", "internalStandAlone.internal");
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
        public void FunctionDeclarationLocalsAndParametersArePartOfTheFingerprint()
        {
            string code = @"
function foo(n: number) {
  let y = z;
  for(let x in []) {}
}";

            // Function declaration, locals within it and the arguments are part of the fingerprint.
            ParseCodeAndValidateSymbols(code, Func("foo"), Param("foo.n"), Var("foo.y"), Var("foo.__for_in__1.x"), Ref("foo.z"));
        }

        [Fact]
        public void IndentifiersUsedInsideFunctionArePartOfTheFingerprint()
        {
            string code = @"
function foo() {
  return someName.bar;
}";

            // All identifiers used inside the function are part of the fingerprint
            ParseCodeAndValidateSymbols(code, "foo", "foo.someName.bar");
        }

        [Fact]
        public void IndentifiersUsedInsideAnonymousFunctionArePartOfTheFingerprint()
        {
            string code = @"
namespace N {
  const x = (() => {
    return someName.bar;
  })();
}";
            
            ParseCodeAndValidateSymbols(code, Ns("N"), Var("N.x"), Ref("N.__fn__0.someName.bar"));
        }

        [Fact]
        public void AllIdentifiersReferencedByLocalsArePartOfTheFingerprint()
        {
            string code = @"
const x = 42;
const y = x;
const z = fromAnotherFile.someField;
";

            // Very important to add all referenced identifiers to a fingerprint
            ParseCodeAndValidateSymbols(code, "x", "y", "z", "fromAnotherFile.someField");
        }

        [Fact]
        public void ImportDeclarationsArePartOfTheFingerprint()
        {
            string code = @"
import * as foo from 'anotherPackage';
import {tool} from 'tool';
";

            // Very important to add all referenced identifiers to a fingerprint
            ParseCodeAndValidateSymbols(code, "foo", "tool", "anotherPackage");
        }
        
        [Fact]
        public void ImportFromShouldBeEscaped()
        {
            string code = @"
const x = {y: importFrom('foo').someField};
";

            ParseCodeAndValidateSymbols(code, "x", "importFrom.foo.someField");
        }

        [Fact]
        public void FunctionCallWithObjectLiteralShouldBeCorrectlyStored()
        {
            string code = @"
const x = {y: fooBar({x:someValue}).someResult.anotherField};
";
            ParseCodeAndValidateSymbols(code, "x", "someValue", "fooBar.someResult.anotherField");
        }

        [Fact]
        public void ExportIntroducesAReferenceAndIsPartOfTheFingerprint()
        {
            string code = @"
export {name};
";

            ParseCodeAndValidateSymbols(code, Import("name"));
        }

        [Fact]
        public void ReferenceAndIsPartOfTheFingerprint()
        {
            string code = @"
export {name};
";

            ParseCodeAndValidateSymbols(code, Import("name"));
        }

        [Fact]
        public void LabelsUsedInSwitchPartOfTheFingerprint()
        {
            string code = @"
function foo(e: MyEnum) {
  switch (e) {
    case MyEnum.value1: return 42;
   }
};";

            // {foo, foo.e, foo.MyEnum, foo.__switch__0.e, foo.__switch__0.MyEnum.value1}
            ParseCodeAndValidateSymbols(code, "foo", "foo.MyEnum", "foo.e", "foo.__switch__0.MyEnum.value1", "foo.__switch__0.e");
        }

        [Fact]
        public void ImportedPackagesArePartOfTheFingerprint()
        {
            string code = @"
import * as foo from 'anotherPackage';
const x = importFrom('package2');
";

            // Very important to add all referenced identifiers to a fingerprint
            ParseCodeAndValidateSymbols(code, "foo", "anotherPackage", "x", "importFrom.package2");
        }

        [Fact]
        public void ImportAliasesArePartOfTheFingerprint()
        {
            string code = @"
import {tool as blah} from 'somePackage';
";

            // Very important to add all referenced identifiers to a fingerprint
            ParseCodeAndValidateSymbols(code, Import("tool"), Import("blah"), Module("somePackage"));
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
        public void BaseTypeIsPartOfTheFingerprint()
        {
            string code = @"
interface Foo extends X.Bar {}";

            // Bar is part of the fingerprint
            ParseCodeAndValidateSymbols(code, "Foo", "Foo.X.Bar");
        }

        [Fact]
        public void TestFingerprintForEmptyInterface()
        {
            string code = @"
interface Foo {;}";

            ParseCodeAndValidateSymbols(code, "Foo");
        }

        [Fact]
        public void IdentifiersReferencedInObjectLiteralArePartOfTheFingerprint()
        {
            string code = @"
const x = {field: y, z, fooBar: foo.bar};";

            // All identifiers used by the object literal are part of the fingerprint.
            ParseCodeAndValidateSymbols(code, Var("x"), Ref("y"), Ref("z"), Ref("foo.bar"));
        }

        [Fact]
        public void TypesUsedInLocalsArePartOfTheFingerprint()
        {
            string code = @"
namespace N {
  const x: {field: X.Y.Z} = undefined;
}";

            // Type used in anonymous type declaration is part of the fingerprint.
            // Technically, the smallest fingerprint should be "X.Y.Z", but due to implementation
            // restrictions, "X.Y" (i.e. all trailing dotted expressions) considered as part of the fingerptint.
            // "X" on the other hand is not a dotted expression and the implementation can remove it from the fingerprint.
            ParseCodeAndValidateSymbols(code, Ns("N"), Var("N.x"), TypeRef("N.X.Y.Z"));
        }

        [Fact]
        public void IdentifiersUsedInArrayLiteralArePartOfTheFingerprint()
        {
            string code = @"
const x = [a,b];";

            ParseCodeAndValidateSymbols(code, Var("x"), Ref("a"), Ref("b"));
        }

        [Fact]
        public void IdentifiersUsedAsArgumentsArePartOfTheFingerprint()
        {
            string code = @"
const x = foo(a,b);";

            ParseCodeAndValidateSymbols(code, Var("x"), Ref("a"), Ref("b"), Ref("foo"));
        }

        [Fact]
        public void TypesUsedInFunctionSignatureArePartOfTheFingerprint()
        {
            string code = @"
function foo(x: { prop: Y.Z }): { z: Z.K } {return undefined;}";
            ParseCodeAndValidateSymbols(code, Func("foo"), Param("foo.x"), TypeRef("foo.Y.Z"), TypeRef("foo.Z.K"));
        }

        [Fact]
        public void TwoFilesWithDifferentSourcesFieldHasTheSameFingerprint()
        {
            string code1 = @"
export const dll = Sdk.build({sources: [f`a.cs`]});";
            string code2 = @"
export const dll = Sdk.build({sources: [f`a.cs`, f`b.cs`], anotherField: 42});";

            var code1Fingerprint = CreateFingerprint(code1);
            var code2Fingerprint = CreateFingerprint(code2);
            XAssert.SetEqual(code1Fingerprint.Symbols, code2Fingerprint.Symbols, InteractionSymbolEqualityComparer.Instance);
            XAssert.AreEqual(code1Fingerprint.ReferencedSymbolsFingerprint, code2Fingerprint.ReferencedSymbolsFingerprint);
        }

        [Fact]
        public void TwoFilesWithDifferentObjectFieldsHasTheSameFingerprints()
        {
            string code1 = @"
export const dll = Sdk.build({sources: [f`a.cs`]});";
            string code2 = @"
export const dll = Sdk.build({sources: [f`a.cs`, p`b.cs`], anotherField: 42});";

            var code1Fingerprint = CreateFingerprint(code1);
            var code2Fingerprint = CreateFingerprint(code2);
            XAssert.SetEqual(code1Fingerprint.Symbols, code2Fingerprint.Symbols, InteractionSymbolEqualityComparer.Instance);
            XAssert.AreEqual(code1Fingerprint.ReferencedSymbolsFingerprint, code2Fingerprint.ReferencedSymbolsFingerprint);
        }

        [Fact]
        public void TwoFilesWithDifferentIdentifiersHasDifferentFingerprints()
        {
            string code1 = @"
export const dll = Sdk.build({sources: [f`a.cs`]});";
            string code2 = @"
export const dll = Sdk.build({sources: [f`a.cs`, p`b.cs`], anotherField});";

            var code1Fingerprint = CreateFingerprint(code1);
            var code2Fingerprint = CreateFingerprint(code2);
            XAssert.SetNotEqual(code1Fingerprint.Symbols, code2Fingerprint.Symbols);
            XAssert.AreNotEqual(code1Fingerprint.ReferencedSymbolsFingerprint, code2Fingerprint.ReferencedSymbolsFingerprint);
        }

        /// <summary>
        /// Building a fingerprint should not throw when encountering invalid DS code
        /// </summary>
        [Fact]
        public void TaggedTemplateInImportNoException()
        {
            string code = "namespace Foo { import * from f`Sdk.Transformers`; }";

            var fingerprint = CreateFingerprint(code);
            XAssert.AreEqual(2, fingerprint.Symbols.Count, string.Join(",", fingerprint.Symbols.Select(s => $"({s.Kind},{s.FullName})")));

            // The namespace is represented in the set of symbols
            XAssert.IsTrue(fingerprint.Symbols.Contains(Ns("Foo")));

            // The wildcard alias is represented in the set of symbols
            XAssert.IsNotNull(fingerprint.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.ImportAlias));
        }

        private SpecBindingSymbols CreateFingerprint(string code)
        {
            var source = ParseAndEnsureNodeIsNotNull(code);
            return SpecBindingSymbols.Create(source, keepSymbols: true);
        }

        private void ParseCodeAndValidateSymbols(string code, params string[] expectedSymbols)
        {
            var fingerprint = CreateFingerprint(code);
            var members = fingerprint.Symbols.Select(t => t.FullName).ToList();

            XAssert.SetEqual(expectedSymbols, members);
        }

        private InteractionSymbol Func(string fullName) => Symbol(SymbolKind.FunctionDeclaration, fullName);
        private InteractionSymbol Param(string fullName) => Symbol(SymbolKind.ParameterDeclaration, fullName);
        private InteractionSymbol Ref(string fullName) => Symbol(SymbolKind.Reference, fullName);
        private InteractionSymbol TypeRef(string fullName) => Symbol(SymbolKind.TypeReference, fullName);
        private InteractionSymbol Var(string fullName) => Symbol(SymbolKind.VariableDeclaration, fullName);
        private InteractionSymbol IFace(string fullName) => Symbol(SymbolKind.InterfaceDeclaration, fullName);
        private InteractionSymbol IFaceMember(string fullName) => Symbol(SymbolKind.InterfaceMemberDeclaration, fullName);
        private InteractionSymbol Ns(string fullName) => Symbol(SymbolKind.NamespaceDeclaration, fullName);
        private InteractionSymbol Enum(string fullName) => Symbol(SymbolKind.EnumDeclaration, fullName);
        private InteractionSymbol EnumValue(string fullName) => Symbol(SymbolKind.EnumValueDeclaration, fullName);
        private InteractionSymbol Import(string fullName) => Symbol(SymbolKind.ImportAlias, fullName);
        private InteractionSymbol Module(string fullName) => Symbol(SymbolKind.ImportedModule, fullName);

        private InteractionSymbol Symbol(SymbolKind kind, string fullName)
        {
            return new InteractionSymbol(kind, fullName);
        }

        private void ParseCodeAndValidateSymbols(string code, params InteractionSymbol[] expectedMembers)
        {
            var fingerprint = CreateFingerprint(code);
            var members = fingerprint.Symbols;

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
