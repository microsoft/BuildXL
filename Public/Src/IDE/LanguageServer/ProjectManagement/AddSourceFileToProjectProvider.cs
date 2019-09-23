// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Script.Analyzer.Analyzers;
using BuildXL.Ide.JsonRpc;
using JetBrains.Annotations;
using LanguageServer;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;
using DScriptUtilities = TypeScript.Net.DScript.Utilities;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// Contains the JSON/RPC methods for adding a source file to a project file.
    /// </summary>
    public class AddSourceFileToProjectProvider
    {
        private readonly GetAppState m_getAppState;

        /// <nodoc/>
        public AddSourceFileToProjectProvider([NotNull] GetAppState getAppState)
        {
            m_getAppState = getAppState;
        }

        /// <summary>
        /// Contains the configuration properties needed for Add Source File to function.
        /// </summary>
        private AddSourceFileConfigurationParams m_addSourceFileConfiguration;

        /// <summary>
        /// Receives the configuration information needed by the dscript/addSourceFileProject request
        /// to function.
        /// </summary>
        /// <remarks>
        /// This extends the language server protocol. The information contained in the message is
        /// required for the add source file project request to properly function. The information
        /// contains things like the function, module, paremeter, etc. that the add source file to project
        /// implementation searches the DScript abstract syntax tree to located the proper place
        /// to add the source file.
        /// </remarks>
        [JsonRpcMethod("dscript/sourceFileConfiguration")]
        protected void OnSourceFileConfigurationNotification(JToken token)
        {
            m_addSourceFileConfiguration = token.ToObject<AddSourceFileConfigurationParams>();
        }

        /// <summary>
        /// Inserts a source file name into a project specification file.
        /// </summary>
        /// <remarks>
        /// This extends the language server protocol. This request searches the DScript
        /// abstract syntax tree using the information provided by the dscript/addSourceFileConfiguration
        /// request to locate the proper location in a project specification file to insert
        /// the requested source file name.
        /// </remarks>
        [JsonRpcMethod("dscript/addSourceFileToProject")]
        protected Result<TextEdit[], ResponseError> AddSourceFileToProject(JToken token)
        {
            if (m_addSourceFileConfiguration == null || m_addSourceFileConfiguration.Configurations.Length == 0)
            {
                return Result<TextEdit[], ResponseError>.Error(new ResponseError
                {
                    code = ErrorCodes.InvalidRequest,
                    message = BuildXL.Ide.LanguageServer.Strings.AddSourceFileConfigurationNotReceived,
                });
            }

            var appState = m_getAppState();
            if (appState == null)
            {
                return Result<TextEdit[], ResponseError>.Error(new ResponseError
                {
                    code = ErrorCodes.InternalError,
                    message = BuildXL.Ide.LanguageServer.Strings.WorkspaceParsingFailedCannotPerformAction,
                });
            }

            var addSourceFileParams = token.ToObject<AddSourceFileToProjectParams>();

            var workspace = appState.IncrementalWorkspaceProvider.WaitForRecomputationToFinish();
            var uri = new Uri(addSourceFileParams.ProjectSpecFileName);

            if (uri.TryGetSourceFile(workspace, appState.PathTable, out var projectSourceFile))
            {
                var checker = workspace.GetSemanticModel().TypeChecker;
                if (TryAddSourceFileToSourceFile(
                    checker,
                    projectSourceFile,
                    addSourceFileParams.RelativeSourceFilePath,
                    workspace,
                    appState.PathTable,
                    m_addSourceFileConfiguration.Configurations))
                {
                    var formattedText = PrettyPrint.GetFormattedText(projectSourceFile);

                    var textEdit = new TextEdit
                    {
                        NewText = formattedText,
                        Range = projectSourceFile.ToRange(),
                    };

                    return Result<TextEdit[], ResponseError>.Success(new[] { textEdit });
                }
            }

            return Result<TextEdit[], ResponseError>.Success(null);
        }

        private static bool IsTypeCorrectForAddSourceFileConfiguration(IType type, Workspace workspace, PathTable pathTable, AddSourceFileConfiguration configuration)
        {
            if (type.Symbol?.Declarations?.FirstOrDefault()?.Name?.Text == configuration.ArgumentTypeName)
            {
                var sourceFilename = type.Symbol?.Declarations?.FirstOrDefault()?.SourceFile?.FileName;
                if (sourceFilename != null)
                {
                    var parsedMoudle = workspace.TryGetModuleBySpecFileName(AbsolutePath.Create(pathTable, sourceFilename));
                    if (parsedMoudle != null)
                    {
                        if (parsedMoudle?.Descriptor.Name == configuration.ArgumentTypeModuleName)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to add a file to a source file list. The conditions that must be met are specified in the
        /// array of <paramref name="configurations"/>
        /// </summary>
        public static bool TryAddSourceFileToSourceFile(ITypeChecker checker, ISourceFile sourceFile, string sourceFileName, Workspace workspace, PathTable pathTable, AddSourceFileConfiguration[] configurations)
        {
            INode sourcesNode = null;
            try
            {
                // Use single or default to ensure that we only match a single sources property.
                // If we find more than one, we don't know which source file list to augment.
                // SingleOrDefault throws an InvalidOperationException if it finds more than one element
                // and returns default<T> if there are 0.
                sourcesNode = NodeWalker.TraverseBreadthFirstAndSelf(sourceFile).SingleOrDefault(node =>
                {
                    // We expect that the property for the source file list to be in an object literal
                    // and hence be a property assignment inside that object literal.
                    // The statement will look something like:
                    // const result = TargetType.build( { sources: [f`foo.cpp`] } );
                    if (node.Kind == SyntaxKind.PropertyAssignment &&
                        node.Cast<IPropertyAssignment>().Name.Kind == SyntaxKind.Identifier &&
                        node.Parent?.Kind == SyntaxKind.ObjectLiteralExpression)
                    {
                        var propertyName = node.Cast<IPropertyAssignment>().Name.Text;

                        // Now check the configurations to see if the any match as there
                        // can be different names (such as "references", "sources", etc.) as
                        // well as different functions, etc.

                        AddSourceFileConfiguration singleConfiguration = null;

                        try
                        {
                            // We use single or default to ensure that only one matching configuration is found.
                            // SingleOrDefault throws an InvalidOperationException if it finds more than one element
                            // and returns default<T> if there are 0.
                            singleConfiguration = configurations.SingleOrDefault(configuration =>
                            {
                                // Check to see if this is the correct property name.
                                if (propertyName != configuration.PropertyName)
                                {
                                    return false;
                                }

                                // Now we will try to find the matching call expression (function name)
                                // The reason we are going to walk parent nodes is that we allow
                                // a "merge" or "override" to be nested inside the function call
                                // as long as the argument type and the expected module the type exists
                                // in match the configuration parameter.
                                var nodeParent = node.Parent.Parent;
                                while (nodeParent != null)
                                {
                                    if (nodeParent.Kind != SyntaxKind.CallExpression)
                                    {
                                        return false;
                                    }

                                    var callExpression = nodeParent.Cast<ICallExpression>();
                                    string calledFunction = string.Empty;

                                    // Depending on the module the function is being called from it may be a straight
                                    // call (such as "build()") or it could be an accessor if it was imported
                                    // from another module (such as "StaticLibrary.build()").
                                    if (callExpression.Expression?.Kind == SyntaxKind.PropertyAccessExpression)
                                    {
                                        var propertyAccessExpression = callExpression.Expression.Cast<IPropertyAccessExpression>();
                                        calledFunction = propertyAccessExpression.Name?.Text;
                                    }
                                    else if (callExpression.Expression?.Kind == SyntaxKind.Identifier)
                                    {
                                        calledFunction = callExpression.Expression.Cast<Identifier>().Text;
                                    }
                                    else
                                    {
                                        return false;
                                    }

                                    // If the called function matches, and has the minimum number of parameters to contain our argument type
                                    // then verify it matches the type name given in the configuration.
                                    if (calledFunction == configuration.FunctionName && callExpression.Arguments?.Length > configuration.ArgumentPosition)
                                    {
                                        var type = checker.GetContextualType(callExpression.Arguments[configuration.ArgumentPosition]);
                                        if (type != null && IsTypeCorrectForAddSourceFileConfiguration(type, workspace, pathTable, configuration))
                                        {
                                            return true;
                                        }
                                    }
                                    else if (DScriptUtilities.IsMergeOrOverrideCallExpression(callExpression))
                                    {
                                        // In the case of a merge or override function, we make sure it is the proper type and keep moving
                                        // up the parent chain to find the function call.
                                        var type = checker.GetTypeAtLocation(callExpression.TypeArguments[0]);
                                        if (type != null && IsTypeCorrectForAddSourceFileConfiguration(type, workspace, pathTable, configuration))
                                        {
                                            nodeParent = nodeParent.Parent;
                                            continue;
                                        }
                                    }

                                    return false;
                                }

                                return false;
                            });
                        }
                        catch (InvalidOperationException)
                        {
                            return false;
                        }

                        return singleConfiguration != null;

                    }

                    return false;
                });
            }
            catch (InvalidOperationException)
            {
            }

            if (sourcesNode != null)
            {
                var propertyAssignment = sourcesNode.Cast<IPropertyAssignment>();
                // Will support array literals for now.
                var initializer = propertyAssignment.Initializer.As<IArrayLiteralExpression>();
                if (initializer == null)
                {
                    // TODO: potentially we could have a glob call here, and what we can do this:
                    // [...(oldExpression), newFile]
                    return false;
                }

                var alreadyPresent = initializer.Elements.Any(element =>
                {
                    return (element.Kind == SyntaxKind.TaggedTemplateExpression &&
                            element.Cast<ITaggedTemplateExpression>().Template?.Text.Equals(sourceFileName, StringComparison.OrdinalIgnoreCase) == true);
                });

                if (!alreadyPresent)
                {
                    initializer.Elements.Add(new TaggedTemplateExpression("f", sourceFileName));
                    return true;
                }
            }
            return false;
        }

    }
}
