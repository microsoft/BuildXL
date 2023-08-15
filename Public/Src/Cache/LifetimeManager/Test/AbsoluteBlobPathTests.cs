// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.BlobLifetimeManager.Test
{
    public class AbsoluteBlobPathTests
    {
        [Fact]
        public void CanParseFromSubject()
        {
            var container = "contentv0-matrix-universe-namespace";
            var path = @"some\path\to\the\file.blob";
            var subject = $@"/blobServices/default/containers/{container}/blobs/{path}";

            var accountName = BlobCacheStorageAccountName.Parse("theaccount");

            var absolutePath = AbsoluteBlobPath.ParseFromChangeEventSubject(accountName, subject);

            absolutePath.Account.Should().Be(accountName);
            absolutePath.Path.Path.Should().Be(path);
            absolutePath.Container.ContainerName.Should().Be(container);
        }
    }
}
