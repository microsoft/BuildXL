// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Collections;
using TypeScript.Net.Extensions;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;
using static TypeScript.Net.Scanning.Scanner;
using static TypeScript.Net.Types.NodeUtilities;

namespace TypeScript.Net.TypeChecking
{
    partial class Checker
    {
        internal class SymbolDisplayBuilder : ISymbolDisplayBuilder
        {
            private readonly Checker m_checker;

            public SymbolDisplayBuilder(Checker checker)
            {
                m_checker = checker;
            }

            private static void WriteKeyword(ISymbolWriter writer, SyntaxKind kind)
            {
                writer.WriteKeyword(TokenToString(kind));
            }

            private void WriteTypeOfSymbol(ISymbolWriter writer, IObjectType type, TypeFormatFlags typeFormatFlags, INode enclosingDeclaration = null)
            {
                WriteKeyword(writer, SyntaxKind.TypeOfKeyword);
                WriteSpace(writer);
                BuildSymbolDisplay(type.Symbol, writer, enclosingDeclaration, SymbolFlags.Value, SymbolFormatFlags.None, typeFormatFlags);
            }

            private static void WritePunctuation(ISymbolWriter writer, SyntaxKind kind)
            {
                writer.WritePunctuation(TokenToString(kind));
            }

            private static void WriteSpace(ISymbolWriter writer)
            {
                writer.WriteSpace(" ");
            }

            private static string GetNameOfSymbol(ISymbol symbol)
            {
                // DScript-specific: injected nodes don't count for computing user-facing names.
                if (symbol.DeclarationList.Where(declaration => !declaration.IsInjectedForDScript()).ToList().Count > 0)
                {
                    var declaration = symbol.DeclarationList[0];
                    if (declaration.Name != null)
                    {
                        return DeclarationNameToString(declaration.Name);
                    }

                    switch (declaration.Kind)
                    {
                        case SyntaxKind.ClassExpression:
                            return "(Anonymous class)";
                        case SyntaxKind.FunctionExpression:
                        case SyntaxKind.ArrowFunction:
                            return "(Anonymous function)";
                    }
                }

                return symbol.Name;
            }

            /// <summary>
            /// Writes only the name of the symbol out to the writer. Uses the original source text
            /// for the name of the symbol if it is available to match how the user inputted the name.
            /// </summary>
            private static void AppendSymbolNameOnly(ISymbol symbol, ISymbolWriter writer)
            {
                writer.WriteSymbol(GetNameOfSymbol(symbol), symbol);
            }

            /// <summary>
            /// Enclosing declaration is optional when we don't want to get qualified name in the enclosing declaration scope
            /// Meaning needs to be specified if the enclosing declaration is given
            /// </summary>
            private void BuildSymbolDisplay(ISymbol inputSymbol, ISymbolWriter writer, INode enclosingDeclaration = null, SymbolFlags inputMeaning = SymbolFlags.None, SymbolFormatFlags flags = SymbolFormatFlags.None, TypeFormatFlags typeFlags = TypeFormatFlags.None)
            {
                Contract.Assert(inputSymbol != null);
                Contract.Assert(writer != null);

                // HINT: To simplify migration we're using "local" functions via delegates.
                // This code could be changed in the future, once C# would have local functions.
                Action<ISymbol, SymbolFlags> walkSymbol = null;

                ISymbol parentSymbol = null;
                Action<ISymbol> appendParentTypeArgumentsAndSymbolName = null;

                appendParentTypeArgumentsAndSymbolName = (ISymbol symbol) =>
                {
                    if (parentSymbol != null)
                    {
                        // Write type arguments of instantiated class/interface here
                        if ((flags & SymbolFormatFlags.WriteTypeParametersOrArguments) != SymbolFormatFlags.None)
                        {
                            if ((symbol.Flags & SymbolFlags.Instantiated) != SymbolFlags.None)
                            {
                                // TODO: check types to avoid redundant ToArray call.
                                BuildDisplayForTypeArgumentsAndDelimiters(
                                    m_checker.GetTypeParametersOfClassOrInterface(parentSymbol),
                                    ((ITransientSymbol)symbol).Mapper, writer, enclosingDeclaration);
                            }
                            else
                            {
                                BuildTypeParameterDisplayFromSymbol(parentSymbol, writer, enclosingDeclaration);
                            }
                        }

                        WritePunctuation(writer, SyntaxKind.DotToken);
                    }

                    parentSymbol = symbol;
                    AppendSymbolNameOnly(symbol, writer);
                };

                // Let the writer know we just wrote out a symbol.  The declaration emitter writer uses
                // this to determine if an import it has previously seen (and not written out) needs
                // to be written to the file once the walk of the tree is complete.
                //
                // NOTE(cyrusn): This approach feels somewhat unfortunate.  A simple pass over the tree
                // up front (for example, during checking) could determine if we need to emit the imports
                // and we could then access that data during declaration emit.
                writer.TrackSymbol(inputSymbol, enclosingDeclaration, inputMeaning);

                walkSymbol = (ISymbol symbol, SymbolFlags meaning) =>
                {
                    if (symbol != null)
                    {
                        var accessibleSymbolChain = m_checker.GetAccessibleSymbolChain(symbol, enclosingDeclaration, meaning, (flags & SymbolFormatFlags.UseOnlyExternalAliasing) != SymbolFormatFlags.None);

                        if (accessibleSymbolChain == null ||
                            m_checker.NeedsQualification(accessibleSymbolChain[0], enclosingDeclaration, accessibleSymbolChain.Count == 1 ? meaning : GetQualifiedLeftMeaning(meaning)))
                        {
                            // Go up and add our parent.
                            walkSymbol(
                                    m_checker.GetParentOfSymbol(accessibleSymbolChain != null ? accessibleSymbolChain[0] : symbol),
                                    GetQualifiedLeftMeaning(meaning));
                        }

                        if (accessibleSymbolChain != null)
                        {
                            foreach (var accessibleSymbol in accessibleSymbolChain)
                            {
                                appendParentTypeArgumentsAndSymbolName(accessibleSymbol);
                            }
                        }
                        else
                        {
                            // If we didn't find accessible symbol chain for this symbol, break if this is external module
                            if (parentSymbol == null && symbol.DeclarationList.Any(n => HasExternalModuleSymbol(n)))
                            {
                                return;
                            }

                            // if this is anonymous type break
                            if ((symbol.Flags & SymbolFlags.TypeLiteral) != SymbolFlags.None || (symbol.Flags & SymbolFlags.ObjectLiteral) != SymbolFlags.None)
                            {
                                return;
                            }

                            appendParentTypeArgumentsAndSymbolName(symbol);
                        }
                    }
                };

                // Get qualified name if the symbol is not a type parameter
                // and there is an enclosing declaration or we specifically
                // asked for it
                var isTypeParameter = inputSymbol.Flags & SymbolFlags.TypeParameter;
                var typeFormatFlag = TypeFormatFlags.UseFullyQualifiedType & typeFlags;
                if (isTypeParameter == SymbolFlags.None && (enclosingDeclaration != null || typeFormatFlag != TypeFormatFlags.None))
                {
                    walkSymbol(inputSymbol, inputMeaning);
                    return;
                }

                appendParentTypeArgumentsAndSymbolName(inputSymbol);
            }

            [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "False negative. BuildTypeDisplay cannot be marked as static becuase its local functions access instance data.")]
            private void BuildTypeDisplay(IType inputType, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags globalFlags, Stack<ISymbol> symbolStack = null)
            {
                var globalFlagsToPass = globalFlags & TypeFormatFlags.WriteOwnNameForAnyLike;
                var inObjectTypeLiteral = false;

                WriteType(inputType, globalFlags);
                return;

                void WriteType(IType type, TypeFormatFlags flags)
                {
                    // Write null/null type as any
                    if ((type.Flags & TypeFlags.Intrinsic) != TypeFlags.None)
                    {
                        if ((type.Flags & TypeFlags.PredicateType) != TypeFlags.None)
                        {
                            BuildTypePredicateDisplay(writer, type.Cast<IPredicateType>().Predicate);
                            BuildTypeDisplay(type.Cast<IPredicateType>().Predicate.Type, writer, enclosingDeclaration, flags, symbolStack);
                        }
                        else
                        {
                            // Special handling for unknown / resolving types, they should show up as any and not unknown or __resolving
                            writer.WriteKeyword((globalFlags & TypeFormatFlags.WriteOwnNameForAnyLike) == TypeFormatFlags.None && IsTypeAny(type)
                                ? "any"
                                : type.Cast<IIntrinsicType>().IntrinsicName);
                        }
                    }
                    else if ((type.Flags & TypeFlags.ThisType) != TypeFlags.None)
                    {
                        if (inObjectTypeLiteral)
                        {
                            writer.ReportInaccessibleThisError();
                        }

                        writer.WriteKeyword("this");
                    }
                    else if ((type.Flags & TypeFlags.Reference) != TypeFlags.None)
                    {
                        WriteTypeReference((ITypeReference)type, flags);
                    }
                    else if ((type.Flags & (TypeFlags.Class | TypeFlags.Interface | TypeFlags.Enum | TypeFlags.TypeParameter)) != TypeFlags.None)
                    {
                        // The specified symbol flags need to be reinterpreted as type flags
                        BuildSymbolDisplay(type.Symbol, writer, enclosingDeclaration, SymbolFlags.Type, SymbolFormatFlags.None, flags);
                    }
                    else if ((type.Flags & TypeFlags.Tuple) != TypeFlags.None)
                    {
                        WriteTupleType((ITupleType)type);
                    }
                    else if ((type.Flags & TypeFlags.UnionOrIntersection) != TypeFlags.None)
                    {
                        WriteUnionOrIntersectionType(type.Cast<IUnionOrIntersectionType>(), flags);
                    }
                    else if ((type.Flags & TypeFlags.Anonymous) != TypeFlags.None)
                    {
                        WriteAnonymousType((IObjectType)type, flags);
                    }
                    else if ((type.Flags & TypeFlags.StringLiteral) != TypeFlags.None)
                    {
                        writer.WriteStringLiteral(I($"\"{TextUtilities.EscapeString(((IStringLiteralType)type).Text)}\""));
                    }
                    else
                    {
                        // Should never get here
                        // { ... }
                        WritePunctuation(writer, SyntaxKind.OpenBraceToken);
                        WriteSpace(writer);
                        WritePunctuation(writer, SyntaxKind.DotDotDotToken);
                        WriteSpace(writer);
                        WritePunctuation(writer, SyntaxKind.CloseBraceToken);
                    }
                }

                void WriteTypeList(IReadOnlyList<IType> types, SyntaxKind delimiter)
                {
                    for (var i = 0; i < types.Count; i++)
                    {
                        if (i > 0)
                        {
                            if (delimiter != SyntaxKind.CommaToken)
                            {
                                WriteSpace(writer);
                            }

                            WritePunctuation(writer, delimiter);
                            WriteSpace(writer);
                        }

                        WriteType(types[i], delimiter == SyntaxKind.CommaToken ? TypeFormatFlags.None : TypeFormatFlags.InElementType);
                    }
                }

                void WriteSymbolTypeReference(ISymbol symbol, IReadOnlyList<IType> typeArguments, int pos, int end, TypeFormatFlags flags)
                {
                    // Unnamed  expressions and arrow functions have reserved names that we don't want to display
                    if ((symbol.Flags & SymbolFlags.Class) != SymbolFlags.None || !IsReservedMemberName(symbol.Name))
                    {
                        BuildSymbolDisplay(symbol, writer, enclosingDeclaration, SymbolFlags.Type, SymbolFormatFlags.None, flags);
                    }

                    if (pos < end)
                    {
                        WritePunctuation(writer, SyntaxKind.LessThanToken);
                        WriteType(typeArguments[pos++], TypeFormatFlags.None);
                        while (pos < end)
                        {
                            WritePunctuation(writer, SyntaxKind.CommaToken);
                            WriteSpace(writer);
                            WriteType(typeArguments[pos++], TypeFormatFlags.None);
                        }

                        WritePunctuation(writer, SyntaxKind.GreaterThanToken);
                    }
                }

                void WriteTypeReference(ITypeReference type, TypeFormatFlags flags)
                {
                    var typeArguments = type.TypeArguments ?? new List<IType>();
                    if (type.Target == m_checker.m_globalArrayType && (flags & TypeFormatFlags.WriteArrayAsGenericType) == TypeFormatFlags.None)
                    {
                        WriteType(typeArguments[0], TypeFormatFlags.InElementType);
                        WritePunctuation(writer, SyntaxKind.OpenBracketToken);
                        WritePunctuation(writer, SyntaxKind.CloseBracketToken);
                    }
                    else
                    {
                        // Write the type reference in the format f<A>.g<B>.C<X, Y> where A and B are type arguments
                        // for outer type parameters, and f and g are the respective declaring containers of those
                        // type parameters.
                        var outerTypeParameters = type.Target.OuterTypeParameters;
                        var i = 0;
                        if (outerTypeParameters != null)
                        {
                            var length = outerTypeParameters.Count;
                            while (i < length)
                            {
                                // Find group of type arguments for type parameters with the same declaring container.
                                var start = i;
                                var parent = m_checker.GetParentSymbolOfTypeParameter(outerTypeParameters[i]);
                                do
                                {
                                    i++;
                                }
                                while (i < length && m_checker.GetParentSymbolOfTypeParameter(outerTypeParameters[i]) == parent);

                                // When type parameters are their own type arguments for the whole group (i.e., we have
                                // the default outer type arguments), we don't show the group.
                                if (!outerTypeParameters.RangeEquals(typeArguments, start, i, EqualityComparer<IType>.Default))
                                {
                                    WriteSymbolTypeReference(parent, typeArguments, start, i, flags);
                                    WritePunctuation(writer, SyntaxKind.DotToken);
                                }
                            }
                        }

                        var typeParameterCount = type.Target.TypeParameters?.Count ?? 0;
                        WriteSymbolTypeReference(type.Symbol, typeArguments, i, typeParameterCount, flags);
                    }
                }

                void WriteTupleType(ITupleType type)
                {
                    WritePunctuation(writer, SyntaxKind.OpenBracketToken);
                    WriteTypeList(type.ElementTypes, SyntaxKind.CommaToken);
                    WritePunctuation(writer, SyntaxKind.CloseBracketToken);
                }

                void WriteUnionOrIntersectionType(IUnionOrIntersectionType type, TypeFormatFlags flags)
                {
                    if ((flags & TypeFormatFlags.InElementType) != TypeFormatFlags.None)
                    {
                        WritePunctuation(writer, SyntaxKind.OpenParenToken);
                    }

                    WriteTypeList(type.Types, (type.Flags & TypeFlags.Union) != TypeFlags.None ? SyntaxKind.BarToken : SyntaxKind.AmpersandToken);
                    if ((flags & TypeFormatFlags.InElementType) != TypeFormatFlags.None)
                    {
                        WritePunctuation(writer, SyntaxKind.CloseParenToken);
                    }
                }

                void WriteAnonymousType(IObjectType type, TypeFormatFlags flags)
                {
                    var symbol = type.Symbol;
                    if (symbol != null)
                    {
                        // Always use 'typeof T' for type of class, enum, and module objects
                        if ((symbol.Flags & (SymbolFlags.Class | SymbolFlags.Enum | SymbolFlags.ValueModule)) != SymbolFlags.None)
                        {
                            WriteTypeofSymbol(type, flags);
                        }
                        else if (ShouldWriteTypeOfFunctionSymbol(symbol, flags))
                        {
                            WriteTypeofSymbol(type, flags);
                        }
                        else if (symbolStack?.Contains(symbol) == true)
                        {
                            // If type is an anonymous type literal in a type alias declaration, use type alias name
                            var typeAlias = m_checker.GetTypeAliasForTypeLiteral(type);
                            if (typeAlias != null)
                            {
                                // The specified symbol flags need to be reinterpreted as type flags
                                BuildSymbolDisplay(typeAlias, writer, enclosingDeclaration, SymbolFlags.Type, SymbolFormatFlags.None, flags);
                            }
                            else
                            {
                                // Recursive usage, use any
                                WriteKeyword(writer, SyntaxKind.AnyKeyword);
                            }
                        }
                        else
                        {
                            // Since instantiations of the same anonymous type have the same symbol, tracking symbols instead
                            // of types allows us to catch circular references to instantiations of the same anonymous type
                            if (symbolStack == null)
                            {
                                symbolStack = new Stack<ISymbol>();
                            }

                            symbolStack.Push(symbol);
                            WriteLiteralType(type, flags);
                            symbolStack.Pop();
                        }
                    }
                    else
                    {
                        // Anonymous types with no symbol are never circular
                        WriteLiteralType(type, flags);
                    }
                }

                void WriteTypeofSymbol(IObjectType type, TypeFormatFlags typeFormatFlags)
                {
                    WriteKeyword(writer, SyntaxKind.TypeOfKeyword);
                    WriteSpace(writer);
                    BuildSymbolDisplay(type.Symbol, writer, enclosingDeclaration, SymbolFlags.Value, SymbolFormatFlags.None, typeFormatFlags);
                }

                string GetIndexerParameterName(IObjectType type, IndexKind indexKind, string fallbackName)
                {
                    var declaration = GetIndexDeclarationOfSymbol(type.Symbol, indexKind);
                    if (declaration == null)
                    {
                        // declaration might not be found if indexer was added from the contextual type.
                        // in this case use fallback name
                        return fallbackName;
                    }

                    Contract.Assert(declaration.Parameters.Length != 0);
                    return DeclarationNameToString(declaration.Parameters[0].Name);
                }

                void WriteLiteralType(IObjectType type, TypeFormatFlags flags)
                {
                    IResolvedType resolved = m_checker.ResolveStructuredTypeMembers(type);

                    if (resolved.Properties.Count == 0 && resolved.StringIndexType == null && resolved.NumberIndexType == null)
                    {
                        if (resolved.CallSignatures.Count == 0 && resolved.ConstructSignatures.Count == 0)
                        {
                            WritePunctuation(writer, SyntaxKind.OpenBraceToken);
                            WritePunctuation(writer, SyntaxKind.CloseBraceToken);
                            return;
                        }

                        if (resolved.CallSignatures.Count == 1 && resolved.ConstructSignatures.Count == 0)
                        {
                            if ((flags & TypeFormatFlags.InElementType) != TypeFormatFlags.None)
                            {
                                WritePunctuation(writer, SyntaxKind.OpenParenToken);
                            }

                            BuildSignatureDisplay(resolved.CallSignatures[0], writer, enclosingDeclaration, globalFlagsToPass | TypeFormatFlags.WriteArrowStyleSignature, /*kind*/ null, symbolStack);
                            if ((flags & TypeFormatFlags.InElementType) != TypeFormatFlags.None)
                            {
                                WritePunctuation(writer, SyntaxKind.CloseParenToken);
                            }

                            return;
                        }

                        if (resolved.ConstructSignatures.Count == 1 && resolved.CallSignatures.Count == 0)
                        {
                            if ((flags & TypeFormatFlags.InElementType) != TypeFormatFlags.None)
                            {
                                WritePunctuation(writer, SyntaxKind.OpenParenToken);
                            }

                            WriteKeyword(writer, SyntaxKind.NewKeyword);
                            WriteSpace(writer);
                            BuildSignatureDisplay(resolved.ConstructSignatures[0], writer, enclosingDeclaration, globalFlagsToPass | TypeFormatFlags.WriteArrowStyleSignature, /*kind*/ null, symbolStack);
                            if ((flags & TypeFormatFlags.InElementType) != TypeFormatFlags.None)
                            {
                                WritePunctuation(writer, SyntaxKind.CloseParenToken);
                            }

                            return;
                        }
                    }

                    var saveInObjectTypeLiteral = inObjectTypeLiteral;
                    inObjectTypeLiteral = true;
                    WritePunctuation(writer, SyntaxKind.OpenBraceToken);
                    writer.WriteLine();
                    writer.IncreaseIndent();
                    foreach (var signature in resolved.CallSignatures.AsStructEnumerable())
                    {
                        BuildSignatureDisplay(signature, writer, enclosingDeclaration, globalFlagsToPass, /*kind*/ null, symbolStack);
                        WritePunctuation(writer, SyntaxKind.SemicolonToken);
                        writer.WriteLine();
                    }

                    foreach (var signature in resolved.ConstructSignatures.AsStructEnumerable())
                    {
                        BuildSignatureDisplay(signature, writer, enclosingDeclaration, globalFlagsToPass, SignatureKind.Construct, symbolStack);
                        WritePunctuation(writer, SyntaxKind.SemicolonToken);
                        writer.WriteLine();
                    }

                    if (resolved.StringIndexType != null)
                    {
                        // [string x]:
                        WritePunctuation(writer, SyntaxKind.OpenBracketToken);
                        writer.WriteParameter(GetIndexerParameterName(resolved, IndexKind.String, /*fallbackName*/"x"));
                        WritePunctuation(writer, SyntaxKind.ColonToken);
                        WriteSpace(writer);
                        WriteKeyword(writer, SyntaxKind.StringKeyword);
                        WritePunctuation(writer, SyntaxKind.CloseBracketToken);
                        WritePunctuation(writer, SyntaxKind.ColonToken);
                        WriteSpace(writer);
                        WriteType(resolved.StringIndexType, TypeFormatFlags.None);
                        WritePunctuation(writer, SyntaxKind.SemicolonToken);
                        writer.WriteLine();
                    }

                    if (resolved.NumberIndexType != null)
                    {
                        // [int x]:
                        WritePunctuation(writer, SyntaxKind.OpenBracketToken);
                        writer.WriteParameter(GetIndexerParameterName(resolved, IndexKind.Number, /*fallbackName*/"x"));
                        WritePunctuation(writer, SyntaxKind.ColonToken);
                        WriteSpace(writer);
                        WriteKeyword(writer, SyntaxKind.NumberKeyword);
                        WritePunctuation(writer, SyntaxKind.CloseBracketToken);
                        WritePunctuation(writer, SyntaxKind.ColonToken);
                        WriteSpace(writer);
                        WriteType(resolved.NumberIndexType, TypeFormatFlags.None);
                        WritePunctuation(writer, SyntaxKind.SemicolonToken);
                        writer.WriteLine();
                    }

                    foreach (var p in resolved.Properties.AsStructEnumerable())
                    {
                        var t = m_checker.GetTypeOfSymbol(p);
                        if ((p.Flags & (SymbolFlags.Function | SymbolFlags.Method)) != SymbolFlags.None && m_checker.GetPropertiesOfObjectType(t).Count == 0)
                        {
                            var signatures = m_checker.GetSignaturesOfType(t, SignatureKind.Call);
                            foreach (var signature in signatures.AsStructEnumerable())
                            {
                                BuildSymbolDisplay(p, writer);
                                if ((p.Flags & SymbolFlags.Optional) != SymbolFlags.None)
                                {
                                    WritePunctuation(writer, SyntaxKind.QuestionToken);
                                }

                                BuildSignatureDisplay(signature, writer, enclosingDeclaration, globalFlagsToPass, /*kind*/ null, symbolStack);
                                WritePunctuation(writer, SyntaxKind.SemicolonToken);
                                writer.WriteLine();
                            }
                        }
                        else
                        {
                            BuildSymbolDisplay(p, writer);
                            if ((p.Flags & SymbolFlags.Optional) != SymbolFlags.None)
                            {
                                WritePunctuation(writer, SyntaxKind.QuestionToken);
                            }

                            WritePunctuation(writer, SyntaxKind.ColonToken);
                            WriteSpace(writer);
                            WriteType(t, TypeFormatFlags.None);
                            WritePunctuation(writer, SyntaxKind.SemicolonToken);
                            writer.WriteLine();
                        }
                    }

                    writer.DecreaseIndent();
                    WritePunctuation(writer, SyntaxKind.CloseBraceToken);
                    inObjectTypeLiteral = saveInObjectTypeLiteral;
                }

                bool ShouldWriteTypeOfFunctionSymbol(ISymbol symbol, TypeFormatFlags flags)
                {
                    var isStaticMethodSymbol = ((symbol.Flags & SymbolFlags.Method) != SymbolFlags.None && // typeof static method
                        symbol.DeclarationList.Any(declaration => (declaration.Flags & NodeFlags.Static) != NodeFlags.None)) == true;

                    var isNonLocalFunctionSymbol = (symbol.Flags & SymbolFlags.Function) != SymbolFlags.None &&
                        (symbol.Parent != null || // is exported  symbol
                            symbol.DeclarationList.Any(declaration =>
                                declaration.Parent.Kind == SyntaxKind.SourceFile || declaration.Parent.Kind == SyntaxKind.ModuleBlock));

                    if (isStaticMethodSymbol || isNonLocalFunctionSymbol)
                    {
                        // typeof is allowed only for static/non local functions
                        return (flags & TypeFormatFlags.UseTypeOfFunction) != TypeFormatFlags.None || // use typeof if format flags specify it
                            symbolStack?.Contains(symbol) == true; // it is type of the symbol uses itself recursively
                    }

                    return false;
                }
            }

            private void BuildTypeParameterDisplayFromSymbol(ISymbol symbol, ISymbolWriter writer, INode enclosingDeclaraiton, TypeFormatFlags flags = TypeFormatFlags.None, Stack<ISymbol> symbolStack = null)
            {
                var targetSymbol = m_checker.GetTargetSymbol(symbol);
                if ((targetSymbol.Flags & SymbolFlags.Class) != SymbolFlags.None ||
                    (targetSymbol.Flags & SymbolFlags.Interface) != SymbolFlags.None ||
                    (targetSymbol.Flags & SymbolFlags.TypeAlias) != SymbolFlags.None)
                {
                    BuildDisplayForTypeParametersAndDelimiters(m_checker.GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol), writer, enclosingDeclaraiton, flags, symbolStack);
                }
            }

            private void BuildTypeParameterDisplay(ITypeParameter tp, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags, Stack<ISymbol> symbolStack)
            {
                AppendSymbolNameOnly(tp.Symbol, writer);
                var varraint = m_checker.GetConstraintOfTypeParameter(tp);
                if (varraint != null)
                {
                    WriteSpace(writer);
                    WriteKeyword(writer, SyntaxKind.ExtendsKeyword);
                    WriteSpace(writer);
                    BuildTypeDisplay(varraint, writer, enclosingDeclaration, flags, symbolStack);
                }
            }

            private void BuildParameterDisplay(ISymbol p, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags, Stack<ISymbol> symbolStack)
            {
                var parameterNode = p.ValueDeclaration.Cast<IParameterDeclaration>();
                if (IsRestParameter(parameterNode))
                {
                    WritePunctuation(writer, SyntaxKind.DotDotDotToken);
                }

                AppendSymbolNameOnly(p, writer);
                if (m_checker.IsOptionalParameter(parameterNode))
                {
                    WritePunctuation(writer, SyntaxKind.QuestionToken);
                }

                WritePunctuation(writer, SyntaxKind.ColonToken);
                WriteSpace(writer);

                BuildTypeDisplay(m_checker.GetTypeOfSymbol(p), writer, enclosingDeclaration, flags, symbolStack);
            }

            private void BuildDisplayForTypeParametersAndDelimiters(IReadOnlyList<ITypeParameter> typeParameters, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags, Stack<ISymbol> symbolStack)
            {
                if (typeParameters != null && typeParameters.Count != 0)
                {
                    WritePunctuation(writer, SyntaxKind.LessThanToken);
                    for (var i = 0; i < typeParameters.Count; i++)
                    {
                        if (i > 0)
                        {
                            WritePunctuation(writer, SyntaxKind.CommaToken);
                            WriteSpace(writer);
                        }

                        BuildTypeParameterDisplay(typeParameters[i], writer, enclosingDeclaration, flags, symbolStack);
                    }

                    WritePunctuation(writer, SyntaxKind.GreaterThanToken);
                }
            }

            private void BuildDisplayForTypeArgumentsAndDelimiters(IReadOnlyList<ITypeParameter> typeParameters, ITypeMapper mapper, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags? flags = null, Stack<ISymbol> symbolStack = null)
            {
                if (typeParameters != null && typeParameters.Count != 0)
                {
                    WritePunctuation(writer, SyntaxKind.LessThanToken);
                    for (var i = 0; i < typeParameters.Count; i++)
                    {
                        if (i > 0)
                        {
                            WritePunctuation(writer, SyntaxKind.CommaToken);
                            WriteSpace(writer);
                        }

                        BuildTypeDisplay(mapper.Mapper(typeParameters[i]), writer, enclosingDeclaration, TypeFormatFlags.None);
                    }

                    WritePunctuation(writer, SyntaxKind.GreaterThanToken);
                }
            }

            private void BuildDisplayForParametersAndDelimiters(IReadOnlyList<ISymbol> parameters, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags, Stack<ISymbol> symbolStack)
            {
                WritePunctuation(writer, SyntaxKind.OpenParenToken);
                for (var i = 0; i < parameters.Count; i++)
                {
                    if (i > 0)
                    {
                        WritePunctuation(writer, SyntaxKind.CommaToken);
                        WriteSpace(writer);
                    }

                    BuildParameterDisplay(parameters[i], writer, enclosingDeclaration, flags, symbolStack);
                }

                WritePunctuation(writer, SyntaxKind.CloseParenToken);
            }

            private static void BuildTypePredicateDisplay(ISymbolWriter writer, ITypePredicate predicate)
            {
                var identifierTypePredicate = IsIdentifierTypePredicate(predicate);
                if (identifierTypePredicate != null)
                {
                    writer.WriteParameter(identifierTypePredicate.ParameterName);
                }
                else
                {
                    WriteKeyword(writer, SyntaxKind.ThisKeyword);
                }

                WriteSpace(writer);
                WriteKeyword(writer, SyntaxKind.IsKeyword);
                WriteSpace(writer);
            }

            private void BuildReturnTypeDisplay(ISignature signature, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags, Stack<ISymbol> symbolStack)
            {
                if ((flags & TypeFormatFlags.WriteArrowStyleSignature) != TypeFormatFlags.None)
                {
                    WriteSpace(writer);
                    WritePunctuation(writer, SyntaxKind.EqualsGreaterThanToken);
                }
                else
                {
                    WritePunctuation(writer, SyntaxKind.ColonToken);
                }

                WriteSpace(writer);

                var returnType = m_checker.GetReturnTypeOfSignature(signature);
                BuildTypeDisplay(returnType, writer, enclosingDeclaration, flags, symbolStack);
            }

            private void BuildSignatureDisplay(ISignature signature, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags, SignatureKind? kind, Stack<ISymbol> symbolStack)
            {
                if (kind == SignatureKind.Construct)
                {
                    WriteKeyword(writer, SyntaxKind.NewKeyword);
                    WriteSpace(writer);
                }

                if (signature.Target != null && ((flags & TypeFormatFlags.WriteTypeArgumentsOfSignature) != TypeFormatFlags.None))
                {
                    // Instantiated signature, write type arguments instead
                    // This is achieved by passing in the mapper separately
                    BuildDisplayForTypeArgumentsAndDelimiters(signature.Target.TypeParameters, signature.Mapper, writer, enclosingDeclaration);
                }
                else
                {
                    BuildDisplayForTypeParametersAndDelimiters(signature.TypeParameters, writer, enclosingDeclaration, flags, symbolStack);
                }

                BuildDisplayForParametersAndDelimiters(signature.Parameters, writer, enclosingDeclaration, flags, symbolStack);
                BuildReturnTypeDisplay(signature, writer, enclosingDeclaration, flags, symbolStack);
            }

            void ISymbolDisplayBuilder.BuildTypeDisplay(IType inputType, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags globalFlags)
            {
                BuildTypeDisplay(inputType, writer, enclosingDeclaration, globalFlags, symbolStack: null);
            }

            void ISymbolDisplayBuilder.BuildSymbolDisplay(ISymbol symbol, ISymbolWriter writer, INode enclosingDeclaration, SymbolFlags meaning, SymbolFormatFlags flags)
            {
                BuildSymbolDisplay(symbol, writer, enclosingDeclaration, meaning, flags, typeFlags: TypeFormatFlags.None);
            }

            void ISymbolDisplayBuilder.BuildSignatureDisplay(ISignature signatures, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags, SignatureKind kind)
            {
                BuildSignatureDisplay(signatures, writer, enclosingDeclaration, flags, kind, symbolStack: null);
            }

            void ISymbolDisplayBuilder.BuildParameterDisplay(ISymbol parameter, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags)
            {
                BuildParameterDisplay(parameter, writer, enclosingDeclaration, flags, symbolStack: null);
            }

            void ISymbolDisplayBuilder.BuildTypeParameterDisplay(ITypeParameter tp, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags)
            {
                BuildTypeParameterDisplay(tp, writer, enclosingDeclaration, flags, symbolStack: null);
            }

            void ISymbolDisplayBuilder.BuildTypeParameterDisplayFromSymbol(ISymbol symbol, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags)
            {
                BuildTypeParameterDisplayFromSymbol(symbol, writer, enclosingDeclaration, flags, symbolStack: null);
            }

            void ISymbolDisplayBuilder.BuildDisplayForParametersAndDelimiters(List<ISymbol> parameters, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags)
            {
                BuildDisplayForParametersAndDelimiters(parameters?.ToArray(), writer, enclosingDeclaration, flags, symbolStack: null);
            }

            void ISymbolDisplayBuilder.BuildDisplayForTypeParametersAndDelimiters(List<ITypeParameter> typeParameters, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags)
            {
                BuildDisplayForTypeParametersAndDelimiters(typeParameters, writer, enclosingDeclaration, flags, symbolStack: null);
            }

            void ISymbolDisplayBuilder.BuildReturnTypeDisplay(ISignature signature, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags)
            {
                BuildReturnTypeDisplay(signature, writer, enclosingDeclaration, flags, symbolStack: null);
            }
        }
    }
}
