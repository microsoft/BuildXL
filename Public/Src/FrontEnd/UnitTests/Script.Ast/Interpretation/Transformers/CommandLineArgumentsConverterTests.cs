// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Ambients.Transformers;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation.Transformers
{
    [Trait("Category", "Transformers")]
    public class CommandLineArgumentsConverterTests : DsTest
    {
        private static readonly string m_testAbsolutePath = OperatingSystemHelper.IsUnixOS ? "/" : "c:/";

        public CommandLineArgumentsConverterTests(ITestOutputHelper output)
            : base(output)
        {}

        [Fact]
        public void TestArgumentListProcessing()
        {
            // Arrange
            string Literal = String.Format(@"export const x: Argument[] = [
  Cmd.flag(""/nologo"", true),
  Cmd.option(""/timeout"", 42),
  Cmd.option(""/out"", Artifact.output(p`{0}foo.txt`)),
  Cmd.argument(p`{0}boo.txt`),
  Cmd.option(""/path"", p`{0}doo.txt`),
  Cmd.argument(PathAtom.create(""anAtom1"")),
  Cmd.option(""/atom"", PathAtom.create(""anAtom2"")),
  Cmd.option(""/rel"", RelativePath.create(""a/b/c"")),
  Cmd.argument(r`a/b/c`),
  Cmd.argument(r`a a/b b/c c`)
];", m_testAbsolutePath);

            // Act
            var parsedLiteral = ParseArrayLiteral(WrapWithCmdApi(Literal));
            var parsedArguments = CommandLineArgumentsConverter.ArrayLiteralToListOfArguments(FrontEndContext.StringTable, parsedLiteral).ToArray();

            // Assert
            Assert.Equal(
                new[]
                {
                    Cmd.Flag("/nologo"),
                    Cmd.Option("/timeout", 42),
                    Cmd.Option("/out", Artifacts.Output(CreateAbsolutePath(FrontEndContext, String.Format("{0}foo.txt", m_testAbsolutePath)))),
                    Cmd.Argument(CreateAbsolutePath(FrontEndContext, String.Format("{0}boo.txt", m_testAbsolutePath))),
                    Cmd.Option("/path", CreateAbsolutePath(FrontEndContext, String.Format("{0}doo.txt", m_testAbsolutePath))),
                    Cmd.Argument(CreatePathAtom(FrontEndContext, "anAtom1")),
                    Cmd.Option("/atom", CreatePathAtom(FrontEndContext, "anAtom2")),
                    Cmd.Option("/rel", CreateRelativePath("a" + Path.DirectorySeparatorChar + "b" + Path.DirectorySeparatorChar + "c")),
                    Cmd.Argument(CreateRelativePath("a" + Path.DirectorySeparatorChar + "b" + Path.DirectorySeparatorChar + "c")),
                    Cmd.Argument(CreateRelativePath("a a" + Path.DirectorySeparatorChar + "b b" + Path.DirectorySeparatorChar + "c c"))
                },
                parsedArguments);
        }

        [Theory]
        [MemberData(nameof(GetTestApiArgumentTestCases))]
        public void TestApiArgumentProcessing(string literal, Func<FrontEndContext, Argument> expectedArgumentFactory)
        {
            ParseAndEnsureEquality(WrapWithCmdApi(literal), expectedArgumentFactory(FrontEndContext));
        }

        [Theory]
        [MemberData(nameof(GetTestArgumentProcessingTestCases))]
        public void TestRawArgumentProcessing(string literal, Func<FrontEndContext, Argument> expectedArgumentFactory)
        {
            ParseAndEnsureEquality(literal, expectedArgumentFactory(FrontEndContext));
        }

        [Theory]
        [MemberData(nameof(GetConversionFailuresTestCases))]
        public void TestConversionFailures(string literal)
        {
            try
            {
                // Explicit try-catch was used to print error message for debugging purposes.
                var parsedLiteral = ParseObjectLiteral(literal);
                var parsedObjectLiteral = parsedLiteral as ObjectLiteral;

                if (parsedObjectLiteral != null)
                {
                    CommandLineArgumentsConverter.ObjectLiteralToArgument(FrontEndContext.StringTable, parsedObjectLiteral);
                }

                XAssert.Fail("DScript snippet '{0}' didn't fail as expected", literal);
            }
            catch (ConvertException e)
            {
                Console.WriteLine("Expected conversion exception happens. Message: " + e.Message);
            }
        }

        [Theory]
        [MemberData("GetConversionWithEvaluationFailure")]
        public void TestConversionWithEvaluationFailures(string literal)
        {
            EvaluateWithFirstError(literal, "x");
        }

        public static IEnumerable<object[]> GetConversionWithEvaluationFailure()
        {
            //
            // Name conversion issues (name should be string, not anything else).
            //
            yield return TestCase(WrapWithCmdApi(@"export const x = Cmd.option(42, 42);"));
            yield return TestCase(WrapWithCmdApi(@"export const x = Cmd.option(false, 42);"));
        }

        public static IEnumerable<object[]> GetConversionFailuresTestCases()
        {
            //
            // Name conversion issues (name should be string, not anything else).
            //
            yield return TestCase(WrapWithCmdApi(@"export const x = Cmd.option(<any>{x: ""42""}, 42);"));

            //
            // Value of unexpected type
            //
            yield return TestCase(WrapWithCmdApi(@"export const x = Cmd.option(""name"", <any>true);"));
            yield return TestCase(WrapWithCmdApi(@"export const x = Cmd.option(""name"", <any>{x: 42, y: 36});"));

            //
            // Artifact conversion issues
            //
            yield return TestCase(WrapWithArtifactKind(String.Format(@"export const x = {{name: ""scalar1"", value: {{kind: ArtifactKind.output, path: ""{0}foo.cs""}}}};",
                m_testAbsolutePath)));

            yield return TestCase(WrapWithArtifactKind(@"export const x = {name: ""scalar1"", value: {kind: ArtifactKind.output, path: 42}};"));

            //
            // PrimitiveArgument conversion issues
            //
            // TODO: Enable me when we add exception (not contract exception) for conversion failure.
            // yield return
            //    TestCase(WrapWithArgumentKind(@"export const x = {name: ""scalar1"", value: {kind: ArgumentKind.rawText, value: 'c:/foo.cs'}};"));
            yield return TestCase(WrapWithArgumentKind(@"export const x = {name: ""scalar2"", value: {kind: ArgumentKind.rawText, value: false}};"));

            // kind is not nullable.
            yield return TestCase(WrapWithArgumentKind(@"export const x = {name: ""scalar2"", value: {kind: undefined, value: 42}};"));

            //
            // PrimitiveArgument conversion issues
            //
            yield return TestCase(@"export const x = {name: ""list1"", value: {separator: 42, values: undefined}};");
            yield return TestCase(@"export const x = {name: ""list2"", value: {separator: "","", values: true}};");
            yield return TestCase(@"export const x = {name: ""list2"", value: {separator: "","", values: undefined}};");
            yield return TestCase(@"export const x = {name: ""list3"", value: {separator: "","", values: ""foo""}};");
            yield return TestCase(@"export const x = {name: ""list3"", value: {separator: "","", values: {x: 42}}};");
            yield return TestCase(@"export const x = {name: ""list3"", value: {separator: "","", values: [{x: 42}]}};");
        }

        public static IEnumerable<object[]> GetTestApiArgumentTestCases()
        {
            //
            // Cmd.flag + Cmd.sign
            //
            yield return TestCase(
                @"export const x = Cmd.flag(""/nologo"", true);",
                (context) => Cmd.Flag("/nologo"));

            yield return TestCase(
                @"export const x = Cmd.option(""/timeout"", 42);",
                (context) => Cmd.Option("/timeout", 42));

            yield return TestCase(
                @"export const x = Cmd.sign(""/unsafe"", true);",
                (context) => Cmd.Option("/unsafe", "+"));

            yield return TestCase(
                @"export const x = Cmd.sign(""/unsafe"", false);",
                (context) => Cmd.Option("/unsafe", "-"));

            yield return TestCase(
                @"export const x = Cmd.sign(""/unsafe"", undefined);",
                (context) => Cmd.Undefined());

            yield return TestCase(
                @"export const x = Cmd.sign(""/enableNoPlus"", true, true);",
                (context) => Cmd.Flag("/enableNoPlus"));

            yield return TestCase(
                @"export const x = Cmd.sign(""/enableNoPlus"", false, true);",
                (context) => Cmd.Option("/enableNoPlus", "-"));

            //
            // Cmd.option with files
            //
            yield return TestCase(
                String.Format(@"export const x = Cmd.option(""/out"", Artifact.output(p`{0}foo.txt`));", m_testAbsolutePath),
                (context) => Cmd.Option("/out", Artifacts.Output(CreateAbsolutePath(context, String.Format("{0}foo.txt", m_testAbsolutePath)))));

            yield return TestCase(
                String.Format(@"export const x = Cmd.option(""/input"", Artifact.input(p`{0}foo.txt`));", m_testAbsolutePath),
                (context) => Cmd.Option("/input", Artifacts.Input(CreateAbsolutePath(context, String.Format("{0}foo.txt", m_testAbsolutePath)))));

            yield return TestCase(
                String.Format(@"export const x = Cmd.option(""/outFolder"", Artifact.output(p`{0}foo`));", m_testAbsolutePath),
                (context) => Cmd.Option("/outFolder", Artifacts.Output(CreateAbsolutePath(context, String.Format("{0}foo", m_testAbsolutePath)))));

            yield return TestCase(
                String.Format(@"export const x = Cmd.option(""/rewrite"", Artifact.rewritten(f`{0}foo.txt`, p`{0}boo.txt`));", m_testAbsolutePath),
                (context) =>
                    Cmd.Option("/rewrite", Artifacts.Rewritten(
                        CreateFile(context, String.Format("{0}foo.txt", m_testAbsolutePath)),
                        CreateAbsolutePath(context, String.Format("{0}boo.txt", m_testAbsolutePath)))));

            yield return TestCase(
                String.Format(@"export const x = Cmd.option(""/rewrite"", Artifact.rewritten(f`{0}foo.txt`));", m_testAbsolutePath),
                (context) =>
                    Cmd.Option("/rewrite", Artifacts.InPlaceRewritten(CreateFile(context, String.Format("{0}foo.txt", m_testAbsolutePath)))));

            //
            // Cmd.options
            //
            yield return TestCase(
                String.Format(@"export const x = Cmd.options(""/crazy"", [Artifact.output(p`{0}foo.txt`), Artifact.input(p`{0}boo.txt`)]);", m_testAbsolutePath),
                (context) =>
                    Cmd.Options("/crazy",
                    Artifacts.Output(CreateAbsolutePath(context, String.Format("{0}foo.txt", m_testAbsolutePath))),
                    Artifacts.Input(CreateAbsolutePath(context, String.Format("{0}boo.txt", m_testAbsolutePath)))));
        }

        public static IEnumerable<object[]> GetTestArgumentProcessingTestCases()
        {
            // Current method return 'test case' information that contains two arguments for TestArgumentProcessing method:
            // literal to parse and factory method that will return an argument.
            // This factory can't return Argument instance because to create an instance FrontEndContextCore is required.
            // But front end context is test-specific and belong to the current instance.

            //
            // Scalar + primitive value test cases
            //
            yield return TestCase(
                @"export const x = {name: ""foo"", value: 42};",
                (context) => Cmd.Option("foo", 42));

            yield return TestCase(
                @"export const x = {name: ""foo"", value: ""42""};",
                (context) => Cmd.Option("foo", "42"));

            yield return TestCase(
                @"export const x = {name: undefined, value: ""42""};",
                (context) => Cmd.Option(null, "42"));

            yield return TestCase(
                @"export const x = {value: ""42""};",
                (context) => Cmd.Argument("42"));

            //
            // Scalar + FileArtifact test cases
            //
            yield return TestCase(
                WrapWithArtifactKind(String.Format(@"export const x = {{name: ""scalar1"", value: {{kind: ArtifactKind.output, path: p`{0}foo.cs`}}}};", m_testAbsolutePath)),
                (context) => Cmd.Option("scalar1", Artifacts.Output(CreateAbsolutePath(context, String.Format("{0}foo.cs", m_testAbsolutePath)))));

            yield return TestCase(
                WrapWithArtifactKind(String.Format(@"export const x = {{name: ""scalar2"", value: {{kind: ArtifactKind.input, path: p`{0}foo`}}}};", m_testAbsolutePath)),
                (context) => Cmd.Option("scalar2", Artifacts.Input(CreateAbsolutePath(context, String.Format("{0}foo", m_testAbsolutePath)))));

            //
            // PrimitiveArgument test cases
            //
            yield return TestCase(
                WrapWithArgumentKind(@"export const x = {name: ""prim1"", value: {kind: ArgumentKind.rawText, value: ""text""}};"),
                (context) => Cmd.RawText("prim1", "text"));

            yield return TestCase(
                WrapWithArgumentKind(@"export const x = {name: ""prim2"", value: {kind: ArgumentKind.regular, value: 42}};"),
                (context) => Cmd.RegularOption("prim2", 42));

            yield return TestCase(
                WrapWithArgumentKind(@"export const x = {name: undefined, value: {kind: ArgumentKind.startUsingResponseFile, value: undefined}};"),
                (context) => Cmd.StartUsingResponseFile());

            yield return TestCase(
                WrapWithArgumentKind(@"export const x = {name: ""@"", value: {kind: ArgumentKind.startUsingResponseFile, value: ""true""}};"),
                (context) => Cmd.StartUsingResponseFile("@", true));

            yield return TestCase(
                WrapWithArgumentKind(@"export const x = {name: ""prim4"", value: {kind: ArgumentKind.flag, value: undefined}};"),
                (context) => Cmd.Flag("prim4"));

            //
            // ScalarArgument[] test cases
            //
            yield return TestCase(
                WrapWithArgumentKind(@"export const x = {name: ""array1"", value: [42, ""42"", {kind: ArgumentKind.regular, value: 42}]};"),
                (context) => Cmd.Options(
                    "array1",
                    ArgumentValue.FromNumber(42),
                    ArgumentValue.FromString("42"),
                    ArgumentValue.FromPrimitive(ArgumentKind.Regular, new PrimitiveValue(42))));

            yield return TestCase(
                WrapWithArtifactKind(String.Format(@"export const x = {{name: ""array2"", value: [{{kind: ArtifactKind.output, path: p`{0}foo.cs`}}]}};", m_testAbsolutePath)),
                (context) => Cmd.Options(
                    "array2",
                    Artifacts.Output(CreateAbsolutePath(context, String.Format("{0}foo.cs", m_testAbsolutePath)))));

            //
            // ListArgument test cases
            //

            yield return TestCase(
                WrapWithArgumentKind(@"export const x = {name: ""list2"", value: {separator: "","", values: []}};"),
                (context) => Cmd.Option("list2", Cmd.Join(",", new ArgumentValue[0])));

            yield return TestCase(
                WrapWithArgumentKind(@"export const x = {name: ""list3"", value: {separator: "","", values: [1, 2, ""42""]}};"),
                (context) => Cmd.Option(
                    "list3",
                    Cmd.Join(
                        ",",
                        new[]
                        {
                            ArgumentValue.FromNumber(1),
                            ArgumentValue.FromNumber(2),
                            ArgumentValue.FromString("42")
                        })));

            yield return TestCase(
                WrapWithArtifactKindAndArgumentKind(
                    String.Format(@"export const x = {{name: ""list4"", value: {{separator: "","", values: [1, {{kind: ArtifactKind.output, path: p`{0}foo.cs`}}]}}}};", m_testAbsolutePath)),
                (context) => Cmd.Option(
                    "list4",
                    Cmd.Join(
                        ",",
                        new[]
                        {
                            ArgumentValue.FromNumber(1),
                            ArgumentValue.FromAbsolutePath(ArtifactKind.Output, CreateAbsolutePath(context, String.Format("{0}foo.cs", m_testAbsolutePath)))
                        })));
        }

        private static string WrapWithArtifactKindAndArgumentKind(string literal)
        {
            return WrapWithArtifactKind(WrapWithArgumentKind(literal));
        }

        private static string WrapWithArtifactKind(string literal)
        {
            const string ArtifactKindDeclaration = @"const enum ArtifactKind {
    input = 1,
    output,
    rewritten,
}";
            return ArtifactKindDeclaration + Environment.NewLine + literal;
        }

        private static string WrapWithArgumentKind(string literal)
        {
            const string ArtifactKindDeclaration = @"const enum ArgumentKind {
    rawText = 1,
    regular,
    flag,
    startUsingResponseFile,
}";
            return ArtifactKindDeclaration + Environment.NewLine + literal;
        }

        private static string WrapWithCmdApi(string literal)
        {
            return CommandLineApi + Environment.NewLine + literal;
        }

        private static object[] TestCase(string literal)
        {
            return new object[] {literal};
        }

        private static object[] TestCase(string literal, Func<FrontEndContext, Argument> expectedArgumentFactory)
        {
            return new object[] {literal, expectedArgumentFactory};
        }

        private static FileArtifact CreateFile(FrontEndContext context, string path)
        {
            return FileArtifact.CreateSourceFile(CreateAbsolutePath(context, path));
        }

        private static AbsolutePath CreateAbsolutePath(FrontEndContext context, string path)
        {
            return AbsolutePath.Create(context.PathTable, path);
        }

        private static PathAtom CreatePathAtom(FrontEndContext context, string pathAtom)
        {
            return PathAtom.Create(context.StringTable, pathAtom);
        }

        private void ParseAndEnsureEquality(string spec, Argument argument)
        {
            try
            {
                var parsedLiteral = ParseObjectLiteral(spec);

                if (parsedLiteral == UndefinedValue.Instance)
                {
                    Assert.Equal(argument, Cmd.Undefined());
                }
                else
                {
                    var parsedArgument = CommandLineArgumentsConverter.ObjectLiteralToArgument(
                        FrontEndContext.StringTable,
                        parsedLiteral as ObjectLiteral);
                    Assert.Equal(argument, parsedArgument);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Conversion failed for : " + spec.Split(new[] { Environment.NewLine }, StringSplitOptions.None).LastOrDefault());
                Console.WriteLine("    Exception : " + e.GetLogEventMessage());
                throw;
            }
        }

        private object ParseObjectLiteral(string spec)
        {
            Contract.Ensures(Contract.Result<object>() != null);

            // Please, use 'export const x' in a spec sample. Otherwise the test will fail.
            var result = EvaluateExpressionsWithNoErrors(spec, "x");

            Assert.NotEmpty(result.Values);
            return result.Values[0];
        }

        private ArrayLiteral ParseArrayLiteral(string spec)
        {
            Contract.Ensures(Contract.Result<ArrayLiteral>() != null);

            // Please, use 'export const x' in a spec sample. Otherwise the test will fail.
            var result = EvaluateExpressionsWithNoErrors(spec, "x");

            var expression = result.Values[0] as ArrayLiteral;

            Assert.NotNull(expression);
            return expression;
        }

        /// <summary>
        /// Set of factory methods similar to Cmd namespace from Common.dsc
        /// </summary>
        private static class Cmd
        {
            public static Argument Undefined()
            {
                return default(Argument);
            }

            public static Argument Undefined(string name)
            {
                return new Argument(name, default(CommandLineValue));
            }

            public static Argument Flag(string name)
            {
                return new Argument(name, new CommandLineValue(ArgumentValue.FromPrimitive(ArgumentKind.Flag, default(PrimitiveValue))));
            }

            public static Argument RawText(string name, string text)
            {
                return new Argument(name, new CommandLineValue(ArgumentValue.FromPrimitive(ArgumentKind.RawText, new PrimitiveValue(text))));
            }

            public static Argument StartUsingResponseFile(string name = null, bool? force = default(bool?))
            {
                return new Argument(
                    name,
                    new CommandLineValue(ArgumentValue.FromPrimitive(ArgumentKind.StartUsingResponseFile,
                        force.HasValue ? new PrimitiveValue(force.Value.ToString().ToLowerInvariant()) : default(PrimitiveValue))));
            }

            public static CompoundArgumentValue Join(string separator, ArgumentValue[] arguments)
            {
                return new CompoundArgumentValue(separator, arguments);
            }

            public static Argument Option(string name, int value)
            {
                return new Argument(name, new CommandLineValue(ArgumentValue.FromNumber(value)));
            }

            public static Argument Option(string name, string value)
            {
                return new Argument(name, new CommandLineValue(ArgumentValue.FromString(value)));
            }

            public static Argument Option(string name, AbsolutePath value)
            {
                return new Argument(name, new CommandLineValue(ArgumentValue.FromAbsolutePath(value)));
            }

            public static Argument Option(string name, RelativePath value)
            {
                return new Argument(name, new CommandLineValue(ArgumentValue.FromRelativePath(value)));
            }

            public static Argument Option(string name, PathAtom value)
            {
                return new Argument(name, new CommandLineValue(ArgumentValue.FromPathAtom(value)));
            }

            public static Argument Option(string name, Artifact value)
            {
                return new Argument(name, new CommandLineValue(new ArgumentValue(value)));
            }

            public static Argument Option(string name, CompoundArgumentValue value)
            {
                return new Argument(name, new CommandLineValue(new ArgumentValue(value)));
            }

            public static Argument Option(string name, ArgumentValue value)
            {
                return new Argument(name, new CommandLineValue(value));
            }

            public static Argument Options(string name, params Artifact[] values)
            {
                return new Argument(name, new CommandLineValue(values.Select(x => new ArgumentValue(x)).ToArray()));
            }

            public static Argument Options(string name, params ArgumentValue[] values)
            {
                return new Argument(name, new CommandLineValue(values));
            }

            public static Argument RegularOption(string name, int value)
            {
                return new Argument(name, new CommandLineValue(ArgumentValue.FromPrimitive(ArgumentKind.Regular, new PrimitiveValue(value))));
            }

            public static Argument RegularOption(string name, string value)
            {
                return new Argument(name, new CommandLineValue(ArgumentValue.FromPrimitive(ArgumentKind.Regular, new PrimitiveValue(value))));
            }

            public static Argument Argument(int value)
            {
                return Option(null, value);
            }

            public static Argument Argument(string value)
            {
                return Option(null, value);
            }

            public static Argument Argument(AbsolutePath value)
            {
                return Option(null, value);
            }

            public static Argument Argument(RelativePath value)
            {
                return Option(null, value);
            }

            public static Argument Argument(PathAtom value)
            {
                return Option(null, value);
            }
        }

        /// <summary>
        /// Set of factory methods similar to Artifact namespace from Common.dsc
        /// </summary>
        private static class Artifacts
        {
            public static Artifact Output(FileArtifact artifact)
            {
                return new Artifact(ArtifactKind.Output, artifact);
            }

            public static Artifact Output(AbsolutePath artifact)
            {
                return new Artifact(ArtifactKind.Output, artifact);
            }

            public static Artifact Output(DirectoryArtifact artifact)
            {
                return new Artifact(ArtifactKind.Output, artifact);
            }

            public static Artifact Input(FileArtifact artifact)
            {
                return new Artifact(ArtifactKind.Input, artifact);
            }

            public static Artifact Input(AbsolutePath artifact)
            {
                return new Artifact(ArtifactKind.Input, artifact);
            }

            public static Artifact Input(DirectoryArtifact artifact)
            {
                return new Artifact(ArtifactKind.Input, artifact);
            }

            public static Artifact Rewritten(FileArtifact originalFile, AbsolutePath copyPath)
            {
                return new Artifact(ArtifactKind.Rewritten, copyPath, originalFile);
            }

            public static Artifact InPlaceRewritten(FileArtifact originalFile)
            {
                return new Artifact(ArtifactKind.Rewritten, originalFile);
            }
        }
    }
}
