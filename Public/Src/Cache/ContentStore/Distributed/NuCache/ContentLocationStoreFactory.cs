// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed.Services;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;

#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Creates <see cref="IContentLocationStore"/> instance backed by Local Location Store.
    /// </summary>
    public class ContentLocationStoreFactory
    {
        /// <nodoc />
        private Tracer Tracer { get; } = new Tracer(nameof(ContentLocationStoreFactory));

        /// <nodoc />
        protected IClock Clock => Arguments.Clock;

        /// <nodoc />
        protected DistributedContentCopier Copier => Arguments.Copier;

        /// <nodoc />
        protected readonly LocalLocationStoreConfiguration Configuration;

        /// <nodoc />
        protected internal ContentLocationStoreFactoryArguments Arguments { get; }

        /// <nodoc />
        public ContentLocationStoreServices Services { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentLocationStoreFactory"/> class.
        /// </summary>
        public ContentLocationStoreFactory(
            ContentLocationStoreFactoryArguments arguments,
            LocalLocationStoreConfiguration configuration)
        {
            Contract.Requires(configuration != null);
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
