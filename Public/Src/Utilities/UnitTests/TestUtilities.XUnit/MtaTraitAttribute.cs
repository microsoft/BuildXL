// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Attribute that is used to mark a test as requiring MTA.
    /// </summary>
    [TraitDiscoverer("Xunit.Sdk.TraitDiscoverer", "xunit.core")]
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class MtaTraitAttribute : Attribute, ITraitAttribute
    {
        /// <summary>
        /// The name of the trait.
        /// </summary>
        public const string MtaTrait = "UseMta";

        /// <summary>
        /// Creates a new instance of the <see cref="MtaTraitAttribute"/> class.
        /// </summary>
        public MtaTraitAttribute(string name = MtaTrait, string value = "true") { }
    }
}
