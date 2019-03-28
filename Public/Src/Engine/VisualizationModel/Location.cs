// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// ViewModel for a file location
    /// </summary>
    public sealed class Location : FileReference
    {
        /// <summary>
        /// The line in the file
        /// </summary>
        public int Line { get; set; }

        /// <summary>
        /// The position in the line
        /// </summary>
        public int Position { get; set; }

        /// <summary>
        /// Builds a location viewmodel from a Token.
        /// </summary>
        public static Location FromToken(PathTable pathTable, LocationData token)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(token != null);

            return new Location()
                   {
                       Id = token.Path.Value.Value,
                       Path = token.Path.ToString(pathTable),
                       Line = token.Line,
                       Position = token.Position,
                   };
        }
    }
}
