// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Constants = BuildXL.Cache.Roxis.Common.Constants;

namespace BuildXL.Cache.Roxis.Client
{
    /// <summary>
    /// Configuration for <see cref="RoxisClient"/>
    /// </summary>
    public class RoxisClientConfiguration
    {
        public string GrpcHost { get; set; } = "localhost";

        public int GrpcPort { get; set; } = Constants.DefaultGrpcPort;
    }
}
