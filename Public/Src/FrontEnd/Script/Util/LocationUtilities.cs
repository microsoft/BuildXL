// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Instrumentation.Common;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Util
{
    // TODO: We need to clean up the use of these different location classes.
    internal static class LocationUtilities
    {
        /// <summary>
        /// Extension method that converts <see cref="LineInfo"/> to <see cref="Location"/>.
        /// </summary>
        public static Location AsLoggingLocation(this LineInfo lineInfo, string file)
        {
            return new Location { File = file, Line = lineInfo.Line, Position = lineInfo.Position };
        }
    }
}
