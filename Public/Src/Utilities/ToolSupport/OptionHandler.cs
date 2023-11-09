// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.ToolSupport
{
    /// <summary>
    /// And command line argument option
    /// </summary>
    public readonly struct OptionHandler
    {
        /// <summary>
        /// The name of the option with no suffixes.
        /// </summary>
        public readonly string OptionName;

        /// <summary>
        /// The action to take in response to the option being set.
        /// </summary>
        public readonly Action<CommandLineUtilities.Option> Action;

        /// <summary>
        /// After Action(opt) has run, determines whether the option is considered enabled or disabled.
        /// </summary>
        public readonly Func<bool> IsEnabled;

        /// <summary>
        /// Whether the option is considered unsafe, this should trigger a warning printout.
        /// </summary>
        public readonly bool IsUnsafe;

        /// <summary>
        /// Anything passed into the command line after the OptionName.
        /// </summary>
        public readonly string Suffix;

        /// <summary>
        /// Set when the option exist to prevent legacy command lines from failing, but no longer does anything.
        /// </summary>
        public readonly bool Inactive;

        /// <nodoc/>
        public OptionHandler(string optionName, Action<CommandLineUtilities.Option> action, bool isUnsafe, Func<bool> isEnabled = null, string suffix = "", bool inactive = false)
        {
            OptionName = optionName;
            Action = action;
            IsUnsafe = isUnsafe;
            IsEnabled = isEnabled ?? (() => true);
            Suffix = suffix;
            Inactive = inactive;
        }
    }
}

