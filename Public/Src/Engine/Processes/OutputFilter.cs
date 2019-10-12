using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;

namespace BuildXL.Processes
{
    internal readonly struct OutputFilter
    {
        /// <summary>
        /// Group name from the ErrorRegex to use to extract error message.
        /// When no such group exists, the entire match is used.
        /// </summary>
        private const string ErrorMessageGroupName = "ErrorMessage";

        internal readonly Predicate<string> LinePredicate;
        internal readonly Regex Regex;

        /// <summary>
        /// The GroupName to be used by the Regex
        /// </summary>
        /// <remarks>
        /// Defaults to ErrorMessageGroupName if nothing is provided when constructing the OutputFiler with a Regex
        /// </remarks>
        internal readonly string GroupName;

        internal OutputFilter(Predicate<string> linePredicate)
            : this(linePredicate, null, null)
        {
            Contract.Requires(linePredicate != null);
        }

        internal OutputFilter(Regex regex, string groupName = null)
            : this(null, regex, groupName)
        {
            Contract.Requires(regex != null);
        }

        private OutputFilter(Predicate<string> linePredicate, Regex regex, string groupName)
        {
            Contract.Requires(linePredicate != null || regex != null);
            LinePredicate = linePredicate;
            Regex = regex;
            GroupName = groupName ?? ErrorMessageGroupName;
        }

        /// <summary>
        /// When <see cref="LinePredicate" /> is specified: it is invoked against <paramref name="source" /> and if
        /// it returns true <paramref name="source" /> is returned.
        ///
        /// When <see cref="Regex" /> is specified: it is invoked against <paramref name="source" /> to find all
        /// matches; the matches are joined by <paramref name="outputSeparator"/> (or <see cref="Environment.NewLine" />
        /// if null) and returned.
        /// </summary>
        internal string ExtractMatches(string source, string outputSeparator = null)
        {
            if (LinePredicate != null)
            {
                return LinePredicate(source) ? source : string.Empty;
            }
            else
            {
                return string.Join(
                    outputSeparator ?? Environment.NewLine,
                    Regex
                        .Matches(source)
                        .Cast<Match>()
                        .Select(ExtractTextFromMatch));
            }
        }

        private string ExtractTextFromMatch(Match match)
        {
            Group regexGroupName = match.Groups[GroupName];
            return regexGroupName.Success
                ? regexGroupName.Value
                : match.Value;
        }

        /// <summary>
        /// If errRegex is set and its options include <see cref="RegexOptions.Singleline"/> (which means that
        /// the whole input string---which in turn may contain multiple lines---should be treated as a single line), returns the
        /// regex itself (to be used later to find all the matches in the input string); otherwise, returns a line filter
        /// (to be used later to match individual lines from the input string).
        /// </summary>
        /// <remarks>
        /// The errRegex must be inialized prior to calling this, which means after TryInitializeErrorRegexAsync in <see cref="SandboxedProcessPipExecutor"/>
        /// </remarks>
        internal static OutputFilter GetErrorFilter(Regex errRegex, bool enableMultiLineScanning)
        {
            if (errRegex != null && enableMultiLineScanning)
            {
                return new OutputFilter(errRegex);
            }
            else
            {
                return new OutputFilter(line =>
                {
                    Contract.Requires(line != null, "line must not be null.");

                    // in absence of regex, treating everything as error.
                    if (errRegex == null)
                    {
                        return true;
                    }

                    return errRegex.IsMatch(line);
                });
            }
        }

        /// <summary>
        /// Create an OutputFilter to match on all PipProperties by extracting all text between PipPropertyPrefix and PipPropertySuffix
        /// </summary>
        internal static OutputFilter GetPipPropertiesFilter(bool enableMultLineScanning)
        {
            const string PipPropertyPrefix = "PipProperty_";
            const string PipPropertySuffix = "_EndProperty";

            // Match everything between PipProperty_ and _EndProperty, assigned to a PipProperty_ group
            Regex pipPropertyRegex = new Regex(PipPropertyPrefix + @"(?<" + PipPropertyPrefix + @">.+?)" + PipPropertySuffix, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
            return new OutputFilter(pipPropertyRegex, PipPropertyPrefix);
        }
    }
}