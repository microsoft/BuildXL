// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Stores
{
    public class RocksDbFileSystemContentStore: IContentStore
    {
        public RocksDbFileSystemContentStore()
        {

        }

        public bool StartupCompleted => throw new NotImplementedException();

        public bool StartupStarted => throw new NotImplementedException();

        public bool ShutdownCompleted => throw new NotImplementedException();

        public bool ShutdownStarted => throw new NotImplementedException();

        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            throw new NotImplementedException();
        }

        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin) // does something
        {
            throw new NotImplementedException();
        }

        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            throw new NotImplementedException();
        }

        public void PostInitializationCompleted(Context context, BoolResult result)
        {
            throw new NotImplementedException();
        }

        public Task<BoolResult> ShutdownAsync(Context context) // does something
        {
            throw new NotImplementedException();
        }
        

        public Task<BoolResult> StartupAsync(Context context) // does something
        {
            throw new NotImplementedException();
        }
    }
}
