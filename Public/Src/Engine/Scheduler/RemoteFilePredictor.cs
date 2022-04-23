// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Pips.Operations;
using BuildXL.Processes.Remoting;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Class used to collect files to be materialized/pre-rendered in remote agents for process remoting.
    /// </summary>
    internal sealed class RemoteFilePredictor : IRemoteFilePredictor
    {
        private readonly IPipExecutionEnvironment m_pipExecutionEnvironment;
        private readonly IFileContentManagerHost m_fileContentManagerHost;
        private readonly LoggingContext m_loggingContext;

        /// <summary>
        /// Creates an instance of <see cref="RemoteFilePredictor"/>.
        /// </summary>
        /// <param name="environment">Pip execution environment; typically the scheduler.</param>
        /// <param name="fileContentManagerHost">File content manager host; typically the scheduler.</param>
        /// <param name="loggingContext">Logging context.</param>
        public RemoteFilePredictor(IPipExecutionEnvironment environment, IFileContentManagerHost fileContentManagerHost, LoggingContext loggingContext)
        {
            m_pipExecutionEnvironment = environment;
            m_fileContentManagerHost = fileContentManagerHost;
            m_loggingContext = loggingContext;
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<AbsolutePath>> GetInputPredictionAsync(Process process)
        {
            var files = new HashSet<FileArtifact>();
            var dynamicFiles = new MultiValueDictionary<FileArtifact, DirectoryArtifact>();

            m_pipExecutionEnvironment.State.FileContentManager.CollectPipFilesToMaterialize(
                isMaterializingInputs: true,
                pipTable: m_pipExecutionEnvironment.PipTable,
                pip: process,
                files: files,
                dynamicFileMap: dynamicFiles,
                // Do not include directory artifacts.
                // Office tenant word has a partial sealed directories containing ~7,000 output files.
                // Office JS has an output directory containing ~100,000 files.
                shouldInclude: artifact => artifact.IsFile,
                shouldIncludeServiceFiles: servicePipId => false);

            files.UnionWith(dynamicFiles.Keys);

            // Use information from historic metadata cache.
            Optional<IEnumerable<AbsolutePath>> readPaths = await m_fileContentManagerHost.GetReadPathsAsync(OperationContext.CreateUntracked(m_loggingContext), process);

            return files.Select(f => f.Path).Concat(readPaths.HasValue ? readPaths.Value : Enumerable.Empty<AbsolutePath>()).Distinct();
        }
    }
}
