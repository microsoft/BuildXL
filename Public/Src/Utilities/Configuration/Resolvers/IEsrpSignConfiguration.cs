// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <nodoc />
    public interface IEsrpSignConfiguration
    {

        /// <summary>
        /// Sign tool exe file
        /// </summary>
        AbsolutePath SignToolPath { get; }

        /// <summary>
        /// ESRP session information config, ESRPClient's -c argument
        /// </summary>
        AbsolutePath SignToolConfiguration { get; }

        /// <summary>
        /// ESRP policy information config, ESRPClient's -p argument
        /// </summary>
        AbsolutePath SignToolEsrpPolicy { get; }

        /// <summary>
        /// EsrpAuthentication.json
        /// </summary>
        AbsolutePath SignToolAadAuth { get; }
    }
}
