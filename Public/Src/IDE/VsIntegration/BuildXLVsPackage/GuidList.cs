// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.VsPackage
{
    internal static class GuidList
    {
        public const string GuidDominoVsPackagePkgString = "76439c3a-9faf-4f38-9f54-f127e9be9171";
        public const string GuidDominoVsPackageCmdSetString = "ddab8610-5e3f-473e-a7a2-6336bb3a834c";

        public static readonly Guid GuidDominoVsPackageCmdSet = new Guid(GuidDominoVsPackageCmdSetString);
    }
}
