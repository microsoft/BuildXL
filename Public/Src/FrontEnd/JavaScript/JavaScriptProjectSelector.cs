// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;

namespace BuildXL.FrontEnd.JavaScript
{
    /// <summary>
    /// Provides facilities for filtering JS projects based on different criteria
    /// </summary>
    public sealed class JavaScriptProjectSelector
    {
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
        public IReadOnlyCollection<JavaScriptProject> GetMatches(string packageName)
        {
            Contract.AssertNotNull(packageName);

            return GetMatches(new JavaScriptProjectSimpleSelector() { PackageName = packageName, Commands = null});
        }

        /// <summary>
        /// Returns all projects with the given package name and script command included in the list of script commands
        /// </summary>
        /// <remarks>
        /// If the selector commands is null, any script command is matched
        /// </remarks>
        public IReadOnlyCollection<JavaScriptProject> GetMatches(IJavaScriptProjectSimpleSelector simpleSelector)
        {
            Contract.AssertNotNull(simpleSelector);

            if (!m_nameToProjects.TryGetValue(simpleSelector.PackageName, out var values))
            {
                return CollectionUtilities.EmptyArray<JavaScriptProject>();
            }

            // If commands is not specified, all commands are returned
            if (simpleSelector.Commands == null)
            {
                return values;
            }

            // Otherwise, all script commands that match verbatim
            return values.Where(js => simpleSelector.Commands.Contains(js.ScriptCommandName)).ToArray();
        }

        /// <summary>
        /// Returns all projects that regex match both the provided package name and commands
        /// </summary>
        /// <remarks>
        /// If commands is null, any script command will match.
        /// </remarks>
        /// <exception cref="System.ArgumentException">A regular expression parsing error occurred</exception>
        public IReadOnlyCollection<JavaScriptProject> GetMatches(IJavaScriptProjectRegexSelector regexSelector)
        {
            var packageRegex = new Regex(regexSelector.PackageNameRegex, RegexOptions.None);
            var packages = m_nameToProjects.Keys.Where(projectName => packageRegex.IsMatch(projectName));

            var commandRegex = regexSelector.CommandRegex != null ? new Regex(regexSelector.CommandRegex, RegexOptions.None) : null;

            var matches = new List<JavaScriptProject>();
            foreach(string packageName in packages)
            {
                var projects = m_nameToProjects[packageName];

                if (commandRegex == null)
                {
                    matches.AddRange(projects);
                }
                else
                {
                    matches.AddRange(projects.Where(project => commandRegex.IsMatch(project.ScriptCommandName)));
                }
            }

            return matches;
        }

        /// <summary>
        /// Returns all projects that the actual selector in the discriminating union specifies
        /// </summary>
        public bool TryGetMatches(DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector> selector, out IReadOnlyCollection<JavaScriptProject> matches, out string failure)
        {
            failure = string.Empty;
            switch (selector.GetValue())
            {
                case string s:
                    matches = GetMatches(s);
                    return true;
                case IJavaScriptProjectSimpleSelector simpleSelector:
                    matches = GetMatches(simpleSelector);
                    return true;
                case IJavaScriptProjectRegexSelector regexSelector:
                    try
                    {
                        matches = GetMatches(regexSelector);
                    }
                    catch(ArgumentException e)
                    {
                        matches = CollectionUtilities.EmptyArray<JavaScriptProject>();
                        failure = e.Message;
                        return false;
                    }

                    return true;
                default:
                    Contract.Assert(false, $"Unexpected type {selector.GetValue().GetType()}");
                    matches = CollectionUtilities.EmptyArray<JavaScriptProject>();
                    return false;
            }
        }
    }
}
