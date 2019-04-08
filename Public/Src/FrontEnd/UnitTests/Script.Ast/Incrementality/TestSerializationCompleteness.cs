// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Script.Expressions.CompositeExpressions;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Statements;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Ast.DScriptV2;
using Test.BuildXL.Utilities;
using TypeScript.Net.Utilities;
using Xunit;
using Xunit.Abstractions;
using LineInfo = TypeScript.Net.Utilities.LineInfo;
using Type = System.Type;
using System.Linq;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class TestSerializationCompleteness : DScriptV2Test
    {
        public TestSerializationCompleteness(ITestOutputHelper output)
            : base(output)
        {
            m_stringTable = new StringTable(0);
            m_symbolTable = new SymbolTable(m_stringTable);
        }

        private GlobalModuleLiteral GlobalModuleLiteral => new GlobalModuleLiteral(m_symbolTable);

        private readonly StringTable m_stringTable;

        private readonly SymbolTable m_symbolTable;

        private readonly PathTable m_pathTable = new PathTable();

        private readonly ModuleRegistry m_moduleRegistry = new ModuleRegistry();

        private readonly QualifierSpaceId m_qualifierSpaceId = new QualifierSpaceId(42);

        public readonly LineInfo DefaultLineInfo = LineInfo.FromLineAndPosition(3, 6);

        public SymbolAtom GetSymbolAtom() => SymbolAtom.Create(m_stringTable, "x");

        public FullSymbol GetFullSymbol() => FullSymbol.Create(m_symbolTable, GetSymbolAtom());

        public PathAtom GetPathAtom() => PathAtom.Create(m_stringTable, "hello");

        public AbsolutePath GetAbsolutePath() =>
            AbsolutePath.Create(m_pathTable, OperatingSystemHelper.IsUnixOS ? "/package.dsc" : "c:/package.dsc");

        public StringId GetStringId() => StringId.Create(m_stringTable, "test");

        public LineMap GetLineMap() => new LineMap(new int[]{1,2,3}, true);

        public Package GetPackage()
        {
            var packageId = PackageId.Create(GetStringId());
            IPackageDescriptor iPackageDescriptor = new PackageDescriptor();
            return Package.Create(packageId, GetAbsolutePath(), iPackageDescriptor);
        }

        public QualifierValue GetQualifierValue()
        {
            var qualifierTable = new QualifierTable(m_stringTable);
            var qualifierId = qualifierTable.CreateQualifier(new Tuple<string, string>("hey", "hello"));
            return QualifierValue.Create(qualifierId, qualifierTable, m_stringTable);
        }

        public FileModuleLiteral GetFileModuleLiteral()
        {
            return new FileModuleLiteral(GetAbsolutePath(), GetQualifierValue(), GlobalModuleLiteral, GetPackage(), m_moduleRegistry, GetLineMap());
        }

        public LocationBasedSymbolReference GetLocationBasedSymbolReference()
        {
            return new LocationBasedSymbolReference(new FilePosition(4, GetAbsolutePath()), GetSymbolAtom(), DefaultLineInfo, m_symbolTable);
        }

        public TypeOrNamespaceModuleLiteral GetTypeOrNamespaceModuleLiteral()
        {
            return new TypeOrNamespaceModuleLiteral(ModuleLiteralId.Create(GetFullSymbol()), GetFileModuleLiteral().Qualifier, GetFileModuleLiteral(), DefaultLineInfo);
        }

        public List<global::BuildXL.FrontEnd.Script.Types.Type> GetTypeList()
        {
            return new List<global::BuildXL.FrontEnd.Script.Types.Type> {PrimitiveType.NumberType, PrimitiveType.StringType};
        }

        public Expression GetExpression1() { return new NumberLiteral(42, DefaultLineInfo); }

        public Expression GetExpression2() { return new NumberLiteral(64, DefaultLineInfo); }

        public List<Expression> GetExpressionList() { return new List<Expression>() {GetExpression1(), GetExpression2()}; }

        public VarDeclaration GetVarDeclaration()
        {
            return new VarDeclaration(
                GetSymbolAtom(),
                PrimitiveType.NumberType,
                GetExpression1(),
                Declaration.DeclarationFlags.Export,
                DefaultLineInfo);
        }

        public List<TypeParameter> GetTypeParameterList()
        {
            var typeParameter = new TypeParameter(GetSymbolAtom(), PrimitiveType.NumberType, DefaultLineInfo);
            var typeParameter2 = new TypeParameter(GetSymbolAtom(), PrimitiveType.StringType, DefaultLineInfo);
            return new List<TypeParameter> {typeParameter, typeParameter2};
        }

        public CallSignature GetCallSignature()
        {
            return new CallSignature(GetTypeParameterList(), GetParameterList(), PrimitiveType.NumberType, DefaultLineInfo);
        }

        public List<Signature> GetCallSignatureList()
        {
            return new List<Signature> {GetCallSignature(), new PropertySignature(GetSymbolAtom(), PrimitiveType.NumberType, true, GetExpressionList(), DefaultLineInfo)};
        }

        public List<Parameter> GetParameterList()
        {
            var parameter = new Parameter(GetSymbolAtom(), PrimitiveType.NumberType, ParameterKind.Required, DefaultLineInfo);
            var parameter2 = new Parameter(GetSymbolAtom(), PrimitiveType.NumberType, ParameterKind.Optional, DefaultLineInfo);
            var parameter3 = new Parameter(GetSymbolAtom(), PrimitiveType.NumberType, ParameterKind.Rest, DefaultLineInfo);
            return new List<Parameter> {parameter, parameter2, parameter3};
        }

        public FunctionStatistic GetFunctionStatistic()
        {
            return new FunctionStatistic(GetSymbolAtom(), GetSymbolAtom(), GetCallSignature(), StringTable);
        }

        public CallableMember0<int> GetCallableMember0()
        {
            return new CallableMember0<int>(FunctionStatistic.Empty, GetSymbolAtom(), null, true);
        }

        public Statement GetStatement1() { return new ExpressionStatement(GetExpression1(), DefaultLineInfo); }

        public Statement GetStatement2() { return new ExpressionStatement(GetExpression2(), DefaultLineInfo); }

        public List<Statement> GetStatementList() { return new List<Statement> {GetStatement1(), GetStatement2()}; }

        public CaseClause GetCaseClause() { return new CaseClause(GetExpression1(), GetStatementList(), DefaultLineInfo); }

        public ImportOrExportClause GetImportOrExportClause()
        {
            return new ImportOrExportClause(new NamespaceImport(GetFullSymbol(), DefaultLineInfo), DefaultLineInfo);
        }

        [Fact]
        public void TestArrayLiteralWithSpreads()
        {
            ArrayLiteralWithSpreads node = new ArrayLiteralWithSpreads(new[] {GetExpression1()}, 0, DefaultLineInfo, GetAbsolutePath());
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestInterpolatedPaths()
        {
            InterpolatedPaths node = new InterpolatedPaths(GetExpressionList(), true, DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestNullNode()
        {
            NullNode node = new NullNode();
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestSourceFile()
        {
            SourceFile node = new SourceFile(AbsolutePath.Create(PathTable, A("c", "foobar.txt")), new List<Declaration> {GetVarDeclaration(), GetVarDeclaration()});
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestArrayType()
        {
            ArrayType node = new ArrayType(PrimitiveType.NumberType, DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestCallSignature()
        {
            CallSignature node = GetCallSignature();
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestFunctionType()
        {
            FunctionType node = new FunctionType(GetTypeParameterList(), GetParameterList(), PrimitiveType.NumberType, DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestNamedTypeReference()
        {
            NamedTypeReference node = new NamedTypeReference(GetSymbolAtom(), GetTypeList(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestObjectType()
        {
            ObjectType node = new ObjectType(GetCallSignatureList(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestParameter()
        {
            foreach (ParameterKind kind in System.Enum.GetValues(typeof(ParameterKind)))
            {
                Parameter node = new Parameter(GetSymbolAtom(), PrimitiveType.NumberType, kind, DefaultLineInfo);
                Parameter node2 = CheckSerializationRoundTrip(node);
                Assert.Equal(node.Kind, node2.Kind);
                Assert.Equal(node.ParameterKind, node2.ParameterKind);
            }
        }

        [Fact]
        public void TestPrimitiveType()
        {
            foreach (PrimitiveTypeKind kind in System.Enum.GetValues(typeof(PrimitiveTypeKind)))
            {
                PrimitiveType node = new PrimitiveType(kind, DefaultLineInfo);
                PrimitiveType node2 = CheckSerializationRoundTrip(node);
                Assert.Equal(node.Kind, node2.Kind);
            }
        }

        [Fact]
        public void TestPropertySignature()
        {
            PropertySignature node = new PropertySignature(GetSymbolAtom(), PrimitiveType.NumberType, true, GetExpressionList(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestTupleType()
        {
            TupleType node = new TupleType(new List<global::BuildXL.FrontEnd.Script.Types.Type> {PrimitiveType.NumberType, PrimitiveType.BooleanType}, DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestTypeParameter()
        {
            TypeParameter node = new TypeParameter(GetSymbolAtom(), PrimitiveType.NumberType, DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestTypeQuery()
        {
            TypeQuery node = new TypeQuery(new NamedTypeReference(GetSymbolAtom(), GetTypeList(), DefaultLineInfo), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestUnionType()
        {
            UnionType node = new UnionType(GetTypeList(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestBlockStatement()
        {
            BlockStatement node = new BlockStatement(GetStatementList(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestBreakStatement()
        {
            BreakStatement node = new BreakStatement(DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestCaseClause()
        {
            CaseClause node = GetCaseClause();
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestContinueStatement()
        {
            ContinueStatement node = new ContinueStatement(DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestDefaultClause()
        {
            DefaultClause node = new DefaultClause(GetStatementList(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestExpressionStatement()
        {
            ExpressionStatement node = new ExpressionStatement(GetExpression1(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestForOfStatement()
        {
            ForOfStatement node = new ForOfStatement(new VarStatement(GetSymbolAtom(), 1, PrimitiveType.NumberType, null, DefaultLineInfo), GetExpression1(), GetStatement1(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestForStatement()
        {
            ForStatement node = new ForStatement(GetStatement1(), GetExpression1(), new AssignmentExpression(GetSymbolAtom(), 0, AssignmentOperator.Assignment, GetExpression1(), DefaultLineInfo), GetStatement2(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestIfStatement()
        {
            IfStatement node = new IfStatement(GetExpression1(), GetStatement1(), GetStatement2(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestReturnStatement()
        {
            ReturnStatement node = new ReturnStatement(GetExpression1(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestSwitchStatement()
        {
            var caseClause2 = new CaseClause(GetExpression2(), GetStatementList(), DefaultLineInfo);
            SwitchStatement node = new SwitchStatement(GetExpression1(), new List<CaseClause> {GetCaseClause(), caseClause2}, new DefaultClause(GetStatementList(), DefaultLineInfo), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestVarStatement()
        {
            VarStatement node = new VarStatement(GetSymbolAtom(), 1, PrimitiveType.NumberType, GetExpression1(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestWhileStatement()
        {
            WhileStatement node = new WhileStatement(GetExpression1(), GetStatement1(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestBoolLiteral()
        {
            BoolLiteral node = new BoolLiteral(true, DefaultLineInfo);
            var trueNode = CheckSerializationRoundTrip(node);

            node = new BoolLiteral(false, DefaultLineInfo);
            var falseNode = CheckSerializationRoundTrip(node);

            Assert.NotEqual(trueNode.ToDebugString(), falseNode.ToDebugString());
        }

        [Fact]
        public void TestDirectoryLiteralExpression()
        {
            DirectoryLiteralExpression node = new DirectoryLiteralExpression(GetExpression1(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestFileLiteral()
        {
            FileLiteral node = new FileLiteral(GetAbsolutePath(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestFileLiteralExpression()
        {
            FileLiteralExpression node = new FileLiteralExpression(GetExpression1(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestNumberLiteral()
        {
            NumberLiteral node = (NumberLiteral)GetExpression1();
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestPathAtomLiteral()
        {
            PathAtomLiteral node = new PathAtomLiteral(GetPathAtom(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestPathLiteral()
        {
            PathLiteral node = new PathLiteral(GetAbsolutePath(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestRelativePathLiteral()
        {
            RelativePathLiteral node = new RelativePathLiteral(RelativePath.Create(GetPathAtom()), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestResolvedStringLiteral()
        {
            ResolvedStringLiteral node = new ResolvedStringLiteral(GetAbsolutePath(), "hello", DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestStringLiteral()
        {
            StringLiteral node = new StringLiteral("hello", DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestStringLiteralExpression()
        {
            StringLiteralExpression node = new StringLiteralExpression(new ApplyExpressionWithTypeArguments(GetExpression1(), GetTypeList(), GetExpressionList().ToArray(), DefaultLineInfo), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestUndefinedLiteral()
        {
            UndefinedLiteral node = UndefinedLiteral.Instance;
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestNonGenericApplyExpression()
        {
            NonGenericApplyExpression node = new NonGenericApplyExpression(GetExpression1(), GetExpressionList().ToArray(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestApplyExpressionWithTypeArguments()
        {
            ApplyExpressionWithTypeArguments node = new ApplyExpressionWithTypeArguments(GetExpression1(), GetTypeList(), GetExpressionList().ToArray(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestArrayExpression()
        {
            ArrayExpression node = new ArrayExpression(GetExpressionList(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestAssignmentExpression()
        {
            foreach (AssignmentOperator op in System.Enum.GetValues(typeof(AssignmentOperator)))
            {
                AssignmentExpression node = new AssignmentExpression(GetSymbolAtom(), 0, op, GetExpression1(), DefaultLineInfo);
                var node2 = CheckSerializationRoundTrip(node);
                Assert.Equal(op, node2.OperatorKind);
            }
        }

        [Fact]
        public void TestBinaryExpression()
        {
            foreach (BinaryOperator op in System.Enum.GetValues(typeof(BinaryOperator)))
            {
                BinaryExpression node = new BinaryExpression(GetExpression1(), op, GetExpression1(), DefaultLineInfo);
                var node2 = CheckSerializationRoundTrip(node);
                Assert.Equal(op, node2.OperatorKind);
            }
        }

        [Fact]
        public void TestCastExpression()
        {
            CastExpression node = new CastExpression(GetExpression1(), PrimitiveType.NumberType, CastExpression.TypeAssertionKind.AsCast, DefaultLineInfo);
            CheckSerializationRoundTrip(node);

            node = new CastExpression(GetExpression1(), PrimitiveType.NumberType, CastExpression.TypeAssertionKind.TypeCast, DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestConditionalExpression()
        {
            ConditionalExpression node = new ConditionalExpression(GetExpression1(), GetExpression2(), GetExpression1(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestImportAliasExpression()
        {
            ImportAliasExpression node = new ImportAliasExpression(GetAbsolutePath(), UniversalLocation.FromLineInfo(DefaultLineInfo, GetAbsolutePath(), m_pathTable));
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestIncrementDecrementExpression()
        {
            foreach (IncrementDecrementOperator op in System.Enum.GetValues(typeof(IncrementDecrementOperator)))
            {
                IncrementDecrementExpression node = new IncrementDecrementExpression(GetSymbolAtom(), 0, op, DefaultLineInfo);
                var node2 = CheckSerializationRoundTrip(node);
                Assert.Equal(op, node2.OperatorKind);
            }
        }

        [Fact]
        public void TestIndexExpression()
        {
            IndexExpression node = new IndexExpression(GetExpression1(), GetExpression2(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestLambdaExpression()
        {
            // InvokeAmbient must be null for serialization
            CallSignature callSignature = GetCallSignature();
            FunctionLikeExpression node = new FunctionLikeExpression(GetSymbolAtom(), callSignature, GetStatement1(), 0, callSignature.Parameters.Count, null, DefaultLineInfo, GetFunctionStatistic());
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestModuleSelectorExpression()
        {
            ModuleSelectorExpression node = new ModuleSelectorExpression(GetExpression1(), GetFullSymbol(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestPropertyAssignment()
        {
            PropertyAssignment node = new PropertyAssignment(GetStringId(), GetExpression1(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestResolvedSelectorExpression()
        {
            var reference = GetLocationBasedSymbolReference();
            ResolvedSelectorExpression node = new ResolvedSelectorExpression(GetExpression1(), reference, reference.Name, DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestSelectorExpression()
        {
            SelectorExpression node = new SelectorExpression(GetExpression1(), GetSymbolAtom(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestUnaryExpression()
        {
            foreach (UnaryOperator op in System.Enum.GetValues(typeof(UnaryOperator)))
            {
                UnaryExpression node = new UnaryExpression(op, GetExpression1(), DefaultLineInfo);
                var node2 = CheckSerializationRoundTrip(node);
                Assert.Equal(op, node2.OperatorKind);
            }
        }

        [Fact]
        public void TestCoerceQualifierTypeExpression()
        {
            CoerceQualifierTypeExpression node = new CoerceQualifierTypeExpression(GetExpression1(), m_qualifierSpaceId, false, DefaultLineInfo, UniversalLocation.FromLineInfo(DefaultLineInfo, GetAbsolutePath(), m_pathTable));
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestQualifierReferenceExpression()
        {
            QualifierReferenceExpression node = new QualifierReferenceExpression(DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestWithQualifierExpression()
        {
            WithQualifierExpression node = new WithQualifierExpression(GetExpression1(), GetExpression2(), m_qualifierSpaceId, m_qualifierSpaceId, DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestFullNameBasedSymbolReference()
        {
            FullNameBasedSymbolReference node = new FullNameBasedSymbolReference(GetFullSymbol(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestLocalReferenceExpression()
        {
            LocalReferenceExpression node = new LocalReferenceExpression(GetSymbolAtom(), 0, DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestLocationBasedSymbolReference()
        {
            LocationBasedSymbolReference node = GetLocationBasedSymbolReference();
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestModuleIdExpression()
        {
            ModuleIdExpression node = new ModuleIdExpression(GetFullSymbol(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestModuleReferenceExpression()
        {
            ModuleReferenceExpression node = new ModuleReferenceExpression(GetFullSymbol(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestNameBasedSymbolReference()
        {
            NameBasedSymbolReference node = new NameBasedSymbolReference(GetSymbolAtom(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestConfigurationDeclaration()
        {
            ConfigurationDeclaration node = new ConfigurationDeclaration(GetSymbolAtom(), GetExpression1(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestEnumDeclaration()
        {
            EnumDeclaration node = new EnumDeclaration(
                GetSymbolAtom(),
                new List<EnumMemberDeclaration> {new EnumMemberDeclaration(GetSymbolAtom(), GetExpression1(), Declaration.DeclarationFlags.Export, GetExpressionList(), DefaultLineInfo), new EnumMemberDeclaration(GetSymbolAtom(), GetExpression2(), Declaration.DeclarationFlags.Export, GetExpressionList(), DefaultLineInfo)},
                GetExpressionList(),
                Declaration.DeclarationFlags.Export,
                DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestEnumMemberDeclaration()
        {
            EnumMemberDeclaration node = new EnumMemberDeclaration(GetSymbolAtom(), GetExpression1(), Declaration.DeclarationFlags.Export, GetExpressionList(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestExportDeclaration()
        {
            ExportDeclaration node = new ExportDeclaration(GetImportOrExportClause(), GetExpression1(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestFunctionDeclaration()
        {
            var symbolAtom = SymbolAtom.Create(m_stringTable, "y");
            FunctionDeclaration node = new FunctionDeclaration(
                new List<SymbolAtom>
                {
                    GetSymbolAtom(),
                    symbolAtom
                },
                GetSymbolAtom(),
                GetCallSignature(),
                GetStatement1(),
                captures: 0,
                locals: 0,
                modifier: Declaration.DeclarationFlags.Export,
                location: DefaultLineInfo,
                stringTable: StringTable);
            var deserialized = CheckSerializationRoundTrip(node);
            Assert.NotNull(deserialized.Statistic);
        }

        [Fact]
        public void TestImportDeclaration()
        {
            ImportDeclaration node = new ImportDeclaration(GetImportOrExportClause(), GetExpression1(), GetExpression2(), GetExpressionList(), Declaration.DeclarationFlags.Export, DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestImportOrExportClause()
        {
            ImportOrExportClause node = GetImportOrExportClause();
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestImportOrExportModuleSpecifier()
        {
            ImportOrExportModuleSpecifier node = new ImportOrExportModuleSpecifier(GetFullSymbol(), GetFullSymbol(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestImportOrExportVarSpecifier()
        {
            ImportOrExportVarSpecifier node = new ImportOrExportVarSpecifier(GetSymbolAtom(), GetSymbolAtom(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestInterfaceDeclaration()
        {
            var namedTypeReference = new NamedTypeReference(GetSymbolAtom(), GetTypeList(), DefaultLineInfo);
            InterfaceDeclaration node = new InterfaceDeclaration(GetSymbolAtom(), GetTypeParameterList(), new List<NamedTypeReference> {namedTypeReference, namedTypeReference}, GetExpressionList(), new ObjectType(GetCallSignatureList(), DefaultLineInfo), Declaration.DeclarationFlags.Export, DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestModuleDeclaration()
        {
            ModuleDeclaration node = new ModuleDeclaration(GetSymbolAtom(), new List<Declaration> {GetVarDeclaration()}, Declaration.DeclarationFlags.Export, DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestNamedImportsOrExports()
        {
            NamedImportsOrExports node = new NamedImportsOrExports(new List<ImportOrExportSpecifier> {new ImportOrExportVarSpecifier(GetSymbolAtom(), GetSymbolAtom(), DefaultLineInfo)}, DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestNamespaceAsVarImport()
        {
            NamespaceAsVarImport node = new NamespaceAsVarImport(GetSymbolAtom(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestPackageDeclaration()
        {
            PackageDeclaration node = new PackageDeclaration(GetSymbolAtom(), GetExpressionList(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestQualifierSpaceDeclaration()
        {
            QualifierSpaceDeclaration node = new QualifierSpaceDeclaration(GetSymbolAtom(), GetExpression1(), DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestTypeAliasDeclaration()
        {
            TypeAliasDeclaration node = new TypeAliasDeclaration(GetSymbolAtom(), GetTypeParameterList(), PrimitiveType.NumberType, Declaration.DeclarationFlags.Export, DefaultLineInfo);
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestVarDeclaration()
        {
            VarDeclaration node = GetVarDeclaration();
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestClosure()
        {
            CallSignature callSignature = GetCallSignature();
            Closure node = new Closure(
                GetTypeOrNamespaceModuleLiteral(),
                new FunctionLikeExpression(
                    GetSymbolAtom(),
                    callSignature,
                    GetStatement1(),
                    5,
                    callSignature.Parameters.Count,
                    null,
                    DefaultLineInfo,
                    GetFunctionStatistic()),
                EvaluationStackFrame.UnsafeFrom(new object[] {1, "hello"}.Select(v => EvaluationResult.Create(v)).ToArray()));
            Assert.Throws<NotImplementedException>(() => CheckSerializationRoundTrip(node));
        }

        [Fact]
        public void TestCallableMember0Int()
        {
            CallableMember0<int> node = GetCallableMember0();
            Assert.Throws<NotImplementedException>(() => CheckSerializationRoundTrip(node));
        }

        [Fact]
        public void TestCallableMember1Int()
        {
            CallableMember1<int> node = new CallableMember1<int>(FunctionStatistic.Empty, GetSymbolAtom(), null, 1, true);
            Assert.Throws<NotImplementedException>(() => CheckSerializationRoundTrip(node));
        }

        [Fact]
        public void TestCallableMember2Int()
        {
            CallableMember2<int> node = new CallableMember2<int>(GetFunctionStatistic(), GetSymbolAtom(), null, 1, true);
            Assert.Throws<NotImplementedException>(() => CheckSerializationRoundTrip(node));
        }

        [Fact]
        public void TestCallableMemberNInt()
        {
            CallableMemberN<int> node = new CallableMemberN<int>(GetFunctionStatistic(), GetSymbolAtom(), null, 1, 1, true);
            Assert.Throws<NotImplementedException>(() => CheckSerializationRoundTrip(node));
        }

        [Fact]
        public void TestCallableValueInt()
        {
            CallableValue<int> node = new CallableValue<int>(1, GetCallableMember0());
            Assert.Throws<NotImplementedException>(() => CheckSerializationRoundTrip(node));
        }

        [Fact]
        public void TestArrayLiteral()
        {
            ArrayLiteral node = ArrayLiteral.Create(new[] { GetExpression1() }, DefaultLineInfo, GetAbsolutePath());
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestObjectLiteral0()
        {
            ObjectLiteral0 node = new ObjectLiteral0(DefaultLineInfo, GetAbsolutePath());
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestObjectLiteralN()
        {
            var namedValue0 = NamedValue.Create(1, 1);
            var namedValue1 = NamedValue.Create(2, 2);
            var namedValue2 = NamedValue.Create(3, 3);
            var namedValue3 = NamedValue.Create(4, 4);
            var namedValue4 = NamedValue.Create(5, 5);
            var namedValue5 = NamedValue.Create(6, 6);
            var namedValueList = new List<NamedValue> { namedValue0, namedValue1, namedValue2, namedValue3, namedValue4, namedValue5 };

            ObjectLiteralN node = (ObjectLiteralN)ObjectLiteralN.Create(namedValueList, DefaultLineInfo, GetAbsolutePath());
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestObjectLiteralSlimNamedValue()
        {
            var namedValue = NamedValue.Create(1, 1);
            ObjectLiteralSlim<StructArray1<NamedValue>> node = new ObjectLiteralSlim<StructArray1<NamedValue>>(new StructArray1<NamedValue>(namedValue), DefaultLineInfo, GetAbsolutePath());
            CheckSerializationRoundTrip(node);
        }

        [Fact]
        public void TestFileModuleLiteral()
        {
            FileModuleLiteral node = GetFileModuleLiteral();
            TestFileModuleLiteralOrAbove(node, typeof(FileModuleLiteral));
        }

        [Fact(Skip = "Global modules are not to be (de)serialized")]
        public void TestGlobalModuleLiteral()
        {
            GlobalModuleLiteral node = GlobalModuleLiteral;

            // Assert.Throws<System.Diagnostics.Contracts.ContractException>(() => CheckSerializationRoundTrip(node));
        }

        [Fact]
        public void TestResolvedFileModuleLiteral()
        {
            FileModuleLiteral node = new FileModuleLiteral(GetAbsolutePath(), GetQualifierValue(), GlobalModuleLiteral, GetPackage(), m_moduleRegistry, GetLineMap());

            node.AddResolvedEntry(GetFullSymbol(), new ResolvedEntry(GetFullSymbol(), GetExpression1()));

            TestFileModuleLiteralOrAbove(node, typeof(FileModuleLiteral));
        }

        private void TestFileModuleLiteralOrAbove(FileModuleLiteral node, Type type)
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new BuildXLWriter(false, ms, true, false))
                using (var reader = new BuildXLReader(true, ms, true))
                {
                    // Serialize
                    node.Serialize(writer);

                    // Copy for validation
                    byte[] firstPass = ms.ToArray();

                    // Reset the stream pointer to the start for deserializing
                    ms.Position = 0;

                    // Deserialize
                    DeserializationContext context = new DeserializationContext(null, reader, m_pathTable, node.LineMap);
                    var node2 = FileModuleLiteral.Read(reader, context.PathTable, GlobalModuleLiteral, m_moduleRegistry);

                    Assert.NotNull(node2);

                    // Reset the stream pointer to the start for serializing
                    ms.Position = 0;

                    // Reserialize
                    node2.Serialize(writer);

                    // Copy for validation
                    byte[] secondPass = ms.ToArray();

                    // Compare byte arrays
                    Assert.Equal(firstPass, secondPass);

                    // Compare ASTs
                    ConstructorTests.ValidateEqual(null, type, node, node2, nameof(type), null);
                }
            }
        }

        [Fact(Skip = "Namespace modules only exist at runtime, and do not need to be serializable")]
        public void TestTypeOrNamespaceModuleLiteral()
        {
            TypeOrNamespaceModuleLiteral node = GetTypeOrNamespaceModuleLiteral();
            CheckSerializationRoundTrip(node);
        }

        /// <summary>
        /// Validate Node1 -> ByteArray1 -> Node2 -> ByteArray2
        /// </summary>
        private T CheckSerializationRoundTrip<T>(T node)
            where T : Node
        {
            byte[] firstPass;
            T node2;

            using (var writerStream = new MemoryStream())
            using (var writer = new BuildXLWriter(false, writerStream, true, false))
            {
                // Serialize (Node1 -> ByteArray1)
                node.Serialize(writer);

                // Copy for validation
                firstPass = writerStream.ToArray();
            }

            using (var readerStream = new MemoryStream(firstPass))
            using (var reader = new BuildXLReader(true, readerStream, true))
            {
                // Deserialize (ByteArray1 -> Node2)
                DeserializationContext context = new DeserializationContext(null, reader, m_pathTable, GetLineMap());
                node2 = Node.Read<T>(context);
            }

            if (node.Kind == SyntaxKind.None)
            {
                Assert.Null(node2);
                return null;
            }
            else
            {
                Assert.NotNull(node2);

                byte[] secondPass;

                using (var writerStream2 = new MemoryStream())
                using (var writer2 = new BuildXLWriter(false, writerStream2, true, false))
                {
                    // Reserialize (Node2 -> ByteArray2)
                    node2.Serialize(writer2);

                    // Copy for validation
                    secondPass = writerStream2.ToArray();
                }

                // Compare byte arrays (ByteArray1 and ByteArray2)
                XAssert.AreArraysEqual(firstPass, secondPass, true);

                // Compare ASTs (Node1 and Node2)
                ConstructorTests.ValidateEqual(null, typeof(T), node, node2, string.Empty, null);

                return node2;
            }
        }
    }
}
