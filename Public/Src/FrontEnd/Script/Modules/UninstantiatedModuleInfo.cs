// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.Utilities.Core.Qualifier;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Class contains uninstantiated module literal with some additional data associated with it.
    /// </summary>
    public sealed class UninstantiatedModuleInfo : IUninstantiatedModuleInfo
    {
        /// <nodoc/>
        [CanBeNull]
        public FileModuleLiteral FileModuleLiteral { get; }

        /// <nodoc/>
        [CanBeNull]
        public TypeOrNamespaceModuleLiteral TypeOrNamespaceTypeOrNamespaceLiteral { get; }

        /// <nodoc/>
        [NotNull]
        public ModuleLiteral ModuleLiteral => (ModuleLiteral)FileModuleLiteral ?? TypeOrNamespaceTypeOrNamespaceLiteral;

        /// <summary>
        /// Gets the qualifier space.
        /// </summary>
        public QualifierSpaceId QualifierSpaceId { get; }

        /// <summary>
        /// Source file with evaluation AST.
        /// </summary>
        public SourceFile SourceFile { get; }

        /// <nodoc />
        // Used only for semantic evaluation
        public UninstantiatedModuleInfo(SourceFile sourceFile, [NotNull]TypeOrNamespaceModuleLiteral typeOrNamespaceLiteral, QualifierSpaceId qualifierSpaceId)
            : this(sourceFile, qualifierSpaceId)
        {
            Contract.Requires(typeOrNamespaceLiteral != null, "typeOrNamespaceLiteral != null");

            TypeOrNamespaceTypeOrNamespaceLiteral = typeOrNamespaceLiteral;
        }

        /// <nodoc />
        public UninstantiatedModuleInfo(SourceFile sourceFile, [NotNull]FileModuleLiteral fileModuleLiteral, QualifierSpaceId qualifierSpaceId)
            : this(sourceFile, qualifierSpaceId)
        {
            Contract.Requires(fileModuleLiteral != null, "fileModuleLiteral != null");

            FileModuleLiteral = fileModuleLiteral;
        }

        /// <nodoc />
        private UninstantiatedModuleInfo(SourceFile sourceFile, QualifierSpaceId qualifierSpaceId)
        {
            Contract.Requires(qualifierSpaceId.IsValid, "qualifierSpaceId.IsValid");

            SourceFile = sourceFile;
            QualifierSpaceId = qualifierSpaceId;
        }

        /// <summary>
        /// Instantiate current module literal.
        /// </summary>
        /// <remarks>
        /// TODO: Current instantiation logic is not very DScript V2 friendly.
        /// Even to get a namespace, the whole file is intstantiated.
        /// This needs to be addressed.
        /// </remarks>
        public ModuleLiteral Instantiate(ModuleRegistry moduleRegistry, QualifierValue qualifier)
        {
            return ModuleLiteral.Instantiate(moduleRegistry, qualifier);
        }
    }
}
