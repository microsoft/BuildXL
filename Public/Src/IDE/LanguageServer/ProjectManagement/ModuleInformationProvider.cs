// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Ide.JsonRpc;
using JetBrains.Annotations;
using LanguageServer;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

using BuildXLModuleDescriptor = BuildXL.FrontEnd.Workspaces.Core.ModuleDescriptor;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// Contains the JSON/RPC methods for retrieving information about project spec files.
    /// </summary>
    public class ModuleInformationProvider
    {
        private readonly GetAppState m_getAppState;

        /// <nodoc/>
        public ModuleInformationProvider([NotNull] GetAppState getAppState)
        {
            m_getAppState = getAppState;
        }

        /// <summary>
        /// Returns the module descriptors for files present in the BuildXL workspace
        /// </summary>
        /// <remarks>
        /// This extends the language server protocol.
        /// </remarks>
        [JsonRpcMethod("dscript/modulesForWorkspace")]
        protected Result<ModulesForWorkspaceResult, ResponseError> GetModules(JToken token)
        {
            var appState = m_getAppState();
            if (appState == null)
            {
                return Result<ModulesForWorkspaceResult, ResponseError>.Error(new ResponseError
                {
                    code = ErrorCodes.InternalError,
                    message = BuildXL.Ide.LanguageServer.Strings.WorkspaceParsingFailedCannotPerformAction,
                });
            }

            var modulesForWorkspaceParams = token.ToObject<ModulesForWorkspaceParams>();

            var workspace = appState.IncrementalWorkspaceProvider.WaitForRecomputationToFinish();
            var workspaceModules = new List<BuildXL.Ide.JsonRpc.ModuleDescriptor>(workspace.Modules.Count);
            foreach(var mod in workspace.Modules)
            {
                if (mod.Descriptor.IsSpecialConfigModule() && !modulesForWorkspaceParams.IncludeSpecialConfigurationModules)
                {
                    continue;
                }

                workspaceModules.Add(new  BuildXL.Ide.JsonRpc.ModuleDescriptor
                                     {
                                         Name = mod.Descriptor.Name,
                                         Id = mod.Descriptor.Id.Value.Value,
                                         ConfigFilename = mod.Definition.ModuleConfigFile.ToString(appState.PathTable),
                                         ResolverKind = mod.Descriptor.ResolverKind,
                                         ResolverName = mod.Descriptor.ResolverName
                });
            }

            return Result<ModulesForWorkspaceResult, ResponseError>.Success(new ModulesForWorkspaceResult { Modules = workspaceModules.ToArray() } );
        }

        /// <summary>
        /// Returns the BuildXL spec files descriptors for a particular module.
        /// </summary>
        /// <remarks>
        /// This extends the language server protocol.
        /// </remarks>
        [JsonRpcMethod("dscript/specsForModule")]
        protected Result<SpecsFromModuleResult, ResponseError> getSpecsForModule(JToken token)
        {
            var appState = m_getAppState();
            if (appState == null)
            {
                return Result<SpecsFromModuleResult, ResponseError>.Error(new ResponseError
                {
                    code = ErrorCodes.InternalError,
                    message = BuildXL.Ide.LanguageServer.Strings.WorkspaceParsingFailedCannotPerformAction,
                });
            }

            var getSpecsForModuleParams = token.ToObject<SpecsForModuleParams>();

            var workspace = appState.IncrementalWorkspaceProvider.WaitForRecomputationToFinish();

            var moduleDescriptor = 

            workspace.TryGetModuleByModuleDescriptor(
                new BuildXLModuleDescriptor(
                    id: ModuleId.UnsafeCreate(getSpecsForModuleParams.ModuleDescriptor.Id),
                    name: getSpecsForModuleParams.ModuleDescriptor.Name,
                    displayName: getSpecsForModuleParams.ModuleDescriptor.Name,
                    version: getSpecsForModuleParams.ModuleDescriptor.Version, 
                    resolverKind: getSpecsForModuleParams.ModuleDescriptor.ResolverKind,
                    resolverName: getSpecsForModuleParams.ModuleDescriptor.ResolverName),
                out var specificModule);
            if (specificModule != null)
            {
                var specDescriptors = new List<SpecDescriptor>(specificModule.Specs.Count);
                foreach (var spec in specificModule.Specs)
                {
                    specDescriptors.Add(new SpecDescriptor { FileName = spec.Value.FileName, Id = spec.Value.Id });
                }
                return Result<SpecsFromModuleResult, ResponseError>.Success(new SpecsFromModuleResult { Specs = specDescriptors.ToArray() });
            }

            return Result<SpecsFromModuleResult, ResponseError>.Error(new ResponseError
            {
                code = ErrorCodes.InvalidParams,
                message = string.Format(BuildXL.Ide.LanguageServer.Strings.ModuleNotFoundInWorkspace, getSpecsForModuleParams.ModuleDescriptor.Name)
            });
        }
    }
}
