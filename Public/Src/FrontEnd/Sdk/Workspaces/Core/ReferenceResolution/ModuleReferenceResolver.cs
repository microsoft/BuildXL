// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using TypeScript.Net.Types;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Distinguishes between intra and inter module references. This is DScript V2 and V1 compatible.
    /// TODO: When DScript V2 becomes the norm, internal references will be gone. Consider removing that functionality from this class at that point.
    /// </summary>
    public sealed class ModuleReferenceResolver : IModuleReferenceResolver
    {
        private readonly PathTable m_pathTable;

        /// <nodoc/>
        public ModuleReferenceResolver(PathTable pathTable)
        {
            m_pathTable = pathTable;
        }

        /// <summary>
        /// From all import and export specifiers in <param name="sourceFile"/>, filters the ones
        /// that do not start with '.' or '/'.
        /// </summary>
        public IEnumerable<ModuleReferenceWithProvenance> GetExternalModuleReferences(ISourceFile sourceFile)
        {
            Contract.Requires(sourceFile != null);
            foreach (var literalExpression in sourceFile.LiteralLikeSpecifiers)
            {
                if (IsValidModuleReference(literalExpression) && IsModuleReference(literalExpression))
                {
                    yield return ConvertLiteralExpressionToNameWithProvenance(sourceFile, literalExpression);
                }
            }
        }

        /// <inheritdoc/>
        public bool TryUpdateExternalModuleReference(ISourceFile sourceFile, ModuleDefinition externalModuleReference, out Failure failure)
        {
            Contract.Requires(sourceFile != null);
            Contract.Requires(externalModuleReference != null);

            failure = null;

            if (sourceFile.ResolvedModules.ContainsKey(externalModuleReference.Descriptor.Name))
            {
                return true;
            }

            // We only update the resolved modules of the file if the external module is one with explicit references. This is because, in that
            // case, the file actually represents the module exports. Otherwise, the resolved modules field is not used by the checker for resolution
            // and the export symbol is computed as an aggregation of all the files in the module
            if (externalModuleReference.ResolutionSemantics == NameResolutionSemantics.ExplicitProjectReferences)
            {
                var resolvedModule = new ResolvedModule(externalModuleReference.MainFile.ToString(m_pathTable), isExternaLibraryImport: true);
                sourceFile.ResolvedModules[externalModuleReference.Descriptor.Name] = resolvedModule;
            }

            return true;
        }

        /// <summary>
        /// Updates all internal (project-like) references of <param name="sourceFile"/> with owning module <param name="owningModule"/>. Checks
        /// module ownership, so only internal references that fall into the same module are allowed. Otherwise, failures are reported
        /// in <param name="resultingFailures"/>
        /// </summary>
        /// <remarks>
        /// Since this has to deal with source files (which handles paths as strings) and module definition, there is a mix of strings and AbsolutePath.
        /// But, since AbsolutePath canonicalizes paths in terms of casing, etc. we rely on them for comparisons
        /// </remarks>
        public bool TryUpdateAllInternalModuleReferences(
            ISourceFile sourceFile,
            ModuleDefinition owningModule,
            out Failure[] resultingFailures)
        {
            Contract.Requires(sourceFile != null);
            Contract.Requires(owningModule != null);

            List<Failure> failures = null;

            var internalReferences = sourceFile.LiteralLikeSpecifiers.Where(specifier => IsValidModuleReference(specifier) && !IsModuleReference(specifier));

            var pathToModuleRoot = owningModule.Root.ToString(m_pathTable);

            foreach (var relativeReference in internalReferences)
            {
                var relativeReferenceText = relativeReference.Text;

                if (sourceFile.ResolvedModules.ContainsKey(relativeReferenceText))
                {
                    continue;
                }

                var pathToSourceFileFolder = sourceFile.Path.Parent().ToString();

                // If the path start with /, means module-rooted. Otherwise, it is relative to the spec location.
                var absoluteReferenceString =
                    ComputeAbsoluteReference(
                        relativeReferenceText[0] == '/' ? pathToModuleRoot : pathToSourceFileFolder,
                        relativeReferenceText);

                if (!AbsolutePath.TryCreate(m_pathTable, absoluteReferenceString, out AbsolutePath absoluteReference))
                {
                    LazyInitializer.EnsureInitialized(ref failures, () => new List<Failure>());

                    // TODO: Consider a more specific failure stating that the reference is malformed (from a path point of view)
                    failures.Add(new SpecNotUnderAModuleFailure(sourceFile, relativeReferenceText, owningModule.Descriptor.DisplayName));
                    continue;
                }

                if (owningModule.Specs.Contains(absoluteReference))
                {
                    sourceFile.ResolvedModules[relativeReferenceText] = new ResolvedModule(absoluteReference.ToString(m_pathTable), true);
                }
                else
                {
                    LazyInitializer.EnsureInitialized(ref failures, () => new List<Failure>());
                    failures.Add(new SpecNotUnderAModuleFailure(sourceFile, relativeReferenceText, owningModule.Descriptor.DisplayName));
                }
            }

            resultingFailures = failures?.ToArray();

            return failures == null;
        }

        private static string ComputeAbsoluteReference(string rootPath, string relativePath)
        {
            // The relative path uses forward slashes, so we turn them into backslashes
            relativePath = relativePath.Replace("/", "\\");

            // If the relative path is package rooted, we can concatenate it directly with the root.
            // Otherwise, it starts with . (can be . or ..). In both cases, we need an extra slash to
            // make an absolute reference
            if (relativePath[0] == '.')
            {
                relativePath = '\\' + relativePath;
            }

            return rootPath + relativePath;
        }

        /// <summary>
        /// Returns true if the module specifier is valid.
        /// </summary>
        [Pure]
        public static bool IsValidModuleReference(ILiteralExpression specifier)
        {
            // Invalid specifiers are possible for IDE scenarios.
            return !string.IsNullOrEmpty(specifier.Text);
        }

        /// <summary>
        /// Determines whether an import or export specifier represents a module reference (as opposed to a file reference)
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public static bool IsModuleReference(ILiteralExpression specifier)
        {
            Contract.Requires(IsValidModuleReference(specifier));

            var text = specifier.Text;
            return text[0] != '.' && text[0] != '/';
        }

        private static ModuleReferenceWithProvenance ConvertLiteralExpressionToNameWithProvenance(ISourceFile sourceFile, ILiteralExpression expression)
        {
            var lineInfo = expression.GetLineInfo(sourceFile);
            return new ModuleReferenceWithProvenance(expression.Text, lineInfo, sourceFile.FileName);
        }
    }
}
