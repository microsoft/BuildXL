// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Abstract class for FrontEnd host implementations.
    /// This class exposes all the objects needed by a FrontEnd
    /// </summary>
    /// <remarks>
    /// This is not the interface that the engine uses to talk to the host.
    /// That is interface IFrontEndController" /> </remarks>
    public abstract class FrontEndHost
    {
        /// <summary>
        /// Has all the specs with implicit name visibility semantic (i.e. all the specs that belong to V2 modules).
        /// </summary>
        private IReadOnlySet<AbsolutePath> m_specsWithImplicitNameVisibility;

        private IWorkspace m_workspace;

        /// <summary>
        /// Name of the designated module that acts as the prelude
        /// </summary>
        public const string PreludeModuleName = "Sdk.Prelude";

        /// <summary>
        /// Stores the environment variables used in the config file
        /// </summary>
        /// <remarks>
        /// This is temporary fix to get around the lack of engine during parsing the config file.
        /// At the end of evaluation, these variables will be recorded in the Engine.
        /// The key in the structure is the name of the environment variable. The string in the value (Item2)
        /// is the value of the environment variable. The boolean in the value (Item1) is used to track
        /// whether that variable was actually used when evaluating the configuration file. This is used by graph caching
        /// to ensure that we reload the graph when one of those environment variables changes between builds.
        /// </remarks>
        public ConcurrentDictionary<string, (bool valueUsed, string value)> EnvVariablesUsedInConfig = new ConcurrentDictionary<string, (bool valueUsed, string value)>();

        /// <summary>
        /// Stores the enumerated directories in the config file
        /// </summary>
        /// <remarks>
        /// This is temporary fix to get around the lack of engine during parsing the config file.
        /// At the end of evaluation, these directories will be tracked in the Engine.w
        /// </remarks>
        public ConcurrentDictionary<string, IReadOnlyList<(string, FileAttributes)>>
            EnumeratedDirectoriesInConfig =
                new ConcurrentDictionary<string, IReadOnlyList<(string, FileAttributes)>>();

        /// <summary>
        /// Configuration for the frontend
        /// </summary>
        public IFrontEndConfiguration FrontEndConfiguration => Configuration.FrontEnd;

        /// <summary>
        /// Global configuration object
        /// </summary>
        /// <remarks>
        /// Useful for deciding default values for options outside of the front-end configuration
        /// </remarks>
        public IConfiguration Configuration { get; protected set; }

        /// <summary>
        /// The global module registry
        /// </summary>
        public IModuleRegistry ModuleRegistry { get; protected set; }

        /// <summary>
        /// Engine.
        /// </summary>
        public FrontEndEngineAbstraction Engine { get; protected set; }

        /// <summary>
        /// Front end artifact manager
        /// </summary>
        public IFrontEndArtifactManager FrontEndArtifactManager { get; protected set; }

        /// <summary>
        /// PipGraph to construct.
        /// </summary>
        public IPipGraph PipGraph { get; protected set; }

        /// <summary>
        /// Handles adding pip fragments to the build.
        /// </summary>
        public IPipGraphFragmentManager PipGraphFragmentManager { get; protected set; }

        /// <nodoc />
        public IEvaluationScheduler DefaultEvaluationScheduler { get; protected set; }

        /// <summary>
        /// Gets a unique folder in the object root with the given attempted friendly name.
        /// </summary>
        /// <returns>The result is a valid <see cref="AbsolutePath"/></returns>
        public abstract AbsolutePath GetFolderForFrontEnd([NotNull]string friendlyName);

        /// <summary>
        /// Downloads a file for frontends with cache support.
        /// </summary>
        public abstract Task<Possible<ContentHash>> DownloadFile([NotNull]string url, AbsolutePath targetLocation, ContentHash? expectedContentHash, [NotNull]string friendlyName);

        /// <summary>
        /// Downloads a package for frontends with cache support.
        /// </summary>
        public abstract Task<Possible<PackageDownloadResult>> DownloadPackage(
            string weakPackageFingerprint,
            PackageIdentity package,
            AbsolutePath packageTargetFolder,
            Func<Task<Possible<IReadOnlyList<RelativePath>>>> producePackage);

        /// <summary>
        /// The path to the primary config file
        /// </summary>
        public AbsolutePath PrimaryConfigFile { get; protected set; }

        /// <summary>
        /// Computed workspace
        /// </summary>
        public IWorkspace Workspace
        {
            get
            {
                return m_workspace;
            }

            protected set
            {
                m_workspace = value;
                if (value != null)
                {
                    m_specsWithImplicitNameVisibility = value.GetSpecFilesWithImplicitNameVisibility();
                }
            }
        }

        /// <summary>
        /// Returns whether the spec belongs to a module with implicit reference semantics
        /// </summary>
        public bool SpecBelongsToImplicitSemanticsModule(AbsolutePath specPath)
        {
            return m_specsWithImplicitNameVisibility?.Contains(specPath) == true;
        }

        /// <summary>
        /// Determines whether coercion happening on a given specPath should use defaults
        /// </summary>
        public bool ShouldUseDefaultsOnCoercion(AbsolutePath specPath)
        {
            // Can't use Workspace here, because it can be collected.
            return m_specsWithImplicitNameVisibility == null || !m_specsWithImplicitNameVisibility.Contains(specPath);
        }

        /// <summary>
        /// The cycle detector used throughout evaluation
        /// </summary>
        public ICycleDetector CycleDetector { get; protected set; }

        /// <summary>
        /// Public facades and serialized ASTs can be used when: 1) the general flag for this optimization is on and 2) the engine partial state has been successfuly reloaded, so we
        /// are sure tables are stable across builds
        /// </summary>
        public bool CanUseSpecPublicFacadeAndAst()
        {
            // The Engine is null during configuration parsing
            return FrontEndConfiguration.UseSpecPublicFacadeAndAstWhenAvailable() && Engine?.IsEngineStatePartiallyReloaded() == true;
        }
    }
}
