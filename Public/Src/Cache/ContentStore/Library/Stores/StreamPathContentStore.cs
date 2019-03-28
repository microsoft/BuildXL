// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     A <see cref="TwoContentStore" /> implemented as two <see cref="FileSystemContentStore" />'s split based on whether
    ///     contents are stored through streams or paths.
    /// </summary>
    public sealed class StreamPathContentStore : TwoContentStore, IAcquireDirectoryLock
    {
        private readonly ContentStoreTracer _tracer = new ContentStoreTracer(nameof(StreamPathContentStore));

        private IContentStore ContentStoreForStream => ContentStore1;

        private IContentStore ContentStoreForPath => ContentStore2;

        /// <summary>
        ///     Name of content store for streams.
        /// </summary>
        public const string NameOfContentStoreForStream = nameof(ContentStoreForStream);

        /// <summary>
        ///     Name of content store for paths.
        /// </summary>
        public const string NameOfContentStoreForPath = nameof(ContentStoreForPath);

        /// <inheritdoc />
        protected override string NameOfContentStore1 => NameOfContentStoreForStream;

        /// <inheritdoc />
        protected override string NameOfContentStore2 => NameOfContentStoreForPath;

        /// <summary>
        ///     Initializes a new instance of the <see cref="StreamPathContentStore" /> class.
        /// </summary>
        public StreamPathContentStore(
            Func<FileSystemContentStore> factoryOfContentStoreForStream,
            Func<FileSystemContentStore> factoryOfContentStoreForPath)
            : base(factoryOfContentStoreForStream, factoryOfContentStoreForPath)
        {
        }

        /// <inheritdoc />
        protected override ContentStoreTracer Tracer => _tracer;

        /// <inheritdoc />
        public override CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(
            Context context,
            string name,
            ImplicitPin implicitPin)
        {
            return CreateReadOnlySessionCall.Run(_tracer, new OperationContext(context), name, () =>
            {
                var sessionForStream = ContentStoreForStream.CreateSession(context, name, implicitPin);
                if (!sessionForStream.Succeeded)
                {
                    return new CreateSessionResult<IReadOnlyContentSession>(sessionForStream, "creation of stream content session failed");
                }

                var sessionForPath = ContentStoreForPath.CreateSession(context, name, implicitPin);
                if (!sessionForPath.Succeeded)
                {
                    return new CreateSessionResult<IReadOnlyContentSession>(sessionForPath, "creation of path content session failed");
                }

                var session = new StreamPathContentSession(
                    name,
                    sessionForStream.Session,
                    sessionForPath.Session);

                return new CreateSessionResult<IReadOnlyContentSession>(session);
            });
        }

        /// <inheritdoc />
        public override CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCall.Run(_tracer, new OperationContext(context), name, () =>
            {
                var sessionForStream = ContentStoreForStream.CreateSession(context, name, implicitPin);
                if (!sessionForStream.Succeeded)
                {
                    return new CreateSessionResult<IContentSession>(sessionForStream, "creation of stream content session failed");
                }

                var sessionForPath = ContentStoreForPath.CreateSession(context, name, implicitPin);
                if (!sessionForPath.Succeeded)
                {
                    return new CreateSessionResult<IContentSession>(sessionForPath, "creation of path content session failed");
                }

                var session = new StreamPathContentSession(
                    name,
                    sessionForStream.Session,
                    sessionForPath.Session);

                return new CreateSessionResult<IContentSession>(session);
            });
        }

        /// <inheritdoc />
        public async Task<BoolResult> AcquireDirectoryLockAsync(Context context)
        {
            var contentStoreForStream = ContentStoreForStream as IAcquireDirectoryLock;
            if (contentStoreForStream != null)
            {
                var acquireLockResult = await contentStoreForStream.AcquireDirectoryLockAsync(context);
                if (!acquireLockResult.Succeeded)
                {
                    return acquireLockResult;
                }
            }

            if (ContentStoreForPath is IAcquireDirectoryLock contentStoreForPath)
            {
                var acquireLockResult = await contentStoreForPath.AcquireDirectoryLockAsync(context);
                if (!acquireLockResult.Succeeded)
                {
                    if (contentStoreForStream != null)
                    {
                        ContentStoreForStream.Dispose();
                    }

                    return acquireLockResult;
                }
            }

            return BoolResult.Success;
        }
    }
}
