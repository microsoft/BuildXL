// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// A disposable struct that calls a given callback when <see cref="Dispose"/> method is called.
    /// </summary>
    public readonly record struct DisposeAction<TState> : IDisposable
    {
        private readonly TState m_state;
        private readonly Action<TState> m_disposeAction;

        /// <nodoc />
        public DisposeAction(TState state, Action<TState> disposeAction) 
            => (m_state, m_disposeAction) = (state, disposeAction);

        /// <inheritdoc />
        public void Dispose() => m_disposeAction(m_state);
    }
}