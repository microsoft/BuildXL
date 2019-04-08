// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.FrontEnd.Script;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// "Module literal" for type or namespace.
    /// </summary>
    public class TypeOrNamespaceModuleLiteral : ModuleLiteral
    {
        /// <summary>
        /// Global fake namespace used in filtered scenarios.
        /// </summary>
        public static TypeOrNamespaceModuleLiteral EmptyInstance { get; } = new TypeOrNamespaceModuleLiteral();

        /// <inheritdoc/>
        /// ReSharper disable once PossibleNullReferenceException
        public override Package Package => OuterScope.Package;

        /// <summary>
        /// OuterScope could be <see cref="GlobalModuleLiteral"/>, in which case this property returns null. This is the case for ambients.
        /// </summary>
        public override FileModuleLiteral CurrentFileModule => OuterScope as FileModuleLiteral;

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.TypeOrNamespaceModuleLiteral;

        /// <nodoc />
        private TypeOrNamespaceModuleLiteral()
            : base(default(ModuleLiteralId), QualifierValue.Unqualified, null, default(LineInfo))
        { }

        /// <nodoc/>
        public TypeOrNamespaceModuleLiteral(ModuleLiteralId id, QualifierValue qualifier, ModuleLiteral outerScope, LineInfo location)
            : base(id, qualifier, outerScope, location)
        {
            Contract.Requires(id.Name.IsValid, "id.Name.IsValid");
            Contract.Requires(outerScope != null, "outerScope != null");

            // Only in V1 type or namespace module literals are unqualified. In V2 unqualified is never used for type or namespace literals.
            Contract.Requires(qualifier == QualifierValue.Unqualified || outerScope.Qualifier.QualifierId == qualifier.QualifierId);
        }

        /// <nodoc />
        public static Node Deserialize(DeserializationContext context, LineInfo location)
        {
            var moduleId = ReadModuleLiteralId(context.Reader);
            if (!moduleId.IsValid)
            {
                return EmptyInstance;
            }

            return new TypeOrNamespaceModuleLiteral(moduleId, QualifierValue.Unqualified, context.CurrentFile, location);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            Write(writer, Id);
        }

        /// <inheritdoc />
        // ReSharper disable once PossibleNullReferenceException
        // In DScript V2 namespaces could be qualified as well.
        public override QualifierValue GetFileQualifier() => Qualifier != QualifierValue.Unqualified ? Qualifier : OuterScope.Qualifier;

        /// <nodoc />
        public override ModuleLiteral Instantiate(ModuleRegistry moduleRegistry, QualifierValue qualifier)
        {
            Contract.Assert(moduleRegistry != null);
            Contract.Assert(qualifier != QualifierValue.Unqualified);

            // Due to sharing the following contract no longer holds: Contract.Requires(Qualifier == Unqualified);
            var moduleKey = QualifiedModuleId.Create(Id, qualifier.QualifierId);

            return moduleRegistry.InstantiateModule(
                (moduleRegistry, @this: this, qualifier, CurrentFileModule),
                moduleKey,
                (state, k) =>
                {
                    var localModuleRegistry = state.moduleRegistry;
                    var @this = state.@this;
                    var localQualifier = state.qualifier;
                    var localCurrentFileModule = state.CurrentFileModule;
                    return @this.DoInstantiate(localModuleRegistry, @this, localQualifier, localCurrentFileModule);
                });
        }

        private TypeOrNamespaceModuleLiteral DoInstantiate(ModuleRegistry moduleRegistry, TypeOrNamespaceModuleLiteral module, QualifierValue qualifier, FileModuleLiteral outerScope)
        {
            Contract.Requires(module != null);
            Contract.Requires(qualifier != QualifierValue.Unqualified);

            Interlocked.CompareExchange(ref m_qualifier, qualifier, QualifierValue.Unqualified);

            // The outer scope of this should have the same qualifier. So if that's not the case we instantiate one and set the parent appropriately
            if (outerScope.Qualifier.QualifierId != qualifier.QualifierId)
            {
                var newOuterScope = moduleRegistry.InstantiateModule(
                    (outerScope, moduleRegistry, qualifier),
                    QualifiedModuleId.Create(outerScope.Id, qualifier.QualifierId),
                    (state, qualifiedModuleId) =>
                    {
                        var capturedOuterScope = state.outerScope;
                        var capturedModuleRegistry = state.moduleRegistry;
                        var capturedQualifier = state.qualifier;
                        return capturedOuterScope.InstantiateFileModuleLiteral(capturedModuleRegistry, capturedQualifier);
                    });

                return new TypeOrNamespaceModuleLiteral(module.Id, qualifier, newOuterScope, module.Location);
            }

            if (m_qualifier.QualifierId == qualifier.QualifierId)
            {
                // Uninstantiated module becomes the first instance.
                return this;
            }

            // Create a new file module instance.
            return new TypeOrNamespaceModuleLiteral(module.Id, qualifier, outerScope, module.Location);
        }
    }
}
