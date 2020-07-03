// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

// ReSharper disable UnusedParameter.Global
namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    /// Options for deleting content from machines
    /// </summary>
    public class DeleteContentOptions
    {
        /// <summary>
        /// Variable controlling local or distributed delete
        /// </summary>
        public bool DeleteLocalOnly { get; set; }
    }

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

        /// <summary>
        ///     Remove given content from all sessions.
        /// </summary>
        Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions);

        /// <summary>
        /// Notifies that the post initialization step of the outer component is finished.
        /// </summary>
        void PostInitializationCompleted(Context context, BoolResult result);
    }
}
