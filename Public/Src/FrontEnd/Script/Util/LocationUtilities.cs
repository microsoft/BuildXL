// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

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
