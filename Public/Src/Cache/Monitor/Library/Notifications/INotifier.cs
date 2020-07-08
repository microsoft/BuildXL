// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Monitor.App.Notifications
{
    public interface INotifier<T>
    {
        void Emit(T notification);
    }
}
