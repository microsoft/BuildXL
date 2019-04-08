// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// Used for viewer to identify and display pip list
    /// </summary>
    public class PipReference
    {
        /// <summary>
        /// Pip Id
        /// </summary>
        public uint Id { get; set; }

        /// <summary>
        /// Pip Hash
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// Pip Description
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Pip State
        /// </summary>
        public PipState State { get; set; }

        /// <summary>
        /// Creates a new PipReferences from a pip
        /// </summary>
        public static PipReference FromPip(Pip pip)
        {
            Contract.Requires(pip != null);

            var visualizationInformation = EngineModel.VisualizationInformation;
            var context = visualizationInformation.Context.Value;
            var scheduler = visualizationInformation.Scheduler.Value;

            return new PipReference()
            {
                Id = PipGraph.GetUInt32FromPip(pip),
                Hash = pip.SemiStableHash.ToString("X16", CultureInfo.InvariantCulture),
                Description = pip.GetDescription(context),
                State = scheduler.GetPipState(pip.PipId),
            };
        }
    }
}
