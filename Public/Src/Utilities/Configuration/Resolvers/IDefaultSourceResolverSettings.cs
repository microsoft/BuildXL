// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// A placeholder for the default source resolver.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1040:AvoidEmptyInterfaces")]
    public partial interface IDefaultSourceResolverSettings : IResolverSettings
    {
    }
}
