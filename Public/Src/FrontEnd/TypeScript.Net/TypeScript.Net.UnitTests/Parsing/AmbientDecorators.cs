// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;
using Xunit.Abstractions;

namespace TypeScript.Net.UnitTests.Parsing
{
    /// <summary>
    /// Test cases for ambient decorators that is used @@ for denoting compile-time decorators.
    /// </summary>
    /// <remarks>
    /// Currently there is no difference in AST between real decorators and ambient decorators.
    /// Because of that, round tripping will never work on ambient decorators without major changes in the AST.
    /// </remarks>
    public class AmbientDecorators
    {
        private readonly ITestOutputHelper m_output;

        public AmbientDecorators(ITestOutputHelper output)
        {
            m_output = output;
        }

        [Fact]
        public void AmbientDecoratorOnImport()
        {
            string code =
@"@@qualifier({x: 42})
import * as X from 'foo.dsc';";

            var node = ParsingHelper.ParseFirstStatementFrom<IImportDeclaration>(code, roundTripTesting: false);
            m_output.WriteLine($"Declaration:\r\n{node.GetFormattedText()}");

            Assert.NotEmpty(node.Decorators.Elements);
            Assert.Equal("qualifier", node.Decorators[0].GetMethodName());
        }

        [Fact]
        public void AmbientDecoratorOnInterfaceDeclaration()
        {
            string code =
@"@@toolOption({name: 'csc'})
interface CscArgs {refs: string[];}";

            var node = ParsingHelper.ParseFirstStatementFrom<IInterfaceDeclaration>(code, roundTripTesting: false);
            m_output.WriteLine($"Declaration:\r\n{node.GetFormattedText()}");

            Assert.NotEmpty(node.Decorators.Elements);
            Assert.Equal("toolOption", node.Decorators[0].GetMethodName());
        }

        [Fact]
        public void AmbientDecoratorOnInterfacemembersDeclaration()
        {
            string code =
@"interface CscArgs {
  @@toolOption({option: 'references'})
  refs: string[];
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IInterfaceDeclaration>(code, roundTripTesting: false);
            m_output.WriteLine($"Declaration:\r\n{node.GetFormattedText()}");

            var firstMember = node.Members[0];
            Assert.NotEmpty(firstMember.Decorators.Elements);
            Assert.Equal("toolOption", firstMember.Decorators[0].GetMethodName());
        }

        [Fact]
        public void AmbientDecoratorOnEnumDeclaration()
        {
            string code =
@"@@kindOption({name: 'csc'})
enum CscKind {kind1 = 42}";

            var node = ParsingHelper.ParseFirstStatementFrom<IEnumDeclaration>(code, roundTripTesting: false);
            m_output.WriteLine($"Declaration:\r\n{node.GetFormattedText()}");

            Assert.NotEmpty(node.Decorators.Elements);
            Assert.Equal("kindOption", node.Decorators[0].GetMethodName());
        }

        [Fact]
        public void AmbientDecoratorOnEnumMembers()
        {
            string code =
@"enum CscKind {
  @@toolOption({name: 'roslyn'})
  roslyn = 1
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IEnumDeclaration>(code, roundTripTesting: false);
            m_output.WriteLine($"Declaration:\r\n{node.GetFormattedText()}");

            var firstMember = node.Members[0];
            Assert.NotEmpty(firstMember.Decorators.Elements);
            Assert.Equal("toolOption", firstMember.Decorators[0].GetMethodName());
        }
    }

    internal static class DecoratorExtensions
    {
        public static string GetMethodName(this IDecorator decorator)
        {
            return decorator.Expression.Cast<ICallExpression>().Expression.Cast<IIdentifier>().Text;
        }
    }
}
