// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.Stores;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// Represents the data that is associated with a content session.
    /// </summary>
    public class LocalContentServerSessionData : ISessionData
    {
        /// <inheritdoc /> 
        public string Name { get; }

        /// <inheritdoc /> 
        public Capabilities Capabilities { get; }

        /// <nodoc />
        public ImplicitPin ImplicitPin { get; }

        /// <nodoc />
        public IList<string> Pins { get; }

        /// <nodoc />
        public LocalContentServerSessionData(string name, Capabilities capabilities, ImplicitPin implicitPin, IList<string> pins)
        {
            Name = name;
            Capabilities = capabilities;
            ImplicitPin = implicitPin;
            Pins = pins;
        }

        /// <nodoc />
        public LocalContentServerSessionData(LocalContentServerSessionData other)
        {
            Name = other.Name;
            Capabilities = other.Capabilities;
            ImplicitPin = other.ImplicitPin;
            Pins = other.Pins;
        }
    }
}
