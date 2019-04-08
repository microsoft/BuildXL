// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Rule that prevents from using <code>Transformer</code> namespace from the prelude.
    /// </summary>
    internal sealed class EnforceNoTransformersRule : PolicyRule
    {
        // This is a well-known module in standard SDK that wrapps ambient transformer.
        // Regardless of this rule this package can reference ambient transformer namespace.
        private static readonly HashSet<string> WellKnownTransformerWrappers = new HashSet<string>(StringComparer.InvariantCulture)
        {
            "Sdk.Transformers",
            "Sdk.TransformersInternal"
        };

        private const string AmbientNamespaceName = "Transformer";

        /// <inheritdoc />
        public override string Name => "NoTransformers";

        /// <nodoc/>
        public override string Description => "Disallows all Transformer usages except for well-known module.";

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(this, ValidateNode, TypeScript.Net.Types.SyntaxKind.PropertyAccessExpression);
            context.RegisterSyntaxNodeAction(this, ValidateTypeNode, TypeScript.Net.Types.SyntaxKind.TypeReference);
        }

        private void ValidateNode(INode node, DiagnosticContext context)
        {
            if (IsWellKnownModule(node.GetSourceFile(), context))
            {
                return;
            }

            var propertyAccess = node.Cast<IPropertyAccessExpression>();
            var identifier = propertyAccess.Expression.As<IIdentifier>();
            CheckIdentifier(node, context, identifier);
        }

        private void ValidateTypeNode(INode node, DiagnosticContext context)
        {
            if (IsWellKnownModule(node.GetSourceFile(), context))
            {
                return;
            }

            var propertyAccess = node.Cast<ITypeReferenceNode>();
            var identifier = propertyAccess.TypeName.As<IQualifiedName>()?.Left?.As<IIdentifier>();
            CheckIdentifier(node, context, identifier);
        }

        private void CheckIdentifier(INode node, DiagnosticContext context, IIdentifier identifier)
        {
            if (identifier == null || identifier.Text != AmbientNamespaceName)
            {
                return;
            }

            var symbol = context.SemanticModel.TypeChecker.GetSymbolAtLocation(identifier);
            if (IsPrelude(symbol?.DeclarationList.FirstOrDefault()?.GetSourceFile(), context))
            {
                context.Logger.ReportAmbientTransformerIsDisallowed(context.LoggingContext, node.LocationForLogging(context.SourceFile), Name);
            }
        }

        private static bool IsPrelude([CanBeNull]ISourceFile sourceFile, DiagnosticContext context)
        {
            return sourceFile != null && context.Workspace.PreludeModule?.Specs.ContainsKey(sourceFile.GetAbsolutePath(context.PathTable)) == true;
        }

        private static bool IsWellKnownModule(ISourceFile sourceFile, DiagnosticContext context)
        {
            if (context.Workspace == null)
            {
                // This rule is applicable only when the workspace is used.
                return true;
            }

            var module = context.Workspace.GetModuleBySpecFileName(sourceFile.GetAbsolutePath(context.PathTable));
            return WellKnownTransformerWrappers.Contains(module.Descriptor.Name);
        }
    }
}
