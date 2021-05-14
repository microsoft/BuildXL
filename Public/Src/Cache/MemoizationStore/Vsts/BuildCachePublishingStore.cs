// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Vsts.Internal;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Content.Common.Authentication;
using Microsoft.VisualStudio.Services.WebApi;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    /// Publishes metadata to the BuildCache service.
    /// </summary>
    public class BuildCachePublishingStore : StartupShutdownSlimBase, IPublishingStore
    {
        /// <nodoc />
        protected readonly IAbsFileSystem FileSystem;

        /// <nodoc />
        protected readonly SemaphoreSlim PublishingGate;

        /// <summary>
        /// The publishing store needs somewhere to get content from in case it needs to publish a
        /// content hash list's contents. This should point towards some locally available cache.
        /// </summary>
        protected readonly IContentStore ContentSource;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(BuildCachePublishingStore));

        /// <nodoc />
        public BuildCachePublishingStore(IContentStore contentSource, IAbsFileSystem fileSystem, int concurrencyLimit)
        {
            ContentSource = contentSource;
            FileSystem = fileSystem;

            PublishingGate = new SemaphoreSlim(concurrencyLimit);
        }

        /// <inheritdoc />
        public virtual Result<IPublishingSession> CreateSession(Context context, string name, PublishingCacheConfiguration config, string pat)
        {
            if (config is not BuildCacheServiceConfiguration buildCacheConfig)
            {
                return new Result<IPublishingSession>($"Configuration is not a {nameof(BuildCacheServiceConfiguration)}. Actual type: {config.GetType().FullName}");
            }

            var contentSessionResult = ContentSource.CreateSession(context, $"{name}-contentSource", ImplicitPin.None);
            if (!contentSessionResult.Succeeded)
            {
                return new Result<IPublishingSession>(contentSessionResult);
            }

            return new Result<IPublishingSession>(new BuildCachePublishingSession(buildCacheConfig, name, pat, contentSessionResult.Session, FileSystem, PublishingGate));
        }
    }
}
