// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.Host.Service.Internal
{
    /// <summary>
    /// Session which aggregates a local and backing content store. The backing content store is
    /// used to populate local content store in cases of local misses.
    /// </summary>
    public class MultiLevelContentSession : MultiLevelReadOnlyContentSession<IContentSession>, IContentSession
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiLevelContentSession"/> class.
        /// </summary>
        public MultiLevelContentSession(
            string name,
            IContentSession localSession,
            IContentSession backingSession)
            : base(name, localSession, backingSession, isLocalWritable: true)
        {
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutFileCoreAsync(OperationContext operationContext, ContentHash contentHash, AbsolutePath path, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return MultiLevelWriteAsync(session => session.PutFileAsync(
                operationContext,
                contentHash,
                path,
                CoerceRealizationMode(realizationMode, session),
                operationContext.Token,
                urgencyHint));
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutFileCoreAsync(OperationContext operationContext, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return MultiLevelWriteAsync(session => session.PutFileAsync(
                operationContext,
                hashType,
                path,
                CoerceRealizationMode(realizationMode, session),
                operationContext.Token,
                urgencyHint));
        }

        private FileRealizationMode CoerceRealizationMode(FileRealizationMode mode, IContentSession session)
        {
            // Backing session may likely be on a different volume. Don't enforce the same rules around FileRealizationMode.
            if (mode == FileRealizationMode.HardLink && session == BackingSession)
            {
                return FileRealizationMode.Any;
            }

            return mode;
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(OperationContext operationContext, ContentHash contentHash, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return MultiLevelWriteAsync(session => session.PutStreamAsync(operationContext, contentHash, stream, operationContext.Token, urgencyHint));
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(OperationContext operationContext, HashType hashType, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return MultiLevelWriteAsync(session => session.PutStreamAsync(operationContext, hashType, stream, operationContext.Token, urgencyHint));
        }

        private async Task<PutResult> MultiLevelWriteAsync(Func<IContentSession, Task<PutResult>> writeAsync)
        {
            await writeAsync(BackingSession).ThrowIfFailureAsync();
            return await writeAsync(LocalSession);
        }
    }
}
