// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Native.IO;
using BuildXL.ProcessPipExecutor;
using BuildXL.Utilities.Core;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    public class ReparsePointResolverTests : PipTestBase
    {
        private ReparsePointResolver ReparsePointResolver { get; }

        public ReparsePointResolverTests(ITestOutputHelper output) : base(output) 
        {
            var directoryTranslator = new DirectoryTranslator();

            if (TryGetSubstSourceAndTarget(out string substSource, out string substTarget))
            {
                directoryTranslator.AddTranslation(substSource, substTarget);
            }

            directoryTranslator.Seal();

            ReparsePointResolver = new ReparsePointResolver(Context, directoryTranslator);
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void TestAllReparsePointsAreResolved()
        {
            // Create 'folder/nestedFolder/nestedFile', a directory symlink that points to 'folder' and a nested
            // directory symlink that points to 'folder/nestedFolder'
            var folder = Path.Combine(TemporaryDirectory, "folder");
            var nestedFolder = Path.Combine(folder, "nestedFolder");
            FileUtilities.CreateDirectory(nestedFolder);

            var nestedFile = Path.Combine(nestedFolder, "nestedFile");

            File.WriteAllText(nestedFile, "content");
            var symlinkFolder = Path.Combine(TemporaryDirectory, "symlinkFolder");
            var symlinkNestedFolder = Path.Combine(TemporaryDirectory, "symlinkFolder", "symlinkNestedFolder");

            var linkResult = FileUtilities.TryCreateSymbolicLink(symlinkFolder, folder, isTargetFile: false);
            XAssert.IsTrue(linkResult.Succeeded);

            var nestedLinkResult = FileUtilities.TryCreateSymbolicLink(symlinkNestedFolder, nestedFolder, isTargetFile: false);
            XAssert.IsTrue(nestedLinkResult.Succeeded);

            var result = ReparsePointResolver.ResolveIntermediateDirectoryReparsePoints(
                AbsolutePath.Create(Context.PathTable, symlinkNestedFolder).Combine(Context.PathTable, "nestedFile"));

            // Both directory symlinks should be resolved
            XAssert.AreEqual(AbsolutePath.Create(Context.PathTable, nestedFile), result);
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void TestLastAtomIsNeverResolved()
        {
            // Create 'folder/nestedFile', a file symlink that points to 'nestedFile'
            var folder = Path.Combine(TemporaryDirectory, "folder");
            FileUtilities.CreateDirectory(folder);

            var nestedFile = Path.Combine(folder, "nestedFile");
            File.WriteAllText(nestedFile, "content");

            var symlinkFile = Path.Combine(folder, "symlinkFile");

            var linkResult = FileUtilities.TryCreateSymbolicLink(symlinkFile, nestedFile, isTargetFile: true);
            XAssert.IsTrue(linkResult.Succeeded);

            var result = ReparsePointResolver.ResolveIntermediateDirectoryReparsePoints(AbsolutePath.Create(Context.PathTable, symlinkFile));

            // Both directory symlinks should be resolved
            XAssert.AreEqual(AbsolutePath.Create(Context.PathTable, symlinkFile), result);
        }
    }
}
