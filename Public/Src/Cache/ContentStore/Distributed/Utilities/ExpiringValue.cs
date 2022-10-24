// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Utils;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// Stores <typeparamref name="TValue"/> and allows re-creating it once it expires.
    /// </summary>
    public sealed class ExpiringValue<TValue>
    {
        private readonly TimeSpan _expiry;
        private readonly IClock _clock;
        private DateTime _insertionTime;
        private TValue? _value;

        private readonly object _syncRoot = new object();

        /// <nodoc />
        public ExpiringValue(TimeSpan expiry, IClock clock, TValue? originalValue = default)
        {
            _expiry = expiry;
            _clock = clock;
            _value = originalValue;
        }

        /// <summary>
        /// Returns true if the value was initialized and is up-to-date.
        /// </summary>
        public bool IsUpToDate() => _insertionTime.IsRecent(_clock.UtcNow, _expiry);

        /// <summary>
        /// Updates the stored value with the <paramref name="value"/>.
        /// </summary>
        public void Update(TValue value)
        {
            lock (_syncRoot)
            {
                _value = value;
                // The insertion time should be updated last.
                _insertionTime = _clock.UtcNow;
            }
        }

        /// <summary>
        /// Tries obtaining an up-to-date value.
        /// </summary>
        public bool TryGetValue([NotNullWhen(true)] out TValue? result)
        {
            var now = _clock.UtcNow;
            if (_insertionTime.IsRecent(now, _expiry))
            {
                lock (_syncRoot)
                {
                    result = _value!;
                }

                return true;
            }

            result = default!;
            return false;
        }

        /// <summary>
        /// Gets an underlying value even if it got stale.
        /// </summary>
        public TValue? GetValueOrDefault()
        {
            lock (_syncRoot)
            {
                return _value;
            }
        }
    }
}
