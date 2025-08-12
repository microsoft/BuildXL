// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Tool.ServicePipDaemon
{
    /// <nodoc/>
    public readonly struct RelativePathReplacementArguments
    {
        /// <nodoc/>
        public string OldValue { get; }

        /// <nodoc/>
        public string NewValue { get; }

        /// <nodoc/>
        public bool IsValid => OldValue != null && NewValue != null;

        /// <nodoc/>
        public RelativePathReplacementArguments(string oldValue, string newValue)
        {
            OldValue = oldValue;
            NewValue = newValue;
        }

        /// <nodoc/>
        public static RelativePathReplacementArguments Invalid => new RelativePathReplacementArguments(null, null);
    }
}
