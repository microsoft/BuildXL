// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc />
    public sealed class EsrpSignConfiguration : IEsrpSignConfiguration
    {
        /// <nodoc />
        public EsrpSignConfiguration()
        {}

        /// <nodoc />
        public EsrpSignConfiguration(IEsrpSignConfiguration template, PathRemapper pathRemapper)
        {
            SignToolPath = pathRemapper.Remap(template.SignToolPath);
            SignToolConfiguration = pathRemapper.Remap(template.SignToolConfiguration);
            SignToolEsrpPolicy = pathRemapper.Remap(template.SignToolEsrpPolicy);
            SignToolAadAuth = pathRemapper.Remap(template.SignToolAadAuth);
        }

        /// <inheritdoc />
        public AbsolutePath SignToolPath { get; set; }

        /// <inheritdoc />
        public AbsolutePath SignToolConfiguration { get; set; }

        /// <inheritdoc />
        public AbsolutePath SignToolEsrpPolicy { get; set; }

        /// <inheritdoc />
        public AbsolutePath SignToolAadAuth { get; set; }
    }
}
