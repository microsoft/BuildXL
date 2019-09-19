// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Script.Constants;
using JetBrains.Annotations;
using TypeScript.Net.Diagnostics;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Set of extension methods for the <see cref="ISourceFile"/> interface.
    /// </summary>
    public static class SourceFileExtensions
    {
        /// <nodoc />
        public static bool IsScriptFile([JetBrains.Annotations.NotNull] this ISourceFile sourceFile)
        {
            Contract.Requires(sourceFile != null);

            return ExtensionUtilities.IsScriptExtension(Path.GetExtension(sourceFile.FileName)) || sourceFile.FileName.StartsWith("Prelude.");
        }

        /// <nodoc />
        public static bool IsProjectFileExtension([JetBrains.Annotations.NotNull] this ISourceFile sourceFile)
        {
            Contract.Requires(sourceFile != null);

            return ExtensionUtilities.IsProjectFileExtension(Path.GetExtension(sourceFile.FileName));
        }

        /// <nodoc />
        public static bool IsBuildListFile([JetBrains.Annotations.NotNull] this ISourceFile sourceFile)
        {
            Contract.Requires(sourceFile != null);

            return ExtensionUtilities.IsBuildListFile(sourceFile.FileName);
        }

        /// <nodoc />
        public static bool IsExternalModule([JetBrains.Annotations.NotNull] ISourceFile file)
        {
            return file.ExternalModuleIndicator != null;
        }

        /// <nodoc />
        public static bool IsExternalOrCommonJsModule([JetBrains.Annotations.NotNull] ISourceFile file)
        {
            return (file.ExternalModuleIndicator ?? file.CommonJsModuleIndicator) != null;
        }

        /// <nodoc />
        public static bool IsDeclarationFile([JetBrains.Annotations.NotNull] ISourceFile file)
        {
            return (file.Flags & NodeFlags.DeclarationFile) != 0;
        }

        /// <nodoc />
        public static bool HasResolvedModule([JetBrains.Annotations.NotNull]ISourceFile sourceFile, string moduleNameText)
        {
            return (sourceFile.ResolvedModules != null) && sourceFile.ResolvedModules.ContainsKey(moduleNameText);
        }

        /// <nodoc />
        [CanBeNull]
        public static IResolvedModule GetResolvedModule([JetBrains.Annotations.NotNull]ISourceFile sourceFile, string moduleNameText)
        {
            return HasResolvedModule(sourceFile, moduleNameText) ? sourceFile.ResolvedModules[moduleNameText] : null;
        }

        /// <summary>
        /// Returns all diagnostics (parse and bind) for a given source file.
        /// </summary>
        /// <remarks>
        /// This function won't return diagnostic from the checker because they're not stored on the source file level.
        /// </remarks>
        public static IEnumerable<Diagnostic> GetAllDiagnostics([JetBrains.Annotations.NotNull] this ISourceFile sourceFile)
        {
            return sourceFile.ParseDiagnostics.Concat(sourceFile.BindDiagnostics);
        }

        /// <summary>
        /// Returns true if a given file has parse or bind diagnostics.
        /// </summary>
        public static bool HasDiagnostics([JetBrains.Annotations.NotNull]this ISourceFile sourceFile)
        {
            // TODO: here we need to add checker diagnostics as well.
            return sourceFile.ParseDiagnostics.Count != 0 || sourceFile.BindDiagnostics.Count != 0;
        }
    }
}
