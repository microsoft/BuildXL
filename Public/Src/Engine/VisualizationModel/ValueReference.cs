// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// Value Identifier for Visualizer
    /// </summary>
    public class ValueReference
    {
        /// <summary>
        /// The value identifier
        /// </summary>
        public int Id;

        /// <summary>
        /// Value Name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// True if it has subvalues
        /// </summary>
        public bool HasSubValues { get; set; }

        /// <summary>
        /// Create reference from value pip
        /// </summary>
        public static ValueReference Create(SymbolTable symbolTable, PathTable pathTable, ValuePip valuePip)
        {
            return new ValueReference
            {
                Id = (int)valuePip.PipId.Value,
                Name = valuePip.LocationData.ToString(pathTable) + ":" + valuePip.Symbol.ToString(symbolTable),
            };
        }
    }
}
