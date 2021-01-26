// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.Monitor.App.Notifications;

namespace BuildXL.Cache.Monitor.Library.Notifications
{
    public class MockNotifier<T> : INotifier<T>
    {
        public IReadOnlyList<T> Results => _results;

        private readonly List<T> _results = new List<T>();

        public void Emit(T notification) => _results.Add(notification);
    }
}
