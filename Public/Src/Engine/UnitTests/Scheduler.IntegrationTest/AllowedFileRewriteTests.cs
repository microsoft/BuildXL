// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using LogEventId = BuildXL.Scheduler.Tracing.LogEventId;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "SharedOpaqueDirectoryTests")]
    [Feature(Features.SharedOpaqueDirectory)]
    public class AllowedFileRewriteTests : SchedulerIntegrationTestBase
    {
        public AllowedFileRewriteTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void AllowedRewriteCachingBehavior()
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            string writtenContent = "content";
            FileArtifact source = CreateSourceFile(sharedOpaqueDirPath);
            File.WriteAllText(source.Path.ToString(Context.PathTable), writtenContent);

            // A rewrite with no readers is always allowed, so the content actually doesn't matter here.    
            var writerBuilder = CreateWriter(writtenContent, source);
            var writer = SchedulePipBuilder(writerBuilder);

            // Run should succeed
            RunScheduler().AssertCacheMiss(writer.Process.PipId);
            // Double check the same content rewrite was detected and allowed
            AssertVerboseEventLogged(LogEventId.AllowedRewriteOnUndeclaredFile);

            // Run again. We should get a cache hit
            RunScheduler().AssertCacheHit(writer.Process.PipId);
            // Double check the same content rewrite was detected and allowed
            AssertVerboseEventLogged(LogEventId.AllowedRewriteOnUndeclaredFile);
        }

        [Fact]
        public void SameContentReadersAreAllowedWithAnyOrdering()
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            string writtenContent = "content";

            FileArtifact source = CreateSourceFile(sharedOpaqueDirPath);
            File.WriteAllText(source.Path.ToString(Context.PathTable), writtenContent);

            // A reader before the writer
            var beforeReader = SchedulePipBuilder(CreateReader(source));

            // The writer
            var writerBuilder = CreateWriter(writtenContent, source);
            writerBuilder.AddInputFile(beforeReader.ProcessOutputs.GetOutputFiles().Single());
            var writer = SchedulePipBuilder(writerBuilder);

            // A reader after the writer
            var afterReaderBuilder = CreateReader(source);
            afterReaderBuilder.AddInputDirectory(writer.ProcessOutputs.GetOutputDirectories().Single().Root);

            // An unordered reader
            SchedulePipBuilder(CreateReader(source));

            // Run should succeed
            RunScheduler().AssertSuccess();
            // Double check the same content rewrite was detected and allowed
            AssertVerboseEventLogged(LogEventId.AllowedRewriteOnUndeclaredFile);
        }

        [Fact]
        public void DifferentContentIsAllowedWhenSafe()
        {
            // Ordered readers on a different-content rewrite should be allowed
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            FileArtifact source = CreateSourceFile(sharedOpaqueDirPath);
            File.WriteAllText(source.Path.ToString(Context.PathTable), "content");

            // A reader before the writer
            var beforeReader = SchedulePipBuilder(CreateReader(source));

            // The writer writes different content
            var writerBuilder = CreateWriter("different content", source);
            writerBuilder.AddInputFile(beforeReader.ProcessOutputs.GetOutputFiles().Single());
            var writer = SchedulePipBuilder(writerBuilder);

            // A reader after the writer
            var afterReaderBuilder = CreateReader(source);
            afterReaderBuilder.AddInputDirectory(writer.ProcessOutputs.GetOutputDirectories().Single().Root);
            SchedulePipBuilder(afterReaderBuilder);

            // Run should succeed. All readers are guaranteed to see the same content across the build.
            RunScheduler().AssertSuccess();
            // Double check the same content rewrite was detected and allowed
            AssertVerboseEventLogged(LogEventId.AllowedRewriteOnUndeclaredFile);
        }

        [Fact]
        public void NonExistentAllowedSourceReadIsASafeRewrite()
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            FileArtifact source = FileArtifact.CreateSourceFile(sharedOpaqueDirPath.Combine(Context.PathTable, "source.txt")); 

            // A reader before the writer. The reader tries to read a non-existent file, which gets classified as an undeclared source read
            var reader = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(source, doNotInfer:true), // non-existent file
                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
            });

            reader.Options |= Process.Options.AllowUndeclaredSourceReads;
            reader.RewritePolicy = RewritePolicy.SafeSourceRewritesAreAllowed;

            var beforeReader = SchedulePipBuilder(reader);

            // The writer writes and delete the file
            var sourceAsOutput = source.CreateNextWrittenVersion();
            var writerBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(sourceAsOutput, doNotInfer: true),
                Operation.DeleteFile(sourceAsOutput, doNotInfer: true),
            });

            writerBuilder.Options |= Process.Options.AllowUndeclaredSourceReads;
            writerBuilder.RewritePolicy = RewritePolicy.SafeSourceRewritesAreAllowed;
            writerBuilder.AddOutputDirectory(sharedOpaqueDirPath, kind: SealDirectoryKind.SharedOpaque);

            writerBuilder.AddInputFile(beforeReader.ProcessOutputs.GetOutputFiles().Single());
            var writer = SchedulePipBuilder(writerBuilder);

            // An unordered reader
            var unorderedReader = SchedulePipBuilder(CreateReader(source));

            // Run should succeed. All readers are guaranteed to see the same content across the build.
            // Make sure the unordered reader runs after the writer just to avoid write locks.
            RunScheduler(constraintExecutionOrder: new[] { ((Pip)writer.Process, (Pip)unorderedReader.Process) }).AssertSuccess();
            // Double check the same content rewrite was detected and allowed
            AssertVerboseEventLogged(LogEventId.AllowedRewriteOnUndeclaredFile);
        }

        [Fact]
        public void RacyReadersOnDifferentContentAreBlocked()
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            FileArtifact source = CreateSourceFile(sharedOpaqueDirPath);
            File.WriteAllText(source.Path.ToString(Context.PathTable), "content");

            // The writer writes different content
            var writerBuilder = CreateWriter("different content", source);
            var writer = SchedulePipBuilder(writerBuilder);

            // An unordered reader
            var reader = SchedulePipBuilder(CreateReader(source));

            // Run should fail because read content is not guaranteed to be consistent
            // We pin the order at execution time since otherwise the read may fail if the writer is locking the file
            RunScheduler(constraintExecutionOrder: new List<(Pip, Pip)> { (reader.Process, writer.Process) }).AssertFailure();
            AssertVerboseEventLogged(LogEventId.DisallowedRewriteOnUndeclaredFile);
            AssertErrorEventLogged(LogEventId.DependencyViolationWriteInUndeclaredSourceRead);
        }


        private ProcessBuilder CreateWriter(string rewrittenContent, FileArtifact source)
        {
            var writer = CreatePipBuilder(new Operation[]
            {
                // WriteFile appends content, so delete it first to guarantee we are writing the specified content
                Operation.DeleteFile(source, doNotInfer: true),
                Operation.WriteFile(source, content: rewrittenContent, doNotInfer: true)
            });

            writer.Options |= Process.Options.AllowUndeclaredSourceReads;
            writer.RewritePolicy = RewritePolicy.SafeSourceRewritesAreAllowed;

            writer.AddOutputDirectory(source.Path.GetParent(Context.PathTable), SealDirectoryKind.SharedOpaque);

            return writer;
        }

        private ProcessBuilder CreateReader(FileArtifact source)
        {
            var reader = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(source, doNotInfer:true),
                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
            });

            reader.Options |= Process.Options.AllowUndeclaredSourceReads;
            reader.RewritePolicy = RewritePolicy.SafeSourceRewritesAreAllowed;

            return reader;
        }
    }
}

