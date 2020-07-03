// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace ContentStoreTest.Stores
{
    public interface ITestServiceClientContentStore
    {
        void SetOverrideCacheName(string value);

        void SetDoNotStartService(bool value);

        Task RestartServerAsync(Context context);

        Task ShutdownServerAsync(Context context);
    }
}
