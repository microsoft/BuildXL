// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.InterfacesTest;
using Test.BuildXL.TestUtilities;
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

        protected AbsolutePath OverrideTestRootDirectoryPath { get; set; }
        protected virtual AbsolutePath TestRootDirectoryPath => OverrideTestRootDirectoryPath ?? _testRootDirectory.Value.Path;

        protected ILogger Logger;

        protected TestBase(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger, ITestOutputHelper output = null)
            : this(logger, new Lazy<IAbsFileSystem>(createFileSystemFunc), output)
        {
        }

        protected TestBase(ILogger logger, Lazy<IAbsFileSystem> fileSystem, ITestOutputHelper output = null)
            : this (logger, output)
        {
            Contract.Requires(fileSystem != null);

            _fileSystem = fileSystem;
            _testRootDirectory = new Lazy<DisposableDirectory>(() => new DisposableDirectory(FileSystem, Guid.NewGuid().ToString("N").Substring(0, 12)));
        }

        protected TestBase(ILogger logger, ITestOutputHelper output = null)
            : base(output)
        {
            Contract.Requires(logger != null);
            Logger = logger;

            _fileSystem = _fileSystem ?? new Lazy<IAbsFileSystem>(() => new PassThroughFileSystem());
            _testRootDirectory = new Lazy<DisposableDirectory>(() => new DisposableDirectory(FileSystem, Guid.NewGuid().ToString("N").Substring(0, 12)));

            TaskScheduler.UnobservedTaskException += OnTaskSchedulerOnUnobservedTaskException;
            FailFastContractChecker.RegisterForFailFastContractViolations();
        }

        private void OnTaskSchedulerOnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs args)
        {
            Logger.Error("Task unobserved exception: " + args.Exception);
        }

        protected virtual IAbsFileSystem CreateFileSystem()
        {
            return null;
        }

        public override void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            TaskScheduler.UnobservedTaskException -= OnTaskSchedulerOnUnobservedTaskException;

            FailFastContractChecker.UnregisterForFailFastContractViolations();

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
