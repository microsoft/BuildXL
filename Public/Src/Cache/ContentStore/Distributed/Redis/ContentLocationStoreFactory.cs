// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Services;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;

#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Creates <see cref="IContentLocationStore"/> instance backed by Local Location Store.
    /// </summary>
    public class ContentLocationStoreFactory : StartupShutdownComponentBase, IContentLocationStoreFactory
    {
        /// <inheritdoc />
        public override bool AllowMultipleStartupAndShutdowns => true;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new ContentSessionTracer(nameof(ContentLocationStoreFactory));

        // https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Basics.md
        // Maintain the same connection multiplexer to reuse across sessions

        /// <nodoc />
        protected IClock Clock => Arguments.Clock;

        /// <nodoc />
        protected DistributedContentCopier Copier => Arguments.Copier;

        /// <nodoc />
        protected string KeySpace => Configuration.Keyspace;

        /// <nodoc />
        protected readonly RedisContentLocationStoreConfiguration Configuration;

        protected ContentLocationStoreFactoryArguments Arguments { get; }

        /// <nodoc />
        public ContentLocationStoreServices Services { get; }

        public ContentLocationStoreFactory(
            IClock clock,
            RedisContentLocationStoreConfiguration configuration,
            DistributedContentCopier copier)
            : this(
                new ContentLocationStoreFactoryArguments()
                {
                    Clock = clock,
                    Copier = copier
                },
                configuration)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentLocationStoreFactory"/> class.
        /// </summary>
        public ContentLocationStoreFactory(
            ContentLocationStoreFactoryArguments arguments,
            RedisContentLocationStoreConfiguration configuration)
        {
            Contract.Requires(configuration != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(configuration.Keyspace));
            Contract.Requires(arguments.Copier != null);

            Arguments = arguments;
            Configuration = configuration;
            Services = new ContentLocationStoreServices(arguments, configuration);

            LinkLifetime(Services.BlobContentLocationRegistry.InstanceOrDefault());
        }

        /// <inheritdoc />
        public Task<IContentLocationStore> CreateAsync(MachineLocation localMachineLocation, ILocalContentStore? localContentStore)
        {
            if (localContentStore != null && Services.BlobContentLocationRegistry.TryGetInstance(out var registry))
            {
                registry.SetLocalContentStore(localContentStore);
            }

            IContentLocationStore contentLocationStore = new TransitioningContentLocationStore(
                Configuration,
                Services.LocalLocationStore.Instance,
                localMachineLocation,
                localContentStore);

            return Task.FromResult(contentLocationStore);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupComponentAsync(OperationContext context)
        {
            Tracer.TraceStartupConfiguration(context, Configuration);
            return BoolResult.SuccessTask;
        }
    }
}
