// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;

using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;

namespace BuildXL.Ide.LanguageServer.Completion
{
    /// <summary>
    /// Provider for code auto-completion functionality.
    /// </summary>
    internal static class ImportStatement
    {
        public static bool ShouldCreateCompletionItemsForImportStatements(CompletionState completionState, INode completionNode)
        {
            return completionState.StartingNode.IsLiteralFromImportOrExportDeclaration();
        }

        public static IEnumerable<CompletionItem> GetCompletionsForImportStatement(CompletionState completionState, INode completionNode)
        {
            // If the file belongs to the module with explicit policy about what the module can reference,
            // when the intellisense should show just that list.
            // Otherwise, we need to show all known modules.
            return FilterModulesFor(completionState)
                .Select(
                    m => new CompletionItem()
                    {
                        Kind = CompletionItemKind.Text,
                        Label = m.Descriptor.Name,
                        InsertText = m.Descriptor.Name,
                        Detail = $"Module config file: '{m.Definition.ModuleConfigFile.ToString(completionState.PathTable)}'",
                        Documentation = $"Module kind: {(m.Definition.ResolutionSemantics == NameResolutionSemantics.ExplicitProjectReferences ? "V1" : "V2")}",
                    });
        }

        private static IEnumerable<ParsedModule> FilterModulesFor(CompletionState completionState)
        {
            HashSet<string> allowedModules = null;

            var module = completionState.Workspace.TryGetModuleBySpecFileName(completionState.StartingNode.GetSourceFile().GetAbsolutePath(completionState.PathTable));
            if (module?.Definition.AllowedModuleDependencies != null)
            {
                allowedModules = new HashSet<string>(module.Definition.AllowedModuleDependencies.Select(m => m.Name));
            }

            return completionState.Workspace.SpecModules

                // Need to filter out special config module ("__Config__"), current module
                // and left only modules allowed by the module configuration (if available).
                .Where(m => !m.Descriptor.IsSpecialConfigModule() && m.Descriptor != module.Descriptor && (allowedModules == null || allowedModules.Contains(m.Descriptor.Name)))
                .ToList();
        }
    }
}
