// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.InterfacesTest;
using Xunit.Abstractions;

namespace ContentStoreTest.Test
{
    public class TestBase : TestWithOutput
    {
        internal const string ScenarioSuffix =
#if DEBUG
            "DEBUG";
#else
            "RELEASE";
#endif

        private bool _disposed;
        private readonly Lazy<DisposableDirectory> _testRootDirectory;

        protected Lazy<IAbsFileSystem> _fileSystem;

        // The file system may be null.
        protected IAbsFileSystem FileSystem => _fileSystem.Value;

        protected virtual AbsolutePath TestRootDirectoryPath => _testRootDirectory.Value.Path;

        protected ILogger Logger;

        protected TestBase(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger, ITestOutputHelper output = null)
            : this(logger, new Lazy<IAbsFileSystem>(createFileSystemFunc), output)
        {
            TaskScheduler.UnobservedTaskException += OnTaskSchedulerOnUnobservedTaskException;
        }

        private void OnTaskSchedulerOnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs args)
        {
            Logger.Error("Task unobserved exception: " + args.Exception);
        }

        protected TestBase(ILogger logger, Lazy<IAbsFileSystem> fileSystem, ITestOutputHelper output = null)
            : base (output)
        {
            Contract.Requires(logger != null);
            Contract.Requires(fileSystem != null);

            Logger = logger;
            _fileSystem = fileSystem;
            _testRootDirectory = new Lazy<DisposableDirectory>(() => new DisposableDirectory(FileSystem, Guid.NewGuid().ToString("N").Substring(0, 12)));
        }

        protected virtual IAbsFileSystem CreateFileSystem()
        {
            return null;
        }

        protected TestBase(ILogger logger, ITestOutputHelper output = null)
            : base(output)
        {
            Contract.Requires(logger != null);
            Logger = logger;
        }

        public sealed override void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            TaskScheduler.UnobservedTaskException -= OnTaskSchedulerOnUnobservedTaskException;

            base.Dispose();

            Dispose(true);

            _disposed = true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_testRootDirectory?.IsValueCreated == true)
                {
                    _testRootDirectory.Value?.Dispose();
                }

                if (_fileSystem?.IsValueCreated == true)
                {
                    FileSystem?.Dispose();
                }

                Logger.Flush();
            }
        }

        protected static string GetRandomFileName()
        {
            // Don't use Path.GetRandomFileName(), it's not random enough when running multi-threaded.
            return Guid.NewGuid().ToString("N").Substring(0, 12);
        }
    }
}
