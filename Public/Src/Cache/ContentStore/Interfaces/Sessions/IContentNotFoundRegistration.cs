// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions
{
    /// <nodoc/>
    public interface IContentNotFoundRegistration : IContentSession
    {
        /// <summary>
        /// Registers a listener that will be called when content is not found when calling
        /// <see cref="IContentSession.PlaceFileAsync(Context, ContentHash, FileSystem.AbsolutePath, FileAccessMode, FileReplacementMode, FileRealizationMode, System.Threading.CancellationToken, UrgencyHint)"/> or
        /// <see cref="IContentSession.PlaceFileAsync(Context, System.Collections.Generic.IReadOnlyList{ContentHashWithPath}, FileAccessMode, FileReplacementMode, FileRealizationMode, System.Threading.CancellationToken, UrgencyHint)"/>.
        /// </summary>
        void AddContentNotFoundOnPlaceListener(Func<Context, ContentHash, Task> listener);
    }
}
