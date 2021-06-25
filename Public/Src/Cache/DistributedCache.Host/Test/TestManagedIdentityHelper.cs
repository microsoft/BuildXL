// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.Host.Configuration;
using FluentAssertions;
using Xunit;

#nullable enable

namespace BuildXL.Cache.Host.Test
{
    public class TestManagedIdentityHelper
    {
        [Fact]
        public void RoundTrip()
        {
            Uri uri = new Uri("sb://www.bing.com");
            string ehName = "eventHub";
            string identity = Guid.NewGuid().ToString();
            var newUriString = ManagedIdentityUriHelper.BuildString(uri, ehName, identity);

            ManagedIdentityUriHelper.TryParseForManagedIdentity(newUriString, out Uri? eventHubNamespaceUri, out string? foundEventHubName, out string? foundManagedIdentityId)
                .Should().BeTrue();

            eventHubNamespaceUri.Should().Be(uri);
            foundEventHubName.Should().Be(ehName);
            foundManagedIdentityId.Should().Be(identity);
        }

        [Fact]
        public void ConcreteExample()
        {
            string uri = "sb://yourEventHubNamespace.servicebus.windows.net";
            string identity = "my-identity-guid";
            string eventHubName = "eventHubName";

            ManagedIdentityUriHelper.TryParseForManagedIdentity(
                $"{uri}/?name={eventHubName}&identity={identity}",
                out Uri? foundEventHubNamespaceUri,
                out string? foundEventHubName,
                out string? foundManagedIdentityId)
                    .Should().BeTrue();

            foundEventHubNamespaceUri.Should().Be(uri);
            foundEventHubName.Should().Be(eventHubName);
            foundManagedIdentityId.Should().Be(identity);
        }
    }
}
