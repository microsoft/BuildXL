// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Configuration.Resolvers.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.BuildXL.Utilities
{
    public class UntrackingResolverSettingsTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CloneRemapsUntrackingSettings(bool useNinja)
        {
            var oldPathTable = new PathTable();
            var newPathTable = new PathTable();
            AbsolutePath.Create(newPathTable, A("z", "different", "path", "table", "entries"));

            var directory = new DirectoryArtifact(AbsolutePath.Create(oldPathTable, A("x", "ignored")), 17, true);
            var relativePath = RelativePath.Create(oldPathTable.StringTable, R("relative", "ignored"));
            var file = FileArtifact.CreateSourceFile(AbsolutePath.Create(oldPathTable, A("x", "ignored.txt")));
            var processName = PathAtom.Create(oldPathTable.StringTable, "worker.exe");
            var breakaway = new BreakawayChildProcess
            {
                ProcessName = PathAtom.Create(oldPathTable.StringTable, "child.exe"),
                RequiredArguments = "--detach",
                RequiredArgumentsIgnoreCase = true,
            };
            UntrackingResolverSettings settings = useNinja ? new NinjaResolverSettings() : new RushResolverSettings();
            settings.UntrackedDirectoryScopes = new[]
            {
                new DiscriminatingUnion<DirectoryArtifact, RelativePath>(directory),
                new DiscriminatingUnion<DirectoryArtifact, RelativePath>(relativePath),
            };
            settings.UntrackedDirectories = new[]
            {
                new DiscriminatingUnion<DirectoryArtifact, RelativePath>(relativePath),
                new DiscriminatingUnion<DirectoryArtifact, RelativePath>(directory),
            };
            settings.UntrackedFiles = new[] { file };
            settings.UntrackedGlobalDirectoryScopes = new[] { relativePath };
            settings.ChildProcessesToBreakawayFromSandbox = new[]
            {
                new DiscriminatingUnion<PathAtom, IBreakawayChildProcess>(processName),
                new DiscriminatingUnion<PathAtom, IBreakawayChildProcess>(breakaway),
            };
            settings.AllowedSurvivingChildProcesses = new[] { processName };

            var clone = (UntrackingResolverSettings)ResolverSettings.CreateFromTemplate(
                settings, new PathRemapper(oldPathTable, newPathTable));

            var expectedDirectory = new DirectoryArtifact(AbsolutePath.Create(newPathTable, A("x", "ignored")), 17, true);
            var expectedRelativePath = RelativePath.Create(newPathTable.StringTable, R("relative", "ignored"));
            var expectedFile = FileArtifact.CreateSourceFile(AbsolutePath.Create(newPathTable, A("x", "ignored.txt")));
            var expectedProcessName = PathAtom.Create(newPathTable.StringTable, "worker.exe");
            XAssert.AreNotEqual(directory.Path, expectedDirectory.Path);
            XAssert.AreNotEqual(processName, expectedProcessName);
            XAssert.AreEqual(expectedDirectory, (DirectoryArtifact)clone.UntrackedDirectoryScopes[0].GetValue());
            XAssert.AreEqual(expectedRelativePath, (RelativePath)clone.UntrackedDirectoryScopes[1].GetValue());
            XAssert.AreEqual(expectedRelativePath, (RelativePath)clone.UntrackedDirectories[0].GetValue());
            XAssert.AreEqual(expectedDirectory, (DirectoryArtifact)clone.UntrackedDirectories[1].GetValue());
            XAssert.AreEqual(expectedFile, clone.UntrackedFiles[0]);
            XAssert.AreEqual(expectedRelativePath, clone.UntrackedGlobalDirectoryScopes[0]);
            XAssert.AreEqual(expectedProcessName, (PathAtom)clone.ChildProcessesToBreakawayFromSandbox[0].GetValue());
            var clonedBreakaway = (IBreakawayChildProcess)clone.ChildProcessesToBreakawayFromSandbox[1].GetValue();
            XAssert.AreEqual(PathAtom.Create(newPathTable.StringTable, "child.exe"), clonedBreakaway.ProcessName);
            XAssert.AreEqual(breakaway.RequiredArguments, clonedBreakaway.RequiredArguments);
            XAssert.AreEqual(breakaway.RequiredArgumentsIgnoreCase, clonedBreakaway.RequiredArgumentsIgnoreCase);
            XAssert.AreEqual(expectedProcessName, clone.AllowedSurvivingChildProcesses[0]);

            XAssert.AreEqual(directory, (DirectoryArtifact)settings.UntrackedDirectoryScopes[0].GetValue());
            XAssert.AreEqual(processName, (PathAtom)settings.ChildProcessesToBreakawayFromSandbox[0].GetValue());
            XAssert.AreEqual("child.exe", breakaway.ProcessName.ToString(oldPathTable.StringTable));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ClonePreservesUnsetUntrackingSettings(bool useNinja)
        {
            UntrackingResolverSettings settings = useNinja ? new NinjaResolverSettings() : new RushResolverSettings();
            var clone = (UntrackingResolverSettings)ResolverSettings.CreateFromTemplate(
                settings, new PathRemapper(new PathTable(), new PathTable()));

            XAssert.IsNull(clone.UntrackedDirectoryScopes);
            XAssert.IsNull(clone.UntrackedDirectories);
            XAssert.IsNull(clone.UntrackedFiles);
            XAssert.IsNull(clone.UntrackedGlobalDirectoryScopes);
            XAssert.IsNull(clone.ChildProcessesToBreakawayFromSandbox);
            XAssert.IsNull(clone.AllowedSurvivingChildProcesses);
        }
    }
}
