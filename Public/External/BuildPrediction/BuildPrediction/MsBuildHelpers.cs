// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;

namespace Microsoft.Build.Prediction
{
    /// <summary>
    /// Helper methods for working with the MSBuild object model.
    /// </summary>
    internal static class MsBuildHelpers
    {
        /// <summary>
        /// A static list containing the top-level Build target within which we evaluate
        /// the MSBuild build graph.
        /// </summary>
        public static readonly string[] BuildTargetAsCollection = { "Build" };

        /// <summary>
        /// MSBuild include list-string delimiters.
        /// </summary>
        /// <remarks>
        /// These are common split characters for dealing with MSBuild string-lists of the form
        /// 'item1;item2;item3', '   item1    ; \n\titem2 ; \r\n       item3', and so forth.
        /// </remarks>
        private static readonly char[] IncludeDelimiters = { ';', '\n', '\r', '\t' };

        /// <summary>
        /// Splits a given file list based on delimiters into a size-optimized list.
        /// If you only need an Enumerable, use <see cref="SplitStringListEnumerable" />.
        /// </summary>
        /// <param name="stringList">
        /// An MSBuild string-list, where whitespace is ignored and the semicolon ';' is used as a separator.
        /// </param>
        /// <returns>A size-optimized list of strings resulting from parsing the string-list.</returns>
        public static IList<string> SplitStringList(this string stringList)
        {
            string[] split = stringList.Trim().Split(IncludeDelimiters, StringSplitOptions.RemoveEmptyEntries);
            var splitList = new List<string>(split.Length);
            foreach (string s in split)
            {
                string trimmed = s.Trim();
                if (trimmed.Length > 0)
                {
                    splitList.Add(trimmed);
                }
            }

            return splitList;
        }

        /// <summary>
        /// Splits a given file list based on delimiters into an enumerable.
        /// If you need a size-optimized list, use <see cref="SplitStringList"/>.
        /// </summary>
        /// <param name="stringList">
        /// An MSBuild string-list, where whitespace is ignored and the semicolon ';' is used as a separator.
        /// </param>
        /// <returns>A size-optimized list of strings resulting from parsing the string-list.</returns>
        public static IEnumerable<string> SplitStringListEnumerable(this string stringList)
        {
            return stringList.Trim().Split(IncludeDelimiters, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0);
        }

        /// <summary>
        /// Evaluates a given value in a project's context.
        /// </summary>
        /// <param name="project">The MSBuild Project to use for the evaluation context.</param>
        /// <param name="unevaluatedValue">Unevaluated value.</param>
        /// <returns>List of evaluated values.</returns>
        public static IEnumerable<string> EvaluateValue(this Project project, string unevaluatedValue)
        {
            if (string.IsNullOrWhiteSpace(unevaluatedValue))
            {
                return Enumerable.Empty<string>();
            }

            string evaluated = project.ExpandString(unevaluatedValue);
            return SplitStringListEnumerable(evaluated);
        }

        /// <summary>
        /// Given string of semicolon-separated target names, gets the set of targets that are to be executed,
        /// given targets plus all targets that given targets depends on.
        /// </summary>
        /// <param name="project">An MSBuild Project instance to use for context.</param>
        /// <param name="targets">Semicolon separated list of targets that we should analyze.</param>
        /// <param name="activeTargets">Collection into which targets should be added.</param>
        public static void AddToActiveTargets(this Project project, string targets, Dictionary<string, ProjectTargetInstance> activeTargets)
        {
            AddToActiveTargets(project, targets.SplitStringList(), activeTargets);
        }

        /// <summary>
        /// Given list of target names, gets set of targets that are to be executed,
        /// for the provided target names and all targets that those depend on.
        /// </summary>
        /// <param name="project">An MSBuild Project instance to use for context.</param>
        /// <param name="evaluatedTargetNames">Previously split set of evaluated target names that we should analyze.</param>
        /// <param name="activeTargets">Collection into which targets should be added.</param>
        public static void AddToActiveTargets(
            this Project project,
            IEnumerable<string> evaluatedTargetNames,
            Dictionary<string, ProjectTargetInstance> activeTargets)
        {
            foreach (string targetName in evaluatedTargetNames)
            {
                // Avoid circular dependencies
                if (activeTargets.ContainsKey(targetName))
                {
                    continue;
                }

                // The Project or its includes might not actually include the target name.
                if (project.Targets.TryGetValue(targetName, out ProjectTargetInstance target))
                {
                    activeTargets.Add(targetName, target);

                    // Parse all parent targets that current target depends on.
                    AddToActiveTargets(project, project.EvaluateValue(target.DependsOnTargets), activeTargets);
                }
            }
        }

        public static List<ImportedTargetsWithBeforeAfterTargets> GetImportedTargetsWithBeforeAfterTargets(this Project project)
        {
            return project.Imports
                .SelectMany(import => import.ImportedProject.Targets)
                .Select(
                    target => new ImportedTargetsWithBeforeAfterTargets(
                        target.Name,
                        target.BeforeTargets,
                        target.AfterTargets))
                .ToList();
        }

        /// <summary>
        /// Expand (recursively) set of active targets to include targets which reference any
        /// of the active targets with BeforeTarget or AfterTarget.
        /// </summary>
        /// <param name="project">An MSBuild Project instance to use for context.</param>
        /// <param name="activeTargets">
        /// Set of active targets. Will be modified in place to add targets that reference this
        /// graph with BeforeTarget or AfterTarget.
        /// </param>
        public static void AddBeforeAndAfterTargets(this Project project, Dictionary<string, ProjectTargetInstance> activeTargets)
        {
            List<ImportedTargetsWithBeforeAfterTargets> allTargets = project.GetImportedTargetsWithBeforeAfterTargets();

            var newTargetsToConsider = true;
            while (newTargetsToConsider)
            {
                newTargetsToConsider = false;

                foreach (ImportedTargetsWithBeforeAfterTargets target in allTargets)
                {
                    // If the target exists in our project and is not already in our list of active targets ...
                    if (project.Targets.ContainsKey(target.TargetName)
                        && !activeTargets.ContainsKey(target.TargetName))
                    {
                        IEnumerable<string> hookedTargets = project.EvaluateValue(target.AfterTargets)
                            .Concat(project.EvaluateValue(target.BeforeTargets));
                        foreach (string hookedTarget in hookedTargets)
                        {
                            // ... and it hooks a running target with BeforeTargets/AfterTargets ...
                            if (activeTargets.ContainsKey(hookedTarget))
                            {
                                // ... then add it to the list of running targets ...
                                project.AddToActiveTargets(target.TargetName, activeTargets);

                                // ... and make a note to run again, since activeTargets has changed.
                                newTargetsToConsider = true;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Evaluates a condition in the context of a flattened ProjectInstance,
        /// avoiding processing where the condition contains constructs that are
        /// difficult to evaluate statically.
        /// </summary>
        /// <returns>
        /// If condition true or false. If any MSBuild project exception is thrown during evaluation,
        /// result will be false.
        /// </returns>
        /// <exception cref="InvalidProjectFileException">
        /// Thrown by MSBuild when evaluation fails. This exception cannot be easily
        /// handled locally, e.g. by catching and returning false, as it will usually
        /// leave the Project in a bad state, e.g. an empty Targets collection,
        /// which will affect downstream code that tries to evaluate targets.
        /// </exception>
        public static bool EvaluateConditionCarefully(this ProjectInstance projectInstance, string condition)
        {
            // To avoid extra work, return true (default) if condition is empty.
            if (string.IsNullOrWhiteSpace(condition))
            {
                return true;
            }

            // We cannot handle %(...) metadata accesses in conditions. For example, see these conditions
            // in Microsoft.WebApplication.targets:
            //
            //   <Copy SourceFiles="@(Content)" Condition="'%(Content.Link)' == ''"
            //   <Copy SourceFiles="@(Content)" Condition="!$(DisableLinkInCopyWebApplication) And '%(Content.Link)' != ''"
            //
            // Attempting to evaluate these conditions throws an MSB4191 exception from
            // Project.ReevaluateIfNecessary(), trashing Project (Project.Targets collection
            // becomes empty, for example). Extra info at:
            // http://stackoverflow.com/questions/4721879/ms-build-access-compiler-settings-in-a-subsequent-task
            // ProjectInstance.EvaluateCondition() also does not support bare metadata based condition parsing,
            // it uses the internal Expander class with option ExpandPropertiesAndItems but not the
            // more extensive ExpandAll or ExpandMetadata.
            // https://github.com/Microsoft/msbuild/blob/master/src/Build/Instance/ProjectInstance.cs#L1763
            if (condition.Contains("%("))
            {
                return false;
            }

            return projectInstance.EvaluateCondition(condition);
        }
    }
}
