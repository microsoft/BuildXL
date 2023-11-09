// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace BuildXL.ToolSupport
{
    /// <summary>
    /// Factory class to create option handlers.
    /// </summary>
    public static class OptionHandlerFactory
    {
        /// <summary>
        /// Returns a singleton array containing a single OptionHandler instance for given name/action.
        /// </summary>
        public static OptionHandler[] CreateOption(string name, Action<CommandLineUtilities.Option> action, bool isUnsafe = false, Func<bool> isEnabled = null)
        {
            return new[] { new OptionHandler(name, action, isUnsafe), };
        }

        /// <summary>
        /// Returns an array containing two OptionHandler instances for two given names, both having the same action.
        /// </summary>
        public static OptionHandler[] CreateOption2(string name1, string name2, Action<CommandLineUtilities.Option> action, bool isUnsafe = false)
        {
            return new[] { new OptionHandler(name1, action, isUnsafe), new OptionHandler(name2, action, isUnsafe), };
        }

        /// <summary>
        /// Returns an array with three OptionHandler instances, having the following names: name, name+, name-.
        /// The "action" argument must accept two arguments: the original CommandLineUtilities.Option,
        /// and a boolean corresponding to the sign suffix (no suffix means true).
        /// </summary>
        public static OptionHandler[] CreateBoolOptionWithValue(
            string name,
            Action<CommandLineUtilities.Option, bool> action,
            bool isUnsafe = false,
            Func<bool> isEnabled = null,
            bool inactive = false)
        {
            return new[]
                   {
                           new OptionHandler(name, opt => action(opt, true), isUnsafe, isEnabled: isEnabled, inactive: inactive),
                           new OptionHandler(name, opt => action(opt, true), isUnsafe, isEnabled: () => true, suffix: "+", inactive: inactive),
                           new OptionHandler(name, opt => action(opt, false), isUnsafe, isEnabled: () => false, suffix: "-", inactive: inactive),
                       };
        }

        /// <summary>
        /// Returns an array with three OptionHandler instances, having the following names: name, name+, name-.
        /// The "action" argument must accept only one argument, which is a boolean corresponding to the sign suffix (no suffix means true).
        /// </summary>
        public static OptionHandler[] CreateBoolOption(string name, Action<bool> action, bool isUnsafe = false, bool inactive = false)
        {
            return CreateBoolOptionWithValue(name, (opt, sign) => action(sign), isUnsafe: isUnsafe, inactive: inactive);
        }

        /// <nodoc/>
        public static OptionHandler[] CreateBoolOption2(
            string name1,
            string name2,
            Action<bool> action,
            bool isUnsafe = false,
            bool inactive = false)
        {
            var options = new List<OptionHandler>();
            options.AddRange(CreateBoolOption(name1, action, isUnsafe, inactive));
            options.AddRange(CreateBoolOption(name2, action, isUnsafe, inactive));
            return options.ToArray();
        }
    }
}
