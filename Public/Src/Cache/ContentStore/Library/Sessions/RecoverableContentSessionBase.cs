// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.ContentStore.Sessions
{
    /// <summary>
    /// Extends <see cref="ContentSessionBase"/> with <see cref="IContentNotFoundRegistration"/> support,
    /// allowing listeners to be notified when a PlaceFile operation returns <c>NotPlacedContentNotFound</c>.
    /// Only content sessions that participate in the content-recovery flow should inherit from this class.
    /// </summary>
    public abstract class RecoverableContentSessionBase : ContentSessionBase, IContentNotFoundRegistration
    {
        private readonly List<Func<Context, ContentHash, Task>> _contentNotFoundListeners = new();

        /// <inheritdoc />
        protected override bool HasContentNotFoundListeners => _contentNotFoundListeners.Count > 0;

        /// <nodoc />
        protected RecoverableContentSessionBase(string name, CounterTracker? counterTracker = null)
            : base(name, counterTracker)
        {
        }

        /// <inheritdoc />
        public virtual void AddContentNotFoundOnPlaceListener(Func<Context, ContentHash, Task> listener)
        {
            _contentNotFoundListeners.Add(listener);
        }

        /// <inheritdoc />
        protected override async Task OnPlaceFileContentNotFoundAsync(OperationContext operationContext, ContentHash contentHash)
        {
            foreach (var listener in _contentNotFoundListeners)
            {
                await listener(operationContext, contentHash);
            }
        }
    }
}
