// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Interface for file change tracking observable.
    /// </summary>
    /// <remarks>
    /// The pattern used here follows https://docs.microsoft.com/en-us/dotnet/standard/events/observer-design-pattern
    /// </remarks>
    public interface IFileChangeTrackingObservable : IObservable<ChangedPathInfo>, IObservable<ChangedFileIdInfo>
    {
        /// <summary>
        /// Subscribes an observer.
        /// </summary>
        IDisposable Subscribe(IFileChangeTrackingObserver observer);

        /// <summary>
        /// Token identifying this instance of <see cref="IFileChangeTrackingObservable"/>.
        /// </summary>
        FileEnvelopeId FileEnvelopeId { get; }
    }
}
