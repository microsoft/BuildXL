// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
