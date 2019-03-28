// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using LanguageServer;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using CancellationToken = System.Threading.CancellationToken;

namespace BuildXL.Ide.LanguageServer.Providers
{
    /// <summary>
    /// Provider that allows for code refactor (light-bulb) functionality.
    /// </summary>
    public sealed class CodeActionProvider : IdeProviderBase
    {
        /// <nodoc />
        public const string CreateImportStatementCommand = "CreateImportStatement";

        /// <summary>
        /// Maintains the list of code actions (commands) understood by this provider
        /// </summary>
        private static readonly List<CodeActionInfo> s_codeActions = new List<CodeActionInfo>
            {
                new CodeActionInfo
                {
                    Title = "Copy import statement",
                    Command = CreateImportStatementCommand,
                    TryCreateCommand = TryCreateImportStatementCommand,
                    ExecuteCommand = ExecuteCreateImportStatementCommand,
                },
            };

        /// <nodoc />
        internal CodeActionProvider(ProviderContext providerContext, IExecuteCommandProvider commandProvider)
            : base(providerContext)
        {
            foreach (var codeAction in s_codeActions)
            {
                commandProvider.AddCommand(codeAction.Command, codeAction.ExecuteCommand);
            }
        }

        /// <summary>
        /// Creates the list of commands this provider is capable of executing
        /// </summary>
        /// <remarks>
        /// Used to fill out the server capabilities ServerCapabilities/executeCommandProvider
        /// </remarks>
        public static List<string> GetCommands()
        {
            var result = new List<string>();
            foreach (var codeAction in s_codeActions)
            {
                result.Add(codeAction.Command);
            }

            return result;
        }

        /// <summary>
        /// Called to provide a list of code actions (light-bulbs)
        /// </summary>
        /// <remarks>
        /// >
        /// Spins through each command in the static list and if the command is determined to be valid,
        /// adds it to the array of commands to return.
        /// </remarks>
        public Result<Command[], ResponseError> CodeAction(CodeActionParams @params, CancellationToken token)
        {
            // TODO: support cancellation
            var result = new List<Command>(s_codeActions.Count);
            foreach (var codeAction in s_codeActions)
            {
                if (codeAction.TryCreateCommand(Workspace, PathTable, @params, out var commandArguments))
                {
                    result.Add(
                        new Command
                        {
                            Title = codeAction.Title,
                            CommandIdentifier = codeAction.Command,
                            Arguments = commandArguments,
                        });
                }
            }

            return Result<Command[], ResponseError>.Success(result.ToArray());
        }

        /// <summary>
        /// Delegate that returns a list of arguments to create a command with.
        /// </summary>
        /// <returns>
        /// True if action params are valid; false otherwise.
        /// </returns>
        private delegate bool TryCreateCommand(
            Workspace workspace,
            PathTable pathTable,
            CodeActionParams actionParams,
            out dynamic[] commandArguments);

        /// <summary>
        /// Used to create a list of commands this provider understands
        /// </summary>
        private struct CodeActionInfo
        {
            /// <summary>
            /// The title of the command (shows up in the UI when you click on the light-bulb)
            /// </summary>
            public string Title;

            /// <summary>
            /// The name of the command (verb) that is passed to "workspace/executeCommand"
            /// </summary>
            public string Command;

            /// <summary>
            /// Delegate that creates the command execution arguemnts. Returns false if the command is not valid.
            /// </summary>
            public TryCreateCommand TryCreateCommand;

            /// <summary>
            /// Delegate that is executed when the command is invoked.
            /// </summary>
            public ExecuteCommand ExecuteCommand;
        }

        private static bool TryCreateImportStatementCommand(
            Workspace workspace,
            PathTable pathTable,
            CodeActionParams actionParams,
            out dynamic[] commandArguments)
        {
            commandArguments = default(dynamic[]);

            var typeChecker = workspace.GetSemanticModel().TypeChecker;

            if (!actionParams.TextDocument.Uri.TryGetSourceFile(workspace, pathTable, out var sourceUri))
            {
                return false;
            }

            Contract.Assert(actionParams.Range?.Start != null);
            if (!DScriptNodeUtilities.TryGetNodeAtPosition(sourceUri, actionParams.Range.Start.ToLineAndColumn(), out var node))
            {
                return false;
            }

            var nodeFlags = NodeUtilities.GetCombinedNodeFlags(node);
            if ((nodeFlags & NodeFlags.ScriptPublic) == NodeFlags.None)
            {
                return false;
            }

            var symbol = typeChecker.GetSymbolAtLocation(node) ?? node.Symbol ?? node.ResolvedSymbol;
            if (symbol == null)
            {
                return false;
            }

            var symbolFullName = typeChecker.GetFullyQualifiedName(symbol);

            // The import statement can only contain a single identifier, so if the symbol's full name
            // is Namespace.Subnamespace.value, we care about just 'Namespace'
            var identifier = GetFirstIdentifier(symbolFullName);

            var module = workspace.TryGetModuleBySpecFileName(sourceUri.GetAbsolutePath(pathTable));
            if (module == null)
            {
                return false;
            }

            var importString = string.Format(
                CultureInfo.InvariantCulture,
                "import {{{0}}} from \"{1}\";",
                identifier,
                module.Definition.Descriptor.Name);

            var bannerString = FormattableStringEx.I($"Import string for '{symbolFullName}' placed on clipboard");

            commandArguments = new dynamic[]
                               {
                                   importString,
                                   bannerString,
                               };

            return true;
        }

        private static string GetFirstIdentifier(string symbolName)
        {
            int indexOfDot = symbolName.IndexOf('.');
            return (indexOfDot > 0) ? symbolName.Substring(0, indexOfDot) : symbolName;
        }

        private static Result<dynamic, ResponseError> ExecuteCreateImportStatementCommand(
            ProviderContext providerContext,
            dynamic[] arguments)
        {
            string importString = arguments[0];
            string bannerString = arguments[1];

            try
            {
                Clipboard.CopyToClipboard(importString);
                Analysis.IgnoreResult(providerContext.ShowMessageAsync(MessageType.Info, bannerString), "Fire and forget");
            }
            catch (Win32Exception e)
            {
                Analysis.IgnoreResult(providerContext.ShowMessageAsync(MessageType.Error, $"Whoops, we couldn't copy import statement to the clipboard. Error: '{e.Message}'"), "Fire and forget");
            }

            return Result.Success;
        }
    }
}
