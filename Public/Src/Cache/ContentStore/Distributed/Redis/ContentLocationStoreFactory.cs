// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Services;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;

#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Creates <see cref="IContentLocationStore"/> instance backed by Local Location Store.
    /// </summary>
    public class ContentLocationStoreFactory
    {
        // https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Basics.md
        // Maintain the same connection multiplexer to reuse across sessions

        /// <nodoc />
        private Tracer Tracer { get; } = new Tracer(nameof(ContentLocationStoreFactory));

        /// <nodoc />
        protected IClock Clock => Arguments.Clock;

        /// <nodoc />
        protected DistributedContentCopier Copier => Arguments.Copier;

        /// <nodoc />
        protected string KeySpace => Configuration.Keyspace;

        /// <nodoc />
        protected readonly RedisContentLocationStoreConfiguration Configuration;

        protected internal ContentLocationStoreFactoryArguments Arguments { get; }

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
        }

        public IContentLocationStore Create(MachineLocation localMachineLocation, ILocalContentStore? localContentStore)
        {
            return new TransitioningContentLocationStore(
                Configuration,
                Services.LocalLocationStore.Instance,
                localMachineLocation,
                localContentStore);
        }

        public void TraceConfiguration(Context context)
        {
            Tracer.TraceStartupConfiguration(context, Configuration);
        }
    }
}
