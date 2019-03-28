// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Core.Tracing;
using BuildXL.FrontEnd.Sdk;

namespace BuildXL.FrontEnd.Core.Incrementality
{
    /// <inheritdoc/>
    public sealed class FrontEndArtifactManager : IFrontEndArtifactManager
    {
        private readonly FrontEndEngineAbstraction m_engine;
        private readonly FrontEndCache m_frontEndCache;
        private readonly FrontEndPublicFacadeAndAstProvider m_publicFacadeAndAstProvider;

        /// <nodoc/>
        public FrontEndArtifactManager(
            FrontEndEngineAbstraction engine,
            string frontEndEngineDirectory,
            Logger logger,
            LoggingContext loggingContext,
            IFrontEndStatistics frontEndStatistics,
            PathTable pathTable,
            IFrontEndConfiguration configuration,
            CancellationToken cancellationToken)
        {
            Contract.Requires(engine != null);
            Contract.Requires(loggingContext != null);
            Contract.Requires(frontEndStatistics != null);
            Contract.Requires(!string.IsNullOrEmpty(frontEndEngineDirectory));
            Contract.Requires(pathTable != null);

            m_engine = engine;
            m_frontEndCache = new FrontEndCache(frontEndEngineDirectory, logger, loggingContext, frontEndStatistics, pathTable);

            // If engine state changed, delete facade and ast cache, because from one run to another the serialized AST bytes
            // can be different even if corresponding DScript sourced didn't change (reason: PathTable can change in the meantime)
            if (!engine.IsEngineStatePartiallyReloaded())
            {
                FrontEndPublicFacadeAndAstProvider.PurgeCache(frontEndEngineDirectory);
            }

            // If public facades are not used, then we don't create the public facade provider. This avoids
            // creating the public facade cache.
            // In particular, the IDE never uses this optimization, and the cache needs an exclusive lock.
            if (configuration.UseSpecPublicFacadeAndAstWhenAvailable())
            {
                m_publicFacadeAndAstProvider = new FrontEndPublicFacadeAndAstProvider(
                    engine,
                    loggingContext,
                    frontEndEngineDirectory,
                    configuration.LogStatistics,
                    pathTable,
                    frontEndStatistics,
                    cancellationToken);
            }
        }

        /// <summary>
        /// <see cref="FrontEndCache.TryLoadFrontEndSnapshot"/>
        /// </summary>
        public IWorkspaceBindingSnapshot TryLoadFrontEndSnapshot(int expectedSpecCount)
        {
            return m_frontEndCache.TryLoadFrontEndSnapshot(expectedSpecCount);
        }

        /// <summary>
        /// <see cref="FrontEndCache.SaveFrontEndSnapshot"/>
        /// </summary>
        public void SaveFrontEndSnapshot(IWorkspaceBindingSnapshot snapshot)
        {
            m_frontEndCache.SaveFrontEndSnapshot(snapshot);
        }

        /// <summary>
        /// <see cref="FrontEndPublicFacadeAndAstProvider.TryGetPublicFacadeWithAstAsync"/>
        /// </summary>
        public Task<PublicFacadeSpecWithAst> TryGetPublicFacadeWithAstAsync(AbsolutePath path)
        {
            Contract.Assert(m_publicFacadeAndAstProvider != null);
            return m_publicFacadeAndAstProvider.TryGetPublicFacadeWithAstAsync(path);
        }

        /// <summary>
        /// <see cref="FrontEndPublicFacadeAndAstProvider.SavePublicFacadeWithAstAsync"/>
        /// </summary>
        public Task SavePublicFacadeWithAstAsync(PublicFacadeSpecWithAst publicFacadeWithAst)
        {
            Contract.Assert(m_publicFacadeAndAstProvider != null);
            return m_publicFacadeAndAstProvider.SavePublicFacadeWithAstAsync(publicFacadeWithAst);
        }

        /// <summary>
        /// <see cref="FrontEndPublicFacadeAndAstProvider.SavePublicFacadeAsync"/>
        /// </summary>
        public Task SavePublicFacadeAsync(AbsolutePath path, FileContent publicFacade)
        {
            Contract.Assert(m_publicFacadeAndAstProvider != null);
            return m_publicFacadeAndAstProvider.SavePublicFacadeAsync(path, publicFacade);
        }

        /// <summary>
        /// <see cref="FrontEndPublicFacadeAndAstProvider.SaveAstAsync"/>
        /// </summary>
        public Task SaveAstAsync(AbsolutePath path, ByteContent content)
        {
            Contract.Assert(m_publicFacadeAndAstProvider != null);
            return m_publicFacadeAndAstProvider.SaveAstAsync(path, content);
        }

        /// <summary>
        /// <see cref="FrontEndPublicFacadeAndAstProvider.NotifySpecsCannotBeUsedAsFacades"/>
        /// </summary>
        public void NotifySpecsCannotBeUsedAsFacades(IEnumerable<AbsolutePath> absolutePaths)
        {
            Contract.Assert(m_publicFacadeAndAstProvider != null);
            m_publicFacadeAndAstProvider.NotifySpecsCannotBeUsedAsFacades(absolutePaths);
        }

        /// <summary>
        /// <see cref="FrontEndEngineAbstraction.GetFileContentAsync"/>.
        /// </summary>
        public Task<Possible<FileContent, RecoverableExceptionFailure>> TryGetFileContentAsync(AbsolutePath path)
        {
            return m_engine.GetFileContentAsync(path);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_publicFacadeAndAstProvider?.Dispose();
        }
    }
}
