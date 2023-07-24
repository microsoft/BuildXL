// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration;

/// <summary>
/// (Endpoint, Port) pair.
/// </summary>
public interface IDistributionServiceLocation
{
    /// <summary>
    /// This comes directly from the command line parameters, and might be either an IP address or a hostname
    /// depending on what the user gave us.
    /// </summary>
    string IpAddress { get; }

    /// <summary>
    /// Port
    /// </summary>
    int BuildServicePort { get; }
}