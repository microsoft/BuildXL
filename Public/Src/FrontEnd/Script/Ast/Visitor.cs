// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Expressions.CompositeExpressions;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.Statements;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;

namespace BuildXL.FrontEnd.Script
{
    /// <nodoc />
    public abstract class Visitor
    {
        /// <nodoc />
        public virtual void Visit(NameBasedSymbolReference nameBasedSymbolReference)
        {
        }

        /// <nodoc />
        public virtual void Visit(LocationBasedSymbolReference locationBasedSymbolReference)
        {
        }

        /// <nodoc />
        public virtual void Visit(ModuleIdExpression idExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(UnaryExpression unaryExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(BinaryExpression binaryExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(ConditionalExpression conditionalExpression)
        {
        }
    
        /// <nodoc />
        public virtual void Visit(SwitchExpression switchExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(SwitchExpressionClause switchExpressionClause)
        {
        }

        /// <nodoc />
        public virtual void Visit(SelectorExpressionBase selectorExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(ModuleSelectorExpression moduleSelectorExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(LocalReferenceExpression localReferenceExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(ArrayExpression arrayExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(IndexExpression indexExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(ApplyExpression applyExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(ApplyExpressionWithTypeArguments applyExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(FunctionLikeExpression lambdaExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(PropertyAssignment propertyAssignment)
        {
        }

        /// <nodoc />
        public virtual void Visit(PathLiteral pathLiteral)
        {
        }

        /// <nodoc />
        public virtual void Visit(FileLiteral fileLiteral)
        {
        }

        /// <nodoc />
        public virtual void Visit(FileLiteralExpression fileLiteralExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(StringLiteralExpression expression)
        {
        }

        /// <nodoc />
        public virtual void Visit(DirectoryLiteralExpression pathLiteral)
        {
        }

        /// <nodoc />
        public virtual void Visit(ResolvedStringLiteral resolvedStringLiteral)
        {
        }

        /// <nodoc />
        public virtual void Visit(PathAtomLiteral pathAtomLiteral)
        {
        }

        /// <nodoc />
        public virtual void Visit(RelativePathLiteral relativePathLiteral)
        {
        }

        /// <nodoc />
        public virtual void Visit(StringLiteral stringLiteral)
        {
        }

        /// <nodoc />
        public virtual void Visit(BoolLiteral boolLiteral)
        {
        }

        /// <nodoc />
        public virtual void Visit(UndefinedLiteral undefinedLiteral)
        {
        }

        /// <nodoc />
        public virtual void Visit(NumberLiteral numberLiteral)
        {
        }

        /// <nodoc />
        public virtual void Visit(AssignmentExpression assignmentExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(IncrementDecrementExpression incrementDecrementExpression)
        {
        }

        /// <nodoc />
        public virtual void Visit(CastExpression castExpression)
        {
        }

        ////////////// Types.

        /// <nodoc />
        public virtual void Visit(PrimitiveType primitiveType)
        {
        }

        /// <nodoc />
        public virtual void Visit(NamedTypeReference namedTypeReference)
        {
        }

        /// <nodoc />
        public virtual void Visit(ArrayType arrayType)
        {
        }

        /// <nodoc />
        public virtual void Visit(TupleType tupleType)
        {
        }

        /// <nodoc />
        public virtual void Visit(UnionType unionType)
        {
        }

        /// <nodoc />
        public virtual void Visit(TypeParameter typeParameter)
        {
        }

        /// <nodoc />
        public virtual void Visit(Parameter parameter)
        {
        }

        /// <nodoc />
        public virtual void Visit(FunctionType functionType)
        {
        }

        /// <nodoc />
        public virtual void Visit(ObjectType objectType)
        {
        }

        /// <nodoc />
        public virtual void Visit(TypeQuery typeQuery)
        {
        }

        ////////////// Statements.

        /// <nodoc />
        public virtual void Visit(VarStatement varStatement)
        {
        }

        /// <nodoc />
        public virtual void Visit(BlockStatement blockStatement)
        {
        }

        /// <nodoc />
        public virtual void Visit(IfStatement ifStatement)
        {
        }

        /// <nodoc />
        public virtual void Visit(ReturnStatement returnStatement)
        {
        }

        /// <nodoc />
        public virtual void Visit(BreakStatement breakStatement)
        {
        }

        /// <nodoc />
        public virtual void Visit(ContinueStatement continueStatement)
        {
        }

        /// <nodoc />
        public virtual void Visit(SwitchStatement switchStatement)
        {
        }

        /// <nodoc />
        public virtual void Visit(CaseClause caseClause)
        {
        }

        /// <nodoc />
        public virtual void Visit(DefaultClause defaultClause)
        {
        }

        /// <nodoc />
        public virtual void Visit(ExpressionStatement expressionStatement)
        {
        }

        /// <nodoc />
        public virtual void Visit(ForStatement forStatement)
        {
        }

        /// <nodoc />
        public virtual void Visit(ForOfStatement forOfStatement)
        {
        }

        /// <nodoc />
        public virtual void Visit(WhileStatement whileStatement)
        {
        }

        ////////////// Declaration.

        /// <nodoc />
        public virtual void Visit(VarDeclaration varDeclaration)
        {
        }

        /// <nodoc />
        public virtual void Visit(FunctionDeclaration functionDeclaration)
        {
        }

        /// <nodoc />
        public virtual void Visit(ConfigurationDeclaration configurationDeclaration)
        {
        }

        /// <nodoc />
        public virtual void Visit(PackageDeclaration packageDeclaration)
        {
        }

        /// <nodoc />
        public virtual void Visit(QualifierSpaceDeclaration qualifierSpaceDeclaration)
        {
        }

        /// <nodoc />
        public virtual void Visit(ImportDeclaration importDeclaration)
        {
        }

        /// <nodoc />
        public virtual void Visit(ExportDeclaration exportDeclaration)
        {
        }

        /// <nodoc />
        public virtual void Visit(NamespaceImport namespaceImport)
        {
        }

        /// <nodoc />
        public virtual void Visit(NamespaceAsVarImport namespaceAsVarImport)
        {
        }

        /// <nodoc />
        public virtual void Visit(ImportOrExportModuleSpecifier importOrExportModuleSpecifier)
        {
        }

        /// <nodoc />
        public virtual void Visit(ImportOrExportVarSpecifier importOrExportVarSpecifier)
        {
        }

        /// <nodoc />
        public virtual void Visit(NamedImportsOrExports namedImportsOrExports)
        {
        }

        /// <nodoc />
        public virtual void Visit(ImportOrExportClause importOrExportClause)
        {
        }

        /// <nodoc />
        public virtual void Visit(InterfaceDeclaration interfaceDeclaration)
        {
        }

        /// <nodoc />
        public virtual void Visit(EnumDeclaration enumDeclaration)
        {
        }

        /// <nodoc />
        public virtual void Visit(ModuleDeclaration moduleDeclaration)
        {
        }

        /// <nodoc />
        public virtual void Visit(EnumMemberDeclaration enumMemberDeclaration)
        {
        }

        /// <nodoc />
        public virtual void Visit(TypeAliasDeclaration typeAliasDeclaration)
        {
        }

        /// <nodoc />
        public virtual void Visit(InterpolatedPaths interpolatedPaths)
        {
        }

        /// <nodoc />
        public virtual void Visit(PropertySignature propertySignature)
        {
        }

        /// <nodoc />
        public virtual void Visit(CallSignature callSignature)
        {
        }

        /// <nodoc />
        public virtual void Visit(ObjectLiteralN value)
        {
        }

        /// <nodoc />
        public virtual void Visit(ObjectLiteral0 value)
        {
        }

        /// <nodoc />
        public virtual void Visit(ObjectLiteralSlim value)
        {
        }

        /// <nodoc />
        public virtual void Visit(ArrayLiteral value)
        {
        }

        /// <nodoc />
        public virtual void Visit(SourceFile sourceFile)
        {
        }
    }
}
