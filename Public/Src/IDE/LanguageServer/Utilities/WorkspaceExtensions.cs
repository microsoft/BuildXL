// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.Types;

namespace BuildXL.Ide.LanguageServer.Server.Utilities
{
    internal static class WorkspaceExtensions
    {
        /// <summary>
        /// Returns true if the <paramref name="module"/> is a prelude or a special configuration module that contains all configuration files.
        /// </summary>
        public static bool IsPreludeOrConfigurationModule(this Workspace workspace, ParsedModule module)
        {
            return module != null && (module == workspace.PreludeModule || module == workspace.ConfigurationModule);
        }

        /// <summary>
        /// Returns all the spec source files.
        /// </summary>
        public static IReadOnlyList<ISourceFile> GetSpecFiles(this Workspace workspace)
        {
            return workspace.SpecSources.Select(s => s.Value.SourceFile).ToArray();
        }

        /// <summary>
        /// Gets SourceFile contained in <paramref name="workspace"/> corresponding to this <paramref name="position"/>
        /// </summary>
        public static bool TryGetSourceFile(
            this Workspace workspace,
            TextDocumentPositionParams position,
            PathTable pathTable,
            out ISourceFile sourceFile)
        {
            var uri = new Uri(position.TextDocument.Uri);
            var pathToFile = uri.ToAbsolutePath(pathTable);

            return workspace.TryGetSourceFile(pathToFile, out sourceFile);
        }
    }
}
