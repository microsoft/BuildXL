// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using ContentStoreTest.Sessions;

namespace ContentStoreTest.Stores
{
    public class TestFileSystemContentStore : FileSystemContentStore
    {
        public TestFileSystemContentStore
            (
            IAbsFileSystem fileSystem,
            IClock clock,
            AbsolutePath rootPath,
            ConfigurationModel configurationModel
            )
            : base(fileSystem, clock, rootPath, configurationModel)
        {
        }

        public ContentStoreConfiguration Configuration => Store.Configuration;

        public override CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            var session = new TestFileSystemContentSession(name, implicitPin, Store);
            return new CreateSessionResult<IReadOnlyContentSession>(session);
        }

        public override CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            var session = new TestFileSystemContentSession(name, implicitPin, Store);
            return new CreateSessionResult<IContentSession>(session);
        }
    }
}
