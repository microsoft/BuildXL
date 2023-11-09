// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Utilities.Collections;

namespace BuildXL.ToolSupport
{
    /// <summary>
    /// Matches and execute option handlers based on parsed command line options.
    /// </summary>
    public static class OptionHandlerMatcher
    {
        /// <summary>
        /// Given a collection of <paramref name="options"/> representing parsed command line options, look for a proper match under <paramref name="optionHandlers"/> 
        /// and execute the corresponding action.
        /// </summary>
        /// <remarks>
        /// If no handler is found for a given option, the traversal is short circuited and the <paramref name="unrecognizedOption"/> is returned.
        /// If a matching handler is flagged as unsafe or the option name is contained under <paramref name="specialCaseUnsafeOptions"/>, the <paramref name="onSpecialCaseUnsafeOption"/> 
        /// action is executed.
        /// </remarks>
        /// <returns>Whether all provided options had a matching handler</returns>
        public static bool TryMatchAndExecuteHandler(
            IEnumerable<CommandLineUtilities.Option> options, 
            OptionHandler[] optionHandlers,
            out CommandLineUtilities.Option unrecognizedOption,
            IReadOnlySet<string> specialCaseUnsafeOptions = null,
            Action<string> onSpecialCaseUnsafeOption = null)
        {
            foreach (CommandLineUtilities.Option opt in options)
            {
                int min = 0;
                int limit = optionHandlers.Length;
                while (min < limit)
                {
                    // This avoids overflow, while i = (min + limit) >> 1 could overflow.
                    int i = unchecked((int)(((uint)min + (uint)limit) >> 1));

                    int order = string.Compare(optionHandlers[i].OptionName + optionHandlers[i].Suffix, opt.Name, StringComparison.OrdinalIgnoreCase);
                    if (order < 0)
                    {
                        min = i + 1;
                    }
                    else
                    {
                        // Since i < limit, this is guaranteed to make progress.
                        limit = i;
                    }
                }

                // Check for equality
                if (min < optionHandlers.Length)
                {
                    if (string.Equals(optionHandlers[min].OptionName + optionHandlers[min].Suffix, opt.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        // min it is
                        optionHandlers[min].Action(opt);

                        // compile list of user-enabled unsafe options to log later
                        if (specialCaseUnsafeOptions?.Contains(opt.Name) == true || (optionHandlers[min].IsUnsafe && optionHandlers[min].IsEnabled()))
                        {
                            onSpecialCaseUnsafeOption?.Invoke(optionHandlers[min].OptionName);
                        }

                        continue;
                    }
                }

                // unknown argument, fail
                unrecognizedOption = opt;
                return false;
            }

            unrecognizedOption = default;
            return true;
        }
    }
}
