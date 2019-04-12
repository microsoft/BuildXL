// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Abstract base class that defines an API for symbol binding during AST Conversion.
    /// </summary>
    internal sealed class Binder
    {
        private readonly RuntimeModelContext m_runtimeModelContext;
        private readonly SymbolAtom m_runtimeRootNamespaceSymbol;
        /// <nodoc />
        private Binder(RuntimeModelContext runtimeModelContext)
        {
            Contract.Requires(runtimeModelContext != null);

            m_runtimeModelContext = runtimeModelContext;
            m_runtimeRootNamespaceSymbol = SymbolAtom.Create(runtimeModelContext.StringTable, Constants.Names.RuntimeRootNamespaceAlias);
        }

        /// <summary>
        /// Factory method that creates a binder.
        /// </summary>
        public static Binder Create(RuntimeModelContext runtimeModelContext)
        {
            return new Binder(runtimeModelContext);
        }

        /// <nodoc />
        public object ToBindingObject(Expression expression, bool thunking)
        {
            Analysis.IgnoreArgument(this);

            if (expression == null)
            {
                return UndefinedLiteral.Instance;
            }

            if (expression is FunctionLikeExpression lambda)
            {
                return lambda;
            }

            if (expression is IConstantExpression constant)
            {
                if (constant.Value is EvaluationResult r)
                {
                    return r.Value;
                }

                return constant.Value;
            }

            // This is used in the V1 code path for binding a thunk, or to bind an object literal (i.e. thunking = false). Therefore it is fine to not capture any template.
            return thunking ? (object)new Thunk(expression, capturedTemplateReference: null) : expression;
        }

        /// <nodoc />
        public void AddFunctionDeclaration(
            ModuleLiteral module,
            FunctionDeclaration functionDeclaration,
            in UniversalLocation location)
        {
            var lambdaExpression = FunctionLikeExpression.CreateFunction(functionDeclaration);
            module.AddResolvedEntry(location.AsFilePosition(), lambdaExpression);
        }

        /// <nodoc />
        public void AddImportAliasBinding(
            ModuleLiteral module,
            Expression expression,
            in UniversalLocation location)
        {
            module.AddResolvedEntry(location.AsFilePosition(), new ResolvedEntry(FullSymbol.Invalid, expression));
        }

        /// <nodoc />
        public void AddEnumMember(
            ModuleLiteral module,
            EnumMemberDeclaration enumMember,
            EnumValue enumValue,
            in UniversalLocation location)
        {
            Analysis.IgnoreArgument(enumMember);
            module.AddResolvedEntry(location.AsFilePosition(), new ResolvedEntry(FullSymbol.Invalid, enumValue));
        }

        /// <nodoc />
        public void AddVariableBinding(ModuleLiteral module, VarDeclaration variable, FullSymbol fullSymbol,
            QualifierSpaceId qualifierSpaceId, Expression capturedTemplate, in UniversalLocation location)
        {
            var fullName = GetFullyQualifiedName(module, variable.Name);

            var resolvedSymbol = ToThunkedResolvedSymbol(
                fullName,
                variable.Initializer,
                qualifierSpaceId,
                capturedTemplate,
                isVariableDeclaration: true);

            module.AddResolvedEntry(location.AsFilePosition(), (ResolvedEntry) resolvedSymbol);

            // if the full name is valid, we need to register in a name to symbol table.
            if (fullSymbol.IsValid)
            {
                module.AddResolvedEntry(fullSymbol, resolvedSymbol);
            }
        }

        private FullSymbol GetFullyQualifiedName(ModuleLiteral module, SymbolAtom name)
        {
            // If the module is a top most namespace, then we don't use its name since it is _$
            if (module.Name.IsValid && module.Name.GetName(m_runtimeModelContext.SymbolTable) != m_runtimeRootNamespaceSymbol)
            {
                // A valid name should mean the module is a type or namespace module
                Contract.Assert(module is TypeOrNamespaceModuleLiteral);

                return module.Name.Combine(m_runtimeModelContext.SymbolTable, name);
            }

            return FullSymbol.Create(m_runtimeModelContext.SymbolTable, name);
        }

        /// <nodoc />
        public void AddExportBinding(
            ModuleLiteral module,
            SymbolAtom name,
            Expression expression,
            in UniversalLocation location)
        {
            // TODO: why an export binding needs a thunk by itself? It is at most a reference
            // to an existing thunk
            var resolvedSymbol = ToThunkedResolvedSymbol(
                FullSymbol.Create(m_runtimeModelContext.SymbolTable, name),
                expression,
                QualifierSpaceId.Invalid,
                capturedTemplate: ObjectLiteral0.SingletonWithoutProvenance,  // The captured template is not important in an export clause, just making sure it is not null.
                isVariableDeclaration: false);

            module.AddResolvedEntry(location.AsFilePosition(), (ResolvedEntry) resolvedSymbol);
        }

        private ResolvedEntry ToThunkedResolvedSymbol(FullSymbol contextName, Expression expression, QualifierSpaceId qualifierSpaceId, Expression capturedTemplate, bool isVariableDeclaration)
        {
            Analysis.IgnoreArgument(this);

            if (IsNullOrConstantExpression(contextName, expression, out ResolvedEntry resolvedSymbol))
            {
                return resolvedSymbol;
            }

            return new ResolvedEntry(contextName, new Thunk(expression, capturedTemplate), qualifierSpaceId, isVariableDeclaration);
        }

        private static bool IsNullOrConstantExpression(FullSymbol symbolName, Expression expression, out ResolvedEntry resolvedSymbol)
        {
            if (expression == null)
            {
                resolvedSymbol = new ResolvedEntry(symbolName, (IConstantExpression)UndefinedLiteral.Instance);
                return true;
            }

            if (expression is IConstantExpression constant)
            {
                resolvedSymbol = new ResolvedEntry(symbolName, constant);
                return true;
            }

            resolvedSymbol = default(ResolvedEntry);
            return false;
        }
    }
}
