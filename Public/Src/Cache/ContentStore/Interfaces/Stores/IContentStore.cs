// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

// ReSharper disable UnusedParameter.Global
namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    ///     Standard interface for content stores.
    /// </summary>
    public interface IContentStore : IStartupShutdown
    {
        /// <summary>
        ///     Create a new session that can only read.
        /// </summary>
        CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin);

        /// <summary>
        ///     Create a new session that can add content as well as read.
        /// </summary>
        CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin);

        /// <summary>
        ///     Gets a current stats snapshot.
        /// </summary>
        Task<GetStatsResult> GetStatsAsync(Context context);
    }

    /// <summary>
    /// Special <see cref="IContentStore"/> version that supports notification about initialization completion.
    /// </summary>
    public interface IContentStoreWithPostInitialization : IContentStore
    {
        /// <summary>
        /// Notifies that the post initialization step of the outer component is finished.
        /// </summary>
        void PostInitializationCompleted(Context context, BoolResult result);
    }
}
