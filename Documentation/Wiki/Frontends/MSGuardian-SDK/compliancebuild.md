# Compliance Build on Cloudbuild
The compliance build on Cloudbuild will run various Guardian tools against the repository root on CB builds to ensure that the repository complies with various Microsoft security standards. Currently it is opt in while repositories are onboarded, but it will eventually be made mandatory for all builds. In its current state, the compliance build will run CredScan against the repo root as soon as it is checked out on Cloudbuild.

## Enabling Compliance Build
To enable (or disable) compliance build, use the `EnableGuardianBuild` flag in your Cloudbuild configuration when submitting a new build. Additionally, the `GuardianBuildFlags` argument (optional) can be added to pass flags into the Guardian SDK.

```
{
    'SpecificRunnerOptions' : {
        'EnableGuardianBuild' : true,
        'GuardianBuildFlags' : '/p:[Tool.Guardian]genBaseline=1',
    },
}
```

## Generating Baselines/Suppressions on Cloudbuild
Suppressions and baselines can be automatically generated on Cloudbuild by setting `/p:[Tool.Guardian]genBaseline=1` for baselines or `/p:[Tool.Guardian]genSuppressions=1` for suppressions (but not both at once) inside the `GuardianBuildFlags` argument in the configuration for the build. This will cause the Compliance build to pass by not allowing the build to break on Guardian errors, and will generate a set of baselines/suppressions under the `{ComplianceBuildLogDirectory}/Guardian` directory. These can be retrieved using the Cloudbuild web interface or with the log drop for the build.

Once generated, manually copy these files to the `{RepositoryRoot}/.config/buildxl/compliance` directory, and check them into the repository. Finally, rebuild without the genBaseline or genSuppressions flags and ensure that the Compliance build passes with the newly generated baselines or suppressions.

Note: The flags above should *only* be used temporarily to generate baselines for a single build. They should not be set in the queue configuration for a repository.