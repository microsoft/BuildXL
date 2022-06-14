// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.Host.Configuration;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Information about the host machine/stamp that cache drive cache construction/configuration
    /// </summary>
    public class HostInfo
    {
        public HostParameters Parameters { get; }

        public string StampId => Parameters.Stamp;

        public string RingId => Parameters.Ring;

        public IEnumerable<string> Capabilities { get; }

        public HostInfo(HostParameters parameters)
        {
            Parameters = parameters;
            Capabilities = Enumerable.Empty<string>();
        }

        public HostInfo(string stampId, string ringId, IEnumerable<string> capabilities)
        {
            Parameters = new HostParameters()
            {
                Stamp = stampId,
                Ring = ringId
            };

            Capabilities = new List<string>(capabilities);
        }

        internal string AppendRingSpecifierIfNeeded(string s, bool useRingIsolation)
        {
            if (useRingIsolation)
            {
                s += CreateRingSuffix();
            }

            return s;
        }

        /// <summary>
        /// Creates a ring suffix which is compliant in an Azure blob container name
        /// </summary>
        private string CreateRingSuffix()
        {
            IEnumerable<char> getChars()
            {
                // Start with a hyphen
                yield return '-';

                foreach (var c in RingId)
                {
                    if (char.IsLetterOrDigit(c))
                    {
                        yield return char.ToLowerInvariant(c);
                    }
                }
            }

            return new string(getChars().ToArray());
        }
    }
}
