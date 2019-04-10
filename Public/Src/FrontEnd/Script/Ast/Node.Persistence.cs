// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.FrontEnd.Script.Expressions.CompositeExpressions;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.Statements;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Mutable;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script
{
    public partial class Node
    {
        /// <nodoc />
        [CanBeNull]
        public static Expression ReadExpression(DeserializationContext context) => Read<Expression>(context);

        /// <nodoc />
        [CanBeNull]
        public static Types.Type ReadType(DeserializationContext context) => Read<Types.Type>(context);

        /// <nodoc />
        [CanBeNull]
        public static TNode Read<TNode>(DeserializationContext context) where TNode : Node
        {
            return (TNode)Read(context);
        }

        /// <nodoc />
        [CanBeNull]
        [SuppressMessage("Microsoft.Maintainability", "CA1505", Justification = "Method is still maintainable because it is comprised of one switch statement with a single line of code for each case.")]
        public static Node Read(DeserializationContext context)
        {
            var reader = context.Reader;
            var kind = (SyntaxKind)reader.ReadInt32Compact();

            // Empty node is special, because if the node is missing the only information that is stored in the stream is Kind and no location.
            if (kind == SyntaxKind.None)
            {
                return null;
            }

            var location = LineInfo.Read(context.LineMap, reader);

            switch (kind)
            {
                case SyntaxKind.SourceFile:
                    return new SourceFile(context, location);
                case SyntaxKind.EnumMember:
                    return new EnumMemberDeclaration(context, location);
                case SyntaxKind.Parameter:
                    return new Parameter(context, location);
                case SyntaxKind.TypeParameter:
                    return new TypeParameter(context, location);
                case SyntaxKind.ApplyExpression:
                    return new NonGenericApplyExpression(context, location);
                case SyntaxKind.ApplyExpressionWithTypeArguments:
                    return new ApplyExpressionWithTypeArguments(context, location);
                case SyntaxKind.ArrayExpression:
                    return new ArrayExpression(context, location);
                case SyntaxKind.BinaryExpression:
                    return new BinaryExpression(context, location);
                case SyntaxKind.FullNameBasedSymbolReference:
                    return new FullNameBasedSymbolReference(context, location);
                case SyntaxKind.ModuleReferenceExpression:
                    return new ModuleReferenceExpression(context, location);
                case SyntaxKind.NameBasedSymbolReference:
                    return new NameBasedSymbolReference(context, location);
                case SyntaxKind.LocalReferenceExpression:
                    return new LocalReferenceExpression(context, location);
                case SyntaxKind.LocationBasedSymbolReference:
                    return new LocationBasedSymbolReference(context, location);
                case SyntaxKind.ModuleIdExpression:
                    return new ModuleIdExpression(context, location);
                case SyntaxKind.WithQualifierExpression:
                    return new WithQualifierExpression(context, location);
                case SyntaxKind.QualifierReferenceExpression:
                    return new QualifierReferenceExpression(location);
                case SyntaxKind.CoerceQualifierTypeExpression:
                    return new CoerceQualifierTypeExpression(context, location);
                case SyntaxKind.IndexExpression:
                    return new IndexExpression(context, location);
                case SyntaxKind.IteExpression:
                    return new ConditionalExpression(context, location);
                case SyntaxKind.SwitchExpression:
                    return new SwitchExpression(context, location);
                case SyntaxKind.SwitchExpressionClause:
                    return new SwitchExpressionClause(context, location);
                case SyntaxKind.LambdaExpression:
                    return new FunctionLikeExpression(context, location);
                case SyntaxKind.SelectorExpression:
                    return new SelectorExpression(context, location);
                case SyntaxKind.ResolvedSelectorExpression:
                    return new ResolvedSelectorExpression(context, location);
                case SyntaxKind.ModuleSelectorExpression:
                    return new ModuleSelectorExpression(context, location);
                case SyntaxKind.UnaryExpression:
                    return new UnaryExpression(context, location);
                case SyntaxKind.PropertyAssignment:
                    return new PropertyAssignment(context, location);
                case SyntaxKind.AssignmentExpression:
                    return new AssignmentExpression(context, location);
                case SyntaxKind.IncrementDecrementExpression:
                    return new IncrementDecrementExpression(context, location);
                case SyntaxKind.CastExpression:
                    return new CastExpression(context, location);
                case SyntaxKind.ImportAliasExpression:
                    return new ImportAliasExpression(context, location);
                case SyntaxKind.ModuleToObjectLiteral:
                    return new ModuleToObjectLiteral(context, location);
                case SyntaxKind.PathLiteral:
                    return new PathLiteral(context, location);
                case SyntaxKind.InterpolatedPaths:
                    return new InterpolatedPaths(context, location);
                case SyntaxKind.FileLiteral:
                    return new FileLiteral(context, location);
                case SyntaxKind.FileLiteralExpression:
                    return new FileLiteralExpression(context, location);
                case SyntaxKind.StringLiteralExpression:
                    return new StringLiteralExpression(context, location);
                case SyntaxKind.DirectoryLiteral:
                    return new DirectoryLiteralExpression(context, location);
                case SyntaxKind.PathAtomLiteral:
                    return new PathAtomLiteral(context, location);
                case SyntaxKind.RelativePathLiteral:
                    return new RelativePathLiteral(context, location);
                case SyntaxKind.StringLiteral:
                    return new StringLiteral(reader, location);
                case SyntaxKind.BoolLiteral:
                    return new BoolLiteral(context, location);
                case SyntaxKind.NumberLiteral:
                    return new NumberLiteral(reader, location);
                case SyntaxKind.UndefinedLiteral:
                    return UndefinedLiteral.Instance;
                case SyntaxKind.ResolvedStringLiteral:
                    return new ResolvedStringLiteral(context, location);
                case SyntaxKind.ImportDeclaration:
                    return new ImportDeclaration(context, location);
                case SyntaxKind.ExportDeclaration:
                    return new ExportDeclaration(context, location);
                case SyntaxKind.VarDeclaration:
                    return new VarDeclaration(context, location);
                case SyntaxKind.BlockStatement:
                    return new BlockStatement(context, location);
                case SyntaxKind.BreakStatement:
                    return new BreakStatement(location);
                case SyntaxKind.ContinueStatement:
                    return new ContinueStatement(location);
                case SyntaxKind.CaseClause:
                    return new CaseClause(context, location);
                case SyntaxKind.DefaultClause:
                    return new DefaultClause(context, location);
                case SyntaxKind.ExpressionStatement:
                    return new ExpressionStatement(context, location);
                case SyntaxKind.IfStatement:
                    return new IfStatement(context, location);
                case SyntaxKind.ReturnStatement:
                    return new ReturnStatement(context, location);
                case SyntaxKind.SwitchStatement:
                    return new SwitchStatement(context, location);
                case SyntaxKind.VarStatement:
                    return new VarStatement(context, location);
                case SyntaxKind.ForStatement:
                    return new ForStatement(context, location);
                case SyntaxKind.ForOfStatement:
                    return new ForOfStatement(context, location);
                case SyntaxKind.WhileStatement:
                    return new WhileStatement(context, location);
                case SyntaxKind.ArrayType:
                    return new ArrayType(context, location);
                case SyntaxKind.FunctionType:
                    return new FunctionType(context, location);
                case SyntaxKind.NamedTypeReference:
                    return new NamedTypeReference(context, location);
                case SyntaxKind.ObjectType:
                    return new ObjectType(context, location);
                case SyntaxKind.PredefinedType:
                    return new PrimitiveType(context, location);
                case SyntaxKind.TupleType:
                    return new TupleType(context, location);
                case SyntaxKind.UnionType:
                    return new UnionType(context, location);
                case SyntaxKind.TypeQuery:
                    return new TypeQuery(context, location);
                case SyntaxKind.PropertySignature:
                    return new PropertySignature(context, location);
                case SyntaxKind.CallSignature:
                    return new CallSignature(context, location);
                case SyntaxKind.TypeOrNamespaceModuleLiteral:
                    return TypeOrNamespaceModuleLiteral.Deserialize(context, location);
                case SyntaxKind.ObjectLiteral0:
                case SyntaxKind.ObjectLiteralN:
                case SyntaxKind.ObjectLiteralSlim:
                    return ObjectLiteral.Create(context, location);
                case SyntaxKind.ArrayLiteral:
                    return ArrayLiteral.Create(context, location);
                case SyntaxKind.ArrayLiteralWithSpreads:
                    return new ArrayLiteralWithSpreads(context, location);

                // Serialization is not needed for configurations.
                case SyntaxKind.ConfigurationDeclaration:
                    return new ConfigurationDeclaration(context, location);
                case SyntaxKind.PackageDeclaration:
                    return new PackageDeclaration(context, location);

                // Types are not used at runtime, so we can skip these if their serialization becomes troublesome
                case SyntaxKind.InterfaceDeclaration:
                    return new InterfaceDeclaration(context, location);
                case SyntaxKind.ModuleDeclaration:
                    return new ModuleDeclaration(context, location);
                case SyntaxKind.NamespaceImport:
                    return new NamespaceImport(context, location);
                case SyntaxKind.NamespaceAsVarImport:
                    return new NamespaceAsVarImport(context, location);
                case SyntaxKind.ImportOrExportModuleSpecifier:
                    return new ImportOrExportModuleSpecifier(context, location);
                case SyntaxKind.ImportOrExportVarSpecifier:
                    return new ImportOrExportVarSpecifier(context, location);
                case SyntaxKind.NamedImportsOrExports:
                    return new NamedImportsOrExports(context, location);
                case SyntaxKind.ImportOrExportClause:
                    return new ImportOrExportClause(context, location);
                case SyntaxKind.TypeAliasDeclaration:
                    return new TypeAliasDeclaration(context, location);

                // Enum declarations are represented as TypeOrNamespace instances at runtime
                case SyntaxKind.EnumDeclaration:
                    return new EnumDeclaration(context, location);

                // Enum members are stored as constants.
                case SyntaxKind.EnumMemberDeclaration:
                    return new EnumMemberDeclaration(context, location);

                // LambdaExpressions are serialized instead.
                case SyntaxKind.FunctionDeclaration:
                    return new FunctionDeclaration(context, location);

                // Supported only in V1 and V1 modules are not serializable.
                case SyntaxKind.QualifierSpaceDeclaration:
                    return new QualifierSpaceDeclaration(context, location);

                // Serialized separately
                case SyntaxKind.FileModuleLiteral:

                // Prelude is not serializable
                case SyntaxKind.GlobalModuleLiteral:

                // Closures are not serializable, only LambdaExpressions are
                case SyntaxKind.Closure:
                case SyntaxKind.Function0:
                case SyntaxKind.Function1:
                case SyntaxKind.Function2:
                case SyntaxKind.FunctionN:
                case SyntaxKind.BoundFunction:

                // Not serializable.
                case SyntaxKind.ImportValue:

                // Not serializable.
                case SyntaxKind.MergeModuleValue:
                case SyntaxKind.ImportExpression:
                case SyntaxKind.QualifiedLocationBasedSymbolReference:
                case SyntaxKind.ObjectLiteralOverride:

                // Added for completeness
                case SyntaxKind.None:
                    break;

                // We will hit this exception if we are missing one value from the enum
                default:
                    throw new BuildXLException(I($"Unable to deserialize syntax kind {kind.ToString()}"));
            }

            string message = I($"The node {kind} is not deserializable yet.");
            throw new InvalidOperationException(message);
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011")]
        protected static Declaration.DeclarationFlags ReadModifier(BuildXLReader reader)
        {
            return (Declaration.DeclarationFlags)reader.ReadByte();
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011")]
        protected static void WriteModifier(Declaration.DeclarationFlags modifiers, BuildXLWriter writer)
        {
            writer.Write((byte)modifiers);
        }

        /// <nodoc />
        protected static Expression[] ReadExpressions(DeserializationContext context)
        {
            return ReadArrayOf<Expression>(context);
        }

        /// <nodoc />
        protected static void WriteExpressions(IReadOnlyList<Expression> decorators, BuildXLWriter writer)
        {
            WriteArrayOf(decorators, writer);
        }

        /// <nodoc />
        protected static SymbolAtom ReadSymbolAtom(DeserializationContext context)
        {
            return context.Reader.ReadSymbolAtom();
        }

        /// <nodoc />
        protected static void WriteSymbolAtom(SymbolAtom atom, BuildXLWriter writer)
        {
            writer.Write(atom);
        }

        /// <nodoc />
        protected static FullSymbol ReadFullSymbol(DeserializationContext context)
        {
            return context.Reader.ReadFullSymbol();
        }

        /// <nodoc />
        protected static void WriteFullSymbol(FullSymbol fullSymbol, BuildXLWriter writer)
        {
            writer.Write(fullSymbol);
        }

        /// <nodoc />
        public static TNode[] ReadArrayOf<TNode>(DeserializationContext context) where TNode : Node
        {
            int count = context.Reader.ReadInt32Compact();
            if (count == 0)
            {
                return CollectionUtilities.EmptyArray<TNode>();
            }

            var result = new TNode[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = (TNode)Read(context);
            }

            return result;
        }

        /// <nodoc />
        public static void WriteArrayOf<TNode>(IReadOnlyList<TNode> elements, BuildXLWriter writer) where TNode : Node
        {
            writer.WriteCompact(elements.Count);
            foreach (var d in elements.AsStructEnumerable())
            {
                d.Serialize(writer);
            }
        }

        /// <nodoc />
        public virtual void Serialize(BuildXLWriter writer)
        {
            writer.WriteCompact((int)Kind);
            Location.Write(writer);

            DoSerialize(writer);
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011")]
        protected static Package ReadPackage(BuildXLReader reader, PathTable pathTable)
        {
            var id = ReadPackageId(reader);
            var path = reader.ReadAbsolutePath();
            var descriptor = new PackageDescriptor()
            {
                Name = id.Name.ToString(pathTable.StringTable),
            };

            return Package.Create(id, path, descriptor);
        }

        /// <nodoc />
        protected static void WritePackage(Package package, BuildXLWriter writer)
        {
            WritePackageId(package.Id, writer);
            writer.Write(package.Path);
        }

        /// <nodoc />
        protected static PackageId ReadPackageId(BuildXLReader reader)
        {
            var name = reader.ReadStringId();
            var minVersion = reader.ReadStringId();
            var maxVersion = reader.ReadStringId();

            if (minVersion.IsValid && maxVersion.IsValid)
            {
                return PackageId.Create(name, PackageVersion.Create(minVersion, maxVersion));
            }

            return PackageId.Create(name);
        }

        /// <nodoc />
        protected static void WritePackageId(PackageId packageId, BuildXLWriter writer)
        {
            writer.Write(packageId.Name);
            writer.Write(packageId.Version.MinVersion);
            writer.Write(packageId.Version.MaxVersion);
        }

        /// <nodoc />
        protected static FilePosition ReadFilePosition(BuildXLReader reader)
        {
            int position = reader.ReadInt32Compact();
            var path = reader.ReadAbsolutePath();
            return new FilePosition(position, path);
        }

        /// <nodoc />
        protected static void WriteFilePosition(FilePosition position, BuildXLWriter writer)
        {
            writer.WriteCompact(position.Position);
            writer.Write(position.Path);
        }

        /// <nodoc />
        protected virtual void DoSerialize(BuildXLWriter writer)
        {
            throw new NotImplementedException(I($"The node {Kind} is not serializable."));
        }

        /// <summary>
        /// Helper method that serializes a given node or writes 'null node' if the <paramref name="node"/> is null.
        /// </summary>
        public static void Serialize([CanBeNull]Node node, BuildXLWriter writer)
        {
            (node ?? NullNode.Instance).Serialize(writer);
        }
    }

    /// <summary>
    /// Fake node used for serializing missing nodes.
    /// </summary>
    public sealed class NullNode : Node
    {
        /// <nodoc />
        public static readonly NullNode Instance = new NullNode();

        /// <nodoc />
        public NullNode()
            : base(default(LineInfo))
        {
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return default(EvaluationResult);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.None;

        /// <inheritdoc />
        public override void Serialize(BuildXLWriter writer)
        {
            writer.Write((byte)Kind);
        }

        /// <inheritdoc />
        public override string ToDebugString() => string.Empty;
    }
}
