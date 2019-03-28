// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.FrontEnd.Sdk;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Core.Incrementality
{
    /// <summary>
    /// Provides storage facilities for a public facade + serialized AST data for a given spec
    /// </summary>
    public sealed class FrontEndPublicFacadeAndAstProvider : IDisposable
    {
        private const string SpecCacheFileName = "PublicFacadeAndAstCache";

        private const string PublicFacadeFilenameFragment = "facade";
        private const string AstFilenameFragment = "ast";

        private readonly FrontEndEngineAbstraction m_engine;
        private readonly PathTable m_pathTable;
        private readonly IFrontEndStatistics m_statistics;
        private readonly FileCombiner m_fileCombiner;
        private IReadOnlySet<AbsolutePath> m_dirtySpecs;

        private readonly ActionBlock<FileContentWithHash> m_filesToSaveQueue;

        // Need to keep a map between the input to the action block and task completion source.
        private readonly ConcurrentDictionary<FileContentWithHash, TaskSourceSlim<object>> m_saveCompletionTasks = new ConcurrentDictionary<FileContentWithHash, TaskSourceSlim<object>>();

        private bool m_disposed = false;

        private static string SpecCacheFullPath(string frontEndEngineDirectory) => Path.Combine(frontEndEngineDirectory, SpecCacheFileName);

        /// <nodoc/>
        public FrontEndPublicFacadeAndAstProvider(
            FrontEndEngineAbstraction engine,
            LoggingContext loggingContext,
            string frontEndEngineDirectory,
            bool logFrontEndStatistics,
            PathTable pathTable,
            IFrontEndStatistics statistics,
            CancellationToken cancellationToken)
        {
            Contract.Requires(engine != null);
            Contract.Requires(loggingContext != null);
            Contract.Requires(!string.IsNullOrEmpty(frontEndEngineDirectory));
            Contract.Requires(pathTable != null);
            Contract.Requires(statistics != null);

            m_engine = engine;
            m_pathTable = pathTable;
            m_statistics = statistics;
            m_fileCombiner = new FileCombiner(
                loggingContext,
                SpecCacheFullPath(frontEndEngineDirectory),
                FileCombiner.FileCombinerUsage.IncrementalScriptFrontEnd,
                logFrontEndStatistics);

            var queueOptions = new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1, CancellationToken = cancellationToken };

            Action<FileContentWithHash> action = SaveFile;
            m_filesToSaveQueue = new ActionBlock<FileContentWithHash>(action, queueOptions);
        }

        /// <summary>
        /// Tries to retrieve a <see cref="PublicFacadeSpecWithAst"/> from a given path
        /// </summary>
        /// <remarks>
        /// The result is only available if it was stored in the past and the path is not marked as dirty. Returns null otherwise.
        /// </remarks>
        public async Task<PublicFacadeSpecWithAst> TryGetPublicFacadeWithAstAsync(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            // If dirty specs were not set or the requested spec is dirty, then the public facade is not available
            if (IsSpecDirty(path))
            {
                return null;
            }

            var rootPath = path.ToString(m_pathTable);
            var hash = await m_engine.GetFileContentHashAsync(rootPath);

            var publicContent = await TryGetPublicFacadeWithHashAsync(rootPath, hash);

            if (!publicContent.IsValid)
            {
                return null;
            }

            var ast = TryGetSerializedAstWithHashAsync(rootPath, hash);

            // If the first one is available, the second one must also be
            if (!ast.IsValid)
            {
                Contract.Assert(false, I($"The spec '{rootPath}' has public facade representation, but serialized AST is not available."));
            }

            return new PublicFacadeSpecWithAst(path, publicContent, ast);
        }

        /// <summary>
        /// Stores a <see cref="PublicFacadeSpecWithAst"/> for future retrieval
        /// </summary>
        public async Task SavePublicFacadeWithAstAsync(PublicFacadeSpecWithAst publicFacadeWithAst)
        {
            Contract.Requires(publicFacadeWithAst != null);

            await SavePublicFacadeAsync(publicFacadeWithAst.SpecPath, publicFacadeWithAst.PublicFacadeContent);
            await SaveAstAsync(publicFacadeWithAst.SpecPath, publicFacadeWithAst.SerializedAst);
        }

        /// <summary>
        /// Stores a public facade content for future retrieval
        /// </summary>
        public async Task SavePublicFacadeAsync(AbsolutePath path, FileContent publicFacade)
        {
            Contract.Requires(publicFacade.IsValid);

            var rootPath = path.ToString(m_pathTable);
            var hash = await m_engine.GetFileContentHashAsync(rootPath);

            var pathToPublicFacade = Path.Combine(rootPath, PublicFacadeFilenameFragment);
            var content = Encoding.UTF8.GetBytes(publicFacade.Content);

            var fileWithContent = new FileContentWithHash(ByteContent.Create(content, content.Length), hash, pathToPublicFacade);

            // Need to store a task completion source to be able to await for the save operation to finish
            var tcs = TaskSourceSlim.Create<object>();

            m_saveCompletionTasks[fileWithContent] = tcs;
            m_filesToSaveQueue.Post(fileWithContent);

            await tcs.Task;
        }

        /// <summary>
        /// Stores a serialized AST for future retrieval
        /// </summary>
        public async Task SaveAstAsync(AbsolutePath path, ByteContent content)
        {
            var rootPath = path.ToString(m_pathTable);
            var hash = await m_engine.GetFileContentHashAsync(rootPath);

            var pathToAst = Path.Combine(rootPath, AstFilenameFragment);

            var fileWithContent = new FileContentWithHash(content, hash, pathToAst);

            // Need to store a task completion source to be able to await for the save operation to finish
            var tcs = TaskSourceSlim.Create<object>();

            m_saveCompletionTasks[fileWithContent] = tcs;
            m_filesToSaveQueue.Post(fileWithContent);

            await tcs.Task;

            // Now we can remove the item from the dictionary.
            m_saveCompletionTasks.TryRemove(fileWithContent, out var _);
        }

        /// <summary>
        /// Notifies that a collection of paths are not safe for retrieval.
        /// </summary>
        /// <remarks>
        /// This provider assumes all paths are not safe until notified otherwise. After this notification
        /// is called, all paths outside of the provided collection are assumed to be safe
        /// </remarks>
        public void NotifySpecsCannotBeUsedAsFacades(IEnumerable<AbsolutePath> absolutePaths)
        {
            Contract.Assert(m_dirtySpecs == null, "Dirty specs can be notified only once");
            m_dirtySpecs = absolutePaths.ToReadOnlySet();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_disposed = true;

                // Make sure the saving queue is complete on disposing
                m_filesToSaveQueue.Complete();

                try
                {
                    m_filesToSaveQueue.Completion.GetAwaiter().GetResult();
                }
                catch (TaskCanceledException)
                {
                    // Expected. This means user cancellation
                }

                m_fileCombiner.Dispose();
            }
        }

        /// <summary>
        /// Deletes the cache file.
        /// </summary>
        internal static void PurgeCache(string frontEndEngineDirectory)
        {
            var facadeAndAstCacheFile = SpecCacheFullPath(frontEndEngineDirectory);
            if (File.Exists(facadeAndAstCacheFile))
            {
                FileUtilities.DeleteFile(facadeAndAstCacheFile);
            }
        }

        private async Task<FileContent> TryGetPublicFacadeWithHashAsync(string rootPath, ContentHash hash)
        {
            using (m_statistics.PublicFacadeHits.Start())
            {
                var pathToPublicFacade = Path.Combine(rootPath, PublicFacadeFilenameFragment);

                var publicFacade = m_fileCombiner.RequestFile(pathToPublicFacade, hash);
                if (publicFacade == null)
                {
                    return FileContent.Invalid;
                }

                var content = await FileContent.ReadFromAsync(publicFacade);

                return content;
            }
        }

        private ByteContent TryGetSerializedAstWithHashAsync(string rootPath, ContentHash hash)
        {
            using (m_statistics.SerializedAstHits.Start())
            {
                var pathToAst = Path.Combine(rootPath, AstFilenameFragment);
                var ast = m_fileCombiner.RequestFile(pathToAst, hash)?.ToArray();

                if (ast == null)
                {
                    return ByteContent.Invalid;
                }

                return ByteContent.Create(ast, ast.Length);
            }
        }

        private bool IsSpecDirty(AbsolutePath path)
        {
            return m_dirtySpecs == null || m_dirtySpecs.Contains(path);
        }

        private void SaveFile(FileContentWithHash fileContentWithHash)
        {
            // Extracting a task completion source to notify the producer that the operation is complete/fail
            bool tcsFound = m_saveCompletionTasks.TryRemove(fileContentWithHash, out var tcs);
            Contract.Assert(tcsFound, "Can't find a task completion source.");

            try
            {
                // We only add the file content if it is not already there (with same path and hash)
                m_fileCombiner.GetOrAddFile(fileContentWithHash.Content.Content, fileContentWithHash.Hash, fileContentWithHash.PathToFile, fileContentWithHash.Content.Length);
                tcs.TrySetResult(null);
            }
            catch (Exception e)
            {
                tcs.TrySetException(e);
            }
        }
    }

    /// <summary>
    /// Structure that gets queued in the file-to-save queue
    /// </summary>
    internal readonly struct FileContentWithHash : IEquatable<FileContentWithHash>
    {
        /// <nodoc/>
        public ByteContent Content { get; }

        /// <nodoc/>
        public ContentHash Hash { get; }

        /// <nodoc/>
        public string PathToFile { get; }

        /// <nodoc/>
        public FileContentWithHash(ByteContent content, ContentHash hash, string pathToFile)
        {
            Content = content;
            Hash = hash;
            PathToFile = pathToFile;
        }

        /// <inheritdoc />
        public bool Equals(FileContentWithHash other)
        {
            return Content.Equals(other.Content) && Hash.Equals(other.Hash) && string.Equals(PathToFile, other.PathToFile);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is FileContentWithHash && Equals((FileContentWithHash) obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Content.GetHashCode(), Hash.GetHashCode(), PathToFile?.GetHashCode() ?? 0);
        }

        /// <nodoc />
        public static bool operator ==(FileContentWithHash left, FileContentWithHash right) => left.Equals(right);

        /// <nodoc />
        public static bool operator !=(FileContentWithHash left, FileContentWithHash right) => !left.Equals(right);
    }
}
