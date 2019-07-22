// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Sdk.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
    #pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId : ushort
    {
        // RESERVED TO [950, 960] (BuildXL.Frontend.Sdk)

        ErrorUnsupportedQualifierValue = 950,
        DuplicateFrontEndRegistration = 951,
        DuplicateResolverRegistration = 952,
    }
}
