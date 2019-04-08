// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
