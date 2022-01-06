// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Information about the host machine/stamp that cache drive cache construction/configuration
    /// </summary>
    public class HostInfo
    {
        public string StampId { get; }

        public string RingId { get; }

        public IEnumerable<string> Capabilities { get; }

        public HostInfo(string stampId, string ringId, IEnumerable<string> capabilities)
        {
            StampId = stampId;
            RingId = ringId;
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
