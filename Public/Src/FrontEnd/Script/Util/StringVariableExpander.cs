// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Util
{
    /// <summary>
    /// Expands environment variable names in a string
    /// </summary>
    public static class StringVariableExpander
    {
        private static readonly char s_variableSeparator = !OperatingSystemHelper.IsWindowsOS ? '$' : '%';

        /// <summary>
        /// Expands environment variables in a string by querying the engine for the corresponding build parameters
        /// </summary>
        /// <remarks>
        /// Even though there is a Environment.ExpandEnvironmentVariables(String) available in C# this function only works
        /// for Windows (https://github.com/dotnet/runtime/issues/25792). Furthermore, we need the expansion to consider
        /// the configured build parameters in addition to the actual environment, and to properly register the access to those parameters in order
        /// for incrementality to safely work.
        /// </remarks>
        /// <returns>Whether there is at least one environment variable in the string that was expanded</returns>
        public static bool TryExpandVariablesInString(Context context, string stringToExpand, FrontEndEngineAbstraction engine, out string expandedString)
        {
            // if the path does not contain an env variable, just return the original path to avoid extra allocations
#if NETCOREAPP
            if (stringToExpand?.Contains(s_variableSeparator) != true)
#else
            if (stringToExpand?.Contains(s_variableSeparator.ToString()) != true)
#endif
            {
                expandedString = stringToExpand;
                return false;
            }

            // Here we build the result, containing all variables expanded
            StringBuilder result = new StringBuilder(stringToExpand.Length);
            // Here we build the variable name we'll query the environment with. Reused across each variable
            StringBuilder variableName = new StringBuilder();

            // Whether we are traversing a variable name or a regular part of the string
            bool inAVariableName = false;
            // Whether there has been at least one successful expansion of the provided string
            bool atLeastOneExpansion = false;

            foreach (char c in stringToExpand)
            {
                // If the character is a variable separator and we are not parsing a variable name, then
                // this is the start of a variable name. Both Windows and non-Windows case start with a distinguished separator
                if (c == s_variableSeparator && !inAVariableName)
                {
                    inAVariableName = true;
                    // The separator is always discarded and not part of the name
                    continue;
                }
                // We are not parsing a variable name and this is a regular character of a string. Just make it part of the result
                else if (c != s_variableSeparator && !inAVariableName)
                {
                    result.Append(c);
                }
                // We are parsing a variable name and we are past the initial separator
                else
                {
                    // This is the end of the variable name
                    if (IsEndOfVariableName(c))
                    {
                        // Get the name and query the engine for the result. If the variable is not defined, just append the name unexpanded
                        // Observe that even when the variable is not defined, by querying the engine we will record this as an anti-dependency
                        var name = variableName.ToString();
                        if (!string.IsNullOrEmpty(name) && engine.TryGetBuildParameter(name, "DScript", out string value, context.TopStackLocation) && !string.IsNullOrEmpty(value))
                        {
                            result.Append(value);
                            atLeastOneExpansion = true;
                        }
                        else
                        {
                            AppendVariableUnexpanded(result, name);
                        }

                        // We are done parsing the variable name. Clear up the builder so subsequent names can be constructed.
                        variableName.Clear();
                        inAVariableName = false;

                        // If the end of the variable name is flagged by an explicit separator (e.g. %PATH%), then there is nothing else to do
                        // and this ending separator won't be part of the result.
                        // Otherwise the ending character can be either the start of a new variable or a regular part of the string
                        if (!EndOfVariableNameIsSeparator())
                        {
                            if (c == s_variableSeparator)
                            {
                                inAVariableName = true;
                                // The separator is always discarded and not part of the name
                                continue;
                            }
                            else
                            {
                                result.Append(c);
                            }
                        }
                    }
                    // We are parsing a variable name and the current char is part of the name
                    else
                    {
                        variableName.Append(c);
                    }
                }
            }

            // We finished traversing the string while interpreting a variable name
            if (inAVariableName)
            {
                // If a separator is needed to end a variable name, then this is a malformed name. E.g."C:\foo\%PATH"
                // So we leave it unexpanded
                if (EndOfVariableNameIsSeparator())
                {
                    result.Append(s_variableSeparator);
                    result.Append(variableName);
                }
                else
                {
                    // Otherwise, the end of the string defines the end of the name. Expand it as needed
                    var name = variableName.ToString();
                    if (!string.IsNullOrEmpty(name) && engine.TryGetBuildParameter(name, "DScript", out string value, context.TopStackLocation) && !string.IsNullOrEmpty(value))
                    {
                        result.Append(value);
                        atLeastOneExpansion = true;
                    }
                    else
                    {
                        AppendVariableUnexpanded(result, name);
                    }
                }
            }

            expandedString = result.ToString();
            return atLeastOneExpansion;
        }

        /// <summary>
        /// For Windows, the end of a variable name is signaled by a separator % (that is discarded on expansion). For Linux/Mac
        /// this is just a regular character that can't be part of variable name.
        /// </summary>
        private static bool EndOfVariableNameIsSeparator() => OperatingSystemHelper.IsWindowsOS;

        /// <summary>
        /// Appends an env var name unexpanded (%Name% for Windows, $Name for linux/mac)
        /// </summary>
        private static void AppendVariableUnexpanded(StringBuilder result, string value)
        {
            // $name for linux-like OS, %name% for Windows
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                result.Append(s_variableSeparator);
                result.Append(value);
            }
            else
            {
                result.Append(s_variableSeparator);
                result.Append(value);
                result.Append(s_variableSeparator);
            }
        }

        // Whether the given character signals the end of a variable name
        private static bool IsEndOfVariableName(char c)
        {
            // On Windows, variable names always end with a %. E.g. %PATH%
            if (OperatingSystemHelper.IsWindowsOS)
            {
                return c == s_variableSeparator;
            }
            // On linux/mac, variable names end when the first non-allowed character is found. E.g. $PATH/foo
            // Allowed characters are [a-zA-Z_]+[a-zA-Z0-9_]*. https://pubs.opengroup.org/onlinepubs/9699919799/utilities/V3_chap02.html#tag_18_10_02
            // Here we don't enforce the syntax, since a malformed name will just result in not expanding that name
            else
            {
                return !(('a' <= c && c <= 'z') || ('A' <= c && c <= 'Z') || ('0' <= c && c <= '9') || c == '_');
            }
        }
    }
}
