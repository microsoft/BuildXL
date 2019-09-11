// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Nuget;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Resolver for NuGet packages.
    /// </summary>
    public sealed class NugetResolver : DScriptSourceResolver
    {
        private WorkspaceNugetModuleResolver m_nugetWorkspaceResolver;

        /// <nodoc />
        public NugetResolver(
            FrontEndHost host,
            FrontEndContext context,
            IConfiguration configuration,
            IFrontEndStatistics statistics,
            SourceFileProcessingQueue<bool> parseQueue,
            Logger logger = null,
            IDecorator<EvaluationResult> evaluationDecorator = null)
            : base(host, context, configuration, statistics, parseQueue, logger, evaluationDecorator)
        { }

        /// <inheritdoc/>
        public override async Task<bool> InitResolverAsync(IResolverSettings resolverSettings, object workspaceResolver)
        {
            Contract.Requires(resolverSettings != null);

            Contract.Assert(m_resolverState == State.Created);
            Contract.Assert(
                resolverSettings is INugetResolverSettings,
                I($"Wrong type for resolver settings, expected {nameof(INugetResolverSettings)} but got {nameof(resolverSettings.GetType)}"));

            Name = resolverSettings.Name;
            m_resolverState = State.ResolverInitializing;

            m_nugetWorkspaceResolver = workspaceResolver as WorkspaceNugetModuleResolver;
            Contract.Assert(m_nugetWorkspaceResolver != null, "Workspace module resolver is expected to be of source type");

            // TODO: We could do something smarter in the future and just download/generate what is needed
            // Use this result to populate the dictionaries that are used for package retrieval (m_packageDirectories, m_packages and m_owningModules)
            var maybePackages = await m_nugetWorkspaceResolver.GetAllKnownPackagesAsync();

            if (!maybePackages.Succeeded)
            {
                // Error should have been reported.
                return false;
            }

            m_owningModules = new Dictionary<ModuleId, Package>();

            foreach (var package in maybePackages.Result.Values.SelectMany(v => v))
            {
                m_packages[package.Id] = package;
                m_owningModules[package.ModuleId] = package;
            }

            if (Configuration.FrontEnd.GenerateCgManifestForNugets.IsValid ||
                Configuration.FrontEnd.ValidateCgManifestForNugets.IsValid)
            {
                
                var cgManfiestGenerator = new NugetCgManifestGenerator(Context);
                string generatedCgManifest = cgManfiestGenerator.GenerateCgManifestForPackages(maybePackages.Result);
                string existingCgManifest = "{}";

                try {
                    var existingCgManifestReader = await FrontEndHost.Engine.GetFileContentAsync(Configuration.FrontEnd.GenerateCgManifestForNugets);

                    if (existingCgManifestReader.Succeeded) {
                        existingCgManifest = new string(existingCgManifestReader.Result.Content);
                    }
                }
                catch (BuildXLException) {
                    // CgManifest FileNotFound, continue to write the new file
                    // No operations required as the empty existingCgManifest will not match with the newly generated cgManifest
                }

                if (!cgManfiestGenerator.CompareForEquality(generatedCgManifest, existingCgManifest))
                {
                    System.Diagnostics.Debugger.Launch();
                    // Overwrite or create new cgmanifest.json file with updated nuget package and version info
                    string targetFilePath = Configuration.FrontEnd.GenerateCgManifestForNugets.ToString(Context.PathTable);

                    try
                    {
                        FileUtilities.CreateDirectory(Path.GetDirectoryName(targetFilePath));

                        ExceptionUtilities.HandleRecoverableIOException(
                            () =>
                            {
                        File.WriteAllText(targetFilePath, generatedCgManifest);
                            },
                            e =>
                            {
                                throw new BuildXLException("Cannot write cgmanifest.json file to disk", e);
                            });
                    }
                    catch (BuildXLException e)
                    {
                        // Rijul: Add log here ?

                        //logger.Log.NugetFailedToWriteSpecFileForPackage(
                        //    m_context.LoggingContext,
                        //    package.Id,
                        //    package.Version,
                        //    targetFilePath,
                        //    e.LogEventMessage);
                        //return new NugetFailure(package, NugetFailure.FailureType.WriteSpecFile, e.InnerException);
                    }

                    // Fix:
                    FrontEndHost.Engine.RecordFrontEndFile(
                        Configuration.FrontEnd.GenerateCgManifestForNugets,
                        generatedCgManifest);
                }
                
                // TODO(rijul): based on {Generate|Validate}CgManifestForNugets, decide whether 
                //              to save manifestContent to disk or compare it against an existing file.
                //
                // IMPORTANT: do not use System.IO.File to read/write files; instead:
                //   (1) to read a file, use FrontEndHost.Engine.GetFileContentAsync() 
                //   (2) to write a file, use File.WriteAllText(), and then call FrontEndHost.Engine.RecordFrontEndFile()
                //       (see WorkspaceNugetModuleResolver.TryWriteSourceFile)
            }

            m_resolverState = State.ResolverInitialized;

            return true;
        }
    }
}
