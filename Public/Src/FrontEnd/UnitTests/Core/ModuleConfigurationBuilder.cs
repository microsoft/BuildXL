// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Helper class for building package/module configurations.
    /// </summary>
    public sealed class ModuleConfigurationBuilder
    {
        private readonly string m_packageName;
        private readonly bool m_useImplicitReferenceSemantics;
        private readonly string m_mainFile;

        private string[] m_projects;
        private string[] m_allowedDependencies;
        private string[] m_cyclicalFriendModules;
        private string m_extraFields;

        private ModuleConfigurationBuilder(string packageName, bool useImplicitReferenceSemantics, string mainFile = null)
        {
            m_packageName = packageName;
            m_useImplicitReferenceSemantics = useImplicitReferenceSemantics;
            m_mainFile = mainFile;
        }

        public static ModuleConfigurationBuilder V2Module(string name)
        {
            return new ModuleConfigurationBuilder(name, useImplicitReferenceSemantics: true);
        }

        public static ModuleConfigurationBuilder V1Module(string name, string mainFile = null)
        {
            return new ModuleConfigurationBuilder(name, useImplicitReferenceSemantics: false, mainFile: mainFile);
        }

        public ModuleConfigurationBuilder WithProjects(params string[] projects)
        {
            m_projects = projects;
            return this;
        }

        public ModuleConfigurationBuilder WithAllowedDependencies(params string[] allowedDependencies)
        {
            m_allowedDependencies = allowedDependencies;
            return this;
        }

        public ModuleConfigurationBuilder WithCyclicalFriendModules(params string[] friendModules)
        {
            m_cyclicalFriendModules = friendModules;
            return this;
        }

        public ModuleConfigurationBuilder WithExtraFields(string extraFields)
        {
            m_extraFields = extraFields;
            return this;
        }

        /// <nodoc />
        public static implicit operator string(ModuleConfigurationBuilder builder)
        {
            return builder.ToString();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            string @implicit =
                m_useImplicitReferenceSemantics
                    ? "NameResolutionSemantics.implicitProjectReferences,"
                    : "NameResolutionSemantics.explicitProjectReferences,";

            string projectsField = m_projects != null ? I($"\r\n\tprojects: [{string.Join(", ", m_projects.Select(f => I($"f`{f}`")))}],") : string.Empty;

            var dependencies = m_allowedDependencies != null
                ? I($"\r\n\tallowedDependencies: [{string.Join(", ", m_allowedDependencies.Select(dep => I($@"""{dep}""")))}],")
                : string.Empty;

            var cyclicalFriends = m_cyclicalFriendModules != null
                ? I($"\r\n\tcyclicalFriendModules: [{string.Join(", ", m_cyclicalFriendModules.Select(dep => I($@"""{dep}""")))}],")
                : string.Empty;

            var mainFile = string.IsNullOrEmpty(m_mainFile) ? string.Empty : $"main: f`{m_mainFile}`";

            var configFunctionName = m_useImplicitReferenceSemantics ? "module" : "package";
            return $@"
{configFunctionName}({{
    name: ""{m_packageName}"",
    nameResolutionSemantics: {@implicit}
    {projectsField}
    {mainFile}
    {dependencies}
    {cyclicalFriends}
    {m_extraFields ?? string.Empty}
}});";
        }
    }
}
