// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public class WarningHandling : IWarningHandling
    {
        /// <nodoc />
        public WarningHandling()
        {
            WarningsAsErrors = new List<int>();
            WarningsNotAsErrors = new List<int>();
            NoWarnings = new List<int>();
        }

        /// <nodoc />
        public WarningHandling(IWarningHandling template)
        {
            Contract.Assume(template != null);

            TreatWarningsAsErrors = template.TreatWarningsAsErrors;
            WarningsAsErrors = new List<int>(template.WarningsAsErrors);
            WarningsNotAsErrors = new List<int>(template.WarningsNotAsErrors);
            NoWarnings = new List<int>(template.NoWarnings);
        }

        /// <inheritdoc />
        public bool TreatWarningsAsErrors { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<int> WarningsAsErrors { get; set; }

        /// <inheritdoc />
        IReadOnlyList<int> IWarningHandling.WarningsAsErrors => WarningsAsErrors;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<int> WarningsNotAsErrors { get; set; }

        /// <inheritdoc />
        IReadOnlyList<int> IWarningHandling.WarningsNotAsErrors => WarningsNotAsErrors;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<int> NoWarnings { get; set; }

        /// <inheritdoc />
        IReadOnlyList<int> IWarningHandling.NoWarnings => NoWarnings;
    }
}
