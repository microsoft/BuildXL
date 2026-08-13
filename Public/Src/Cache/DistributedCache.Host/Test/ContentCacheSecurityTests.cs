// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Launcher.Server;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.Host.Test
{
    public class ContentCacheSecurityTests
    {
        [Fact]
        public void DownloadUrlPolicyRejectsAllOriginsWhenNoneAreConfigured()
        {
            var policy = new DownloadUrlPolicy(allowedOrigins: null);

            policy.IsAllowed("https://account.blob.core.windows.net/container/content?sig=secret").Should().BeFalse();
            policy.IsAllowed("https://downloads.example.com/content").Should().BeFalse();
        }

        [Fact]
        public void DownloadUrlPolicyAllowsOnlyConfiguredHttpsOrigins()
        {
            var policy = new DownloadUrlPolicy(new[] { "https://downloads.example.com", "https://storage.example.com:8443" });

            policy.IsAllowed("https://downloads.example.com/content/file?sig=secret").Should().BeTrue();
            policy.IsAllowed("https://storage.example.com:8443/file").Should().BeTrue();
            policy.IsAllowed("http://downloads.example.com/file").Should().BeFalse();
            policy.IsAllowed("https://downloads.example.com:8443/file").Should().BeFalse();
            policy.IsAllowed("https://other.example.com/file").Should().BeFalse();
            policy.IsAllowed("https://user@downloads.example.com/file").Should().BeFalse();
            policy.IsAllowed("https://downloads.example.com/file#fragment").Should().BeFalse();
        }

        [Theory]
        [InlineData("https://127.0.0.1/content")]
        [InlineData("https://[::1]/content")]
        [InlineData("https://169.254.169.254/metadata")]
        [InlineData("https://10.0.0.1/content")]
        public void DownloadUrlPolicyRejectsUnconfiguredInternalOrigins(string downloadUrl)
        {
            var policy = new DownloadUrlPolicy(new[] { "https://downloads.example.com" });

            policy.IsAllowed(downloadUrl).Should().BeFalse();
        }

        [Theory]
        [InlineData("http://downloads.example.com")]
        [InlineData("https://downloads.example.com/path")]
        [InlineData("https://downloads.example.com?query=value")]
        [InlineData("https://user@downloads.example.com")]
        public void DownloadUrlPolicyRejectsInvalidConfiguredOrigins(string origin)
        {
            Action createPolicy = () => new DownloadUrlPolicy(new[] { origin });

            createPolicy.Should().Throw<ArgumentException>();
        }

    }
}
