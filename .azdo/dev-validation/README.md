# Developer validation pipeline

## Overview

This pipeline definition is used to run validations on developers' in-progress features.
With this pipeline definition, the user does not need to check in the features to the main branch
and wait for the release pipeline to run all validations.

This pipeline definition can be viewed as a "merged" definition of the rolling build and release pipeline definitions.
It takes the part of the rolling build definition that creates and publishes drop artifacts and NuGet packages, and
the part of the release definition that run validations. This pipeline definition ensures that the created artifacts
and packages have different names from the ones created and published by the official rolling build and release pipeline definitions.

## Instructions

The pipeline running this definition is [BuildXL Developer Validation](https://dev.azure.com/mseng/Domino/_build?definitionId=19875).

To run the pipeline, the user in-progress feature must be in `releases/dev-validation/<alias>/<feature_name>` branch. The requirement for being
under `releases` branch is due to the use of CB `BuildXL_Internal_Rolling` queue for building in CB.
