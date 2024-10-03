// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using System.Text;
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

            ReparsePointResolver = new ReparsePointResolver(Context.PathTable, directoryTranslator);
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

        /// There is nothing Linux-specific with this test, but under CloudBuild we run with additional directory
        /// translations (e.g. Out folder is usually a reparse point) that this test infra is not aware of, so paths
        /// are not properly translated
        [FactIfSupported(requiresSymlinkPermission: true, requiresLinuxBasedOperatingSystem: true)]
        public void TestGetAllReparsePointChains()
        {
            // Create the following layout
            // A
            // |- B-symlink
            // |- B-symlik-again
            //    |- D-symlink
            //
            // B
            // |- D-symlink
            //
            // D
            // |- file.txt
            //
            // B-symlink       -> B
            // B-symlink-again -> B-symlink
            // D-symlink       -> D
            //
            // And get all reparse points involved in resolving A/B-symlink-again/D-symlink/file.txt

            var aFolder = Path.Combine(TemporaryDirectory, "A");
            var bFolder = Path.Combine(TemporaryDirectory, "B");
            var dFolder = Path.Combine(TemporaryDirectory, "D");
            FileUtilities.CreateDirectory(aFolder);
            FileUtilities.CreateDirectory(bFolder);
            FileUtilities.CreateDirectory(dFolder);

            var file = Path.Combine(dFolder, "file.txt");
            File.WriteAllText(file, "content");

            var bSymlinked = Path.Combine(aFolder, "B-symlink");
            var _ = FileUtilities.TryCreateSymbolicLink(bSymlinked, bFolder, isTargetFile: false);

            var bSymlinkedAgain = Path.Combine(aFolder, "B-symlink-again");
            _ = FileUtilities.TryCreateSymbolicLink(bSymlinkedAgain, bSymlinked, isTargetFile: false);

            var dSymlinked = Path.Combine(bSymlinkedAgain, "D-symlink");
            _ = FileUtilities.TryCreateSymbolicLink(dSymlinked, dFolder, isTargetFile: false);

            var result = ReparsePointResolver.GetAllReparsePointsInChains(AbsolutePath.Create(Context.PathTable, Path.Combine(dSymlinked, "file.txt")));

            // We should get a path for each created symlink (and for each symlink all its preceding atoms should be fully resolved)
            // We do 'Contains' instead of asserting the complete set because in CB there might be other reparse points involved (e.g. Out directory)
            AbsolutePath[] shouldContain = [
                AbsolutePath.Create(Context.PathTable, Path.Combine(TemporaryDirectory, "A", "B-symlink")),
                AbsolutePath.Create(Context.PathTable, Path.Combine(TemporaryDirectory, "A", "B-symlink-again")),
                AbsolutePath.Create(Context.PathTable, Path.Combine(TemporaryDirectory, "B", "D-symlink"))
            ];

            if (shouldContain.Any(p => !result.Contains(p))) 
            {
                var sb = new StringBuilder();
                sb.AppendLine("[");
                foreach (var p in result)
                {
                    sb.AppendLine("    " + p.ToString(Context.PathTable));
                }
                sb.AppendLine("]");
                var failedPath = shouldContain.First(p => !result.Contains(p)).ToString(Context.PathTable);
                XAssert.Fail($"A path {failedPath} is not contained in the collection {sb}");
            }
        }
    }
}
