// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.JavaScript
{
    /// <summary>
    /// Provides facilities for filtering JS projects based on different criteria
    /// </summary>
    public sealed class JavaScriptProjectSelector
    {
        private static readonly ConcurrentDictionary<string, Regex> s_regexCache = new();

        /// <summary>
        /// Map from project name to all projects with that name (and different script commands)
        /// Speeds up searches under the assumption that there are usually a handful of script commands per project compared to the amount of project names
        /// </summary>
        private readonly MultiValueDictionary<string, JavaScriptProject> m_nameToProjects;

        /// <nodoc/>
        public JavaScriptProjectSelector(IReadOnlyCollection<JavaScriptProject> allProjects)
        {
            Contract.RequiresNotNull(allProjects);
            m_nameToProjects = allProjects.ToMultiValueDictionary(javaScriptProject => javaScriptProject.Name, javaScriptProject => javaScriptProject);
        }

        /// <summary>
        /// Returns all projects with the given package name
        /// </summary>
        private IReadOnlyList<JavaScriptProject> GetProjectsByName(string packageName)
        {
            Contract.AssertNotNull(packageName);

            if (!m_nameToProjects.TryGetValue(packageName, out var values))
            {
                return CollectionUtilities.EmptyArray<JavaScriptProject>();
            }

            return values;
        }


        /// <summary>
        /// Returns all projects where the regex matches the provided package name
        /// </summary>
        private IEnumerable<JavaScriptProject> GetProjectsByName(Regex nameRegex) => m_nameToProjects.Where(kvp => nameRegex.IsMatch(kvp.Key)).SelectMany(kvp => kvp.Value);

        /// <summary>
        /// Returns all projects that the actual selector in the discriminating union specifies
        /// </summary>
        public bool TryGetMatches(DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector> selector, out IReadOnlyCollection<JavaScriptProject> matches, out string failure)
        {
            failure = string.Empty;
            try
            {
                switch (selector.GetValue())
                {
                    case string s:
                        // Only the name needs to match
                        matches = GetProjectsByName(s);
                        return true;
                    case IJavaScriptProjectSimpleSelector simpleSelector:
                        // Match name and commands
                        matches = GetProjectsByName(simpleSelector.PackageName).Where(p => IsCommandMatch(simpleSelector, p)).ToList();
                        return true;
                    case IJavaScriptProjectRegexSelector regexSelector:
                        // Regex match name and commands
                        matches = GetProjectsByName(GetRegex(regexSelector.PackageNameRegex)).Where(p => IsCommandMatch(regexSelector, p)).ToList();
                        return true;
                    default:
                        Contract.Assert(false, $"Unexpected type {selector.GetValue().GetType()}");
                        matches = CollectionUtilities.EmptyArray<JavaScriptProject>();
                        return false;
                };
            }
            catch (ArgumentException e)
            {
                matches = CollectionUtilities.EmptyArray<JavaScriptProject>();
                failure = e.Message;
                return false;
            }
        }

        internal static bool TryMatch(DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector> selector, JavaScriptProject project, out bool isMatch, out string failure)
        {
            isMatch = false;
            failure = string.Empty;
            switch (selector.GetValue())
            {
                case string s:
                    isMatch = project.Name == s;
                    return true;
                case IJavaScriptProjectSimpleSelector simpleSelector:
                    isMatch = project.Name == simpleSelector.PackageName && IsCommandMatch(simpleSelector, project);
                    return true;
                case IJavaScriptProjectRegexSelector regexSelector:
                    try
                    {
                        var packageRegex = GetRegex(regexSelector.PackageNameRegex);
                        isMatch = packageRegex.IsMatch(project.Name) && IsCommandMatch(regexSelector, project);
                        return true;
                    }
                    catch (ArgumentException e)
                    {
                        failure = e.Message;
                        return false;
                    }
                default:
                    Contract.Assert(false, $"Unexpected type {selector.GetValue().GetType()}");
                    return false;
            }
        }

		/// <summary>
		/// Returns true if the project's command matches the selector
		/// </summary>
        private static bool IsCommandMatch(IJavaScriptProjectSimpleSelector simpleSelector, JavaScriptProject project)
        {
            return simpleSelector.Commands == null || simpleSelector.Commands.Contains(project.ScriptCommandName);
        }

		/// <summary>
		/// Returns true if the project's command matches the selector
		/// </summary>
		private static bool IsCommandMatch(IJavaScriptProjectRegexSelector regexSelector, JavaScriptProject project)
        {
            var commandRegex = regexSelector.CommandRegex != null ? GetRegex(regexSelector.CommandRegex) : null;
            return commandRegex == null || commandRegex.IsMatch(project.ScriptCommandName);
        }

        private static Regex GetRegex(string pattern)
        {
            return s_regexCache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.None));
        }
    }
}
