// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Core.dsc"/>
/// <reference path="Prelude.Configuration.dsc"/>

/** Project  description that could be used to configure projects */
interface ProjectDescription {
    /** Qualifier types relevant to the package */
    qualifierSpace?: QualifierSpace;
}

/** Configuration function that is used in projects for configuringa project. */
declare function project(project: ProjectDescription): void;

// Temporary function for declaring qualifier space
declare function declareQualifierSpace(qualifierSpace: QualifierSpace);

/** Function to switch between qualifiers on imports. */
declare function withQualifier(qualifier: Object);
