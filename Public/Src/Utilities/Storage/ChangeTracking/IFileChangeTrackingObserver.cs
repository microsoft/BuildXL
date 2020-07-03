// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Interface for file change processing observer.
    /// </summary>
    /// <remarks>
    /// This interface creates ambiguities because it derives from two <see cref="IObserver{T}"/>'s.
    /// A class implementing this interface must implement explicitly those two interfaces.
    /// </remarks>
    // ReSharper disable once PossibleInterfaceMemberAmbiguity
    public interface IFileChangeTrackingObserver : IObserver<ChangedPathInfo>, IObserver<ChangedFileIdInfo>
    {
        /// <summary>
        /// Initializes observer.
        /// </summary>
        void OnInit();

        /// <summary>
        /// Notifies result upon completion of journal scanning.
        /// </summary>
        void OnCompleted(ScanningJournalResult result);
    }
}
