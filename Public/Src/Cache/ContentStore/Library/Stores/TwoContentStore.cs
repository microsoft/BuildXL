// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     An <see cref="IContentStore" /> implemented as a composition of two <see cref="IContentStore" />.
    /// </summary>
    public abstract class TwoContentStore : IContentStore, IRepairStore
    {
        /// <summary>
        ///     Gets the tracer for this <see cref="IContentStore"/>.
        /// </summary>
        protected abstract ContentStoreTracer Tracer { get; }

        /// <summary>
        ///     The first <see cref="IContentStore" />.
        /// </summary>
        protected readonly IContentStore ContentStore1;

        /// <summary>
        ///     The second <see cref="IContentStore" />.
        /// </summary>
        protected readonly IContentStore ContentStore2;

        /// <summary>
        ///     Gets the name of the first <see cref="IContentStore"/>.
        /// </summary>
        protected virtual string NameOfContentStore1 => nameof(ContentStore1);

        /// <summary>
        ///     Gets the name of the first <see cref="IContentStore"/>.
        /// </summary>
        protected virtual string NameOfContentStore2 => nameof(ContentStore2);

        private bool _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TwoContentStore" /> class.
        /// </summary>
        protected TwoContentStore(Func<IContentStore> factoryOfContentStore1, Func<IContentStore> factoryOfContentStore2)
        {
            ContentStore1 = factoryOfContentStore1();
            ContentStore2 = factoryOfContentStore2();
        }

        /// <inheritdoc />
        public virtual bool StartupCompleted => ContentStore1.StartupCompleted && ContentStore2.StartupCompleted;

        /// <inheritdoc />
        public virtual bool StartupStarted => ContentStore1.StartupStarted && ContentStore2.StartupStarted;

        /// <inheritdoc />
        public virtual async Task<BoolResult> StartupAsync(Context context)
        {
            try
            {
                var startupResults =
                    await Task.WhenAll(ContentStore1.StartupAsync(context), ContentStore2.StartupAsync(context));
                Contract.Assert(startupResults.Length == 2);

                var startupResult1 = startupResults[0];
                var startupResult2 = startupResults[1];

                var result = startupResult1 & startupResult2;
                if (!result.Succeeded)
                {
                    var sb = new StringBuilder();

                    if (!startupResult1.Succeeded)
                    {
                        sb.Concat($"{NameOfContentStore1} startup failed, error=[{startupResult1}]", "; ");
                    }

                    if (!startupResult2.Succeeded)
                    {
                        sb.Concat($"{NameOfContentStore2} startup failed, error=[{startupResult2}]", "; ");
                    }

                    if (startupResult1.Succeeded)
                    {
                        var shutdownResult = await ContentStore1.ShutdownAsync(context);
                        if (!shutdownResult.Succeeded)
                        {
                            sb.Concat($"{NameOfContentStore1} shutdown failed, error=[{shutdownResult}]", "; ");
                        }
                    }

                    if (startupResult2.Succeeded)
                    {
                        var shutdownResult = await ContentStore2.ShutdownAsync(context);
                        if (!shutdownResult.Succeeded)
                        {
                            sb.Concat($"{NameOfContentStore2} shutdown failed, error=[{shutdownResult}]", "; ");
                        }
                    }

                    result = new BoolResult(sb.ToString());
                }

                return result;
            }
            catch (Exception e)
            {
                return new BoolResult(e, $"{GetType().Name} startup failed");
            }
        }

        /// <inheritdoc />
        public virtual bool ShutdownCompleted => ContentStore1.ShutdownCompleted && ContentStore2.ShutdownCompleted;

        /// <inheritdoc />
        public bool ShutdownStarted => ContentStore1.ShutdownStarted && ContentStore2.ShutdownStarted;

        /// <inheritdoc />
        public async Task<BoolResult> ShutdownAsync(Context context)
        {
            try
            {
                var shutdownResults =
                    await
                        Task.WhenAll(ContentStore1.ShutdownAsync(context), ContentStore2.ShutdownAsync(context));
                Contract.Assert(shutdownResults.Length == 2);

                var shutdownResult1 = shutdownResults[0];
                var shutdownResult2 = shutdownResults[1];

                var result = shutdownResult1 & shutdownResult2;

                if (!result.Succeeded)
                {
                    var sb = new StringBuilder();
                    if (!shutdownResult1.Succeeded)
                    {
                        sb.Concat($"{NameOfContentStore1} shutdown failed, error=[{shutdownResult1}]", "; ");
                    }

                    if (!shutdownResult2.Succeeded)
                    {
                        sb.Concat($"{NameOfContentStore2} shutdown failed, error=[{shutdownResult2}]", "; ");
                    }

                    result = new BoolResult(sb.ToString());
                }

                return result;
            }
            catch (Exception e)
            {
                return new BoolResult(e, $"{GetType().Name} shutdown failed");
            }
        }

        /// <inheritdoc />
        public abstract CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(
            Context context,
            string name,
            ImplicitPin implicitPin);

        /// <inheritdoc />
        public abstract CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin);

        /// <inheritdoc />
        public virtual Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return GetStatsCall<ContentStoreTracer>.RunAsync(
                Tracer,
                new OperationContext(context),
                () =>
                {
                    return Task.WhenAll(ContentStore1.GetStatsAsync(context), ContentStore2.GetStatsAsync(context))
                        .ContinueWith(
                            antecedent =>
                            {
                                if (antecedent.IsFaulted)
                                {
                                    return new GetStatsResult(antecedent.Exception);
                                }

                                if (antecedent.IsCanceled)
                                {
                                    return new GetStatsResult($"{nameof(GetStatsAsync)} is cancelled");
                                }

                                Contract.Assert(antecedent.Result.Length == 2);

                                var counterSet = new CounterSet();
                                counterSet.Merge(antecedent.Result[0].CounterSet, NameOfContentStore1 + ".");
                                counterSet.Merge(antecedent.Result[1].CounterSet, NameOfContentStore2 + ".");

                                return new GetStatsResult(counterSet);
                            });
                });
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Protected implementation of Dispose pattern.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                ContentStore1.Dispose();
                ContentStore2.Dispose();
            }

            _disposed = true;
        }

        /// <inheritdoc />
        public Task<StructResult<long>> RemoveFromTrackerAsync(Context context)
        {
            return RemoveFromTrackerCall<ContentStoreTracer>.RunAsync(Tracer, new OperationContext(context), async () =>
            {
                var removeTaskByStore = new Dictionary<string, Task<StructResult<long>>>();

                var store1 = ContentStore1 as IRepairStore;
                if (store1 != null)
                {
                    removeTaskByStore.Add(NameOfContentStore1, store1.RemoveFromTrackerAsync(context));
                }
                else
                {
                    Tracer.Debug(context, $"Repair handling not enabled for {NameOfContentStore1}.");
                }

                var store2 = ContentStore2 as IRepairStore;
                if (store2 != null)
                {
                    removeTaskByStore.Add(NameOfContentStore2, store2.RemoveFromTrackerAsync(context));
                }
                else
                {
                    Tracer.Debug(context, $"Repair handling not enabled for {NameOfContentStore2}.");
                }

                await Task.WhenAll(removeTaskByStore.Values);

                var sb = new StringBuilder();
                long filesTrimmed = 0;
                foreach (var kvp in removeTaskByStore)
                {
                    var removeTrackerResult = await kvp.Value;
                    if (removeTrackerResult.Succeeded)
                    {
                        filesTrimmed += removeTrackerResult.Data;
                    }
                    else
                    {
                        sb.Concat($"{kvp.Key} repair handling failed, error=[{removeTrackerResult}]", "; ");
                    }
                }

                if (sb.Length > 0)
                {
                    return new StructResult<long>(sb.ToString());
                }
                else
                {
                    return new StructResult<long>(filesTrimmed);
                }
            });
        }

        /// <inheritdoc />
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result)
        {
            ContentStore1.PostInitializationCompleted(context, result);
            ContentStore2.PostInitializationCompleted(context, result);
        }
    }
}
