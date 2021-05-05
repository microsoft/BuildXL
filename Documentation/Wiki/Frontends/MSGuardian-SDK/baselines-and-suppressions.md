# Generating Baselines and Suppression files for BuildXL builds
The `guardian baseline` and `guardian suppress` commands can be used to baseline and suppress guardian violations. These must be run manually and committed to the repository.

1. Run BuildXL to get a failing build with Guardian violations.
2. Navigate to the output directory for the failing Guardian call (there will be a unique one for each call to Guardian.runGuardian()). The directory should be `Out/Objects/.../<unique directory>/guardianOut`.
3. Run the baseline or suppress command as described in the Guardian wiki with the addition of the following argument: `--settings-file .\buildxl.gdnsettings`
    * Example: `guardian baseline --settings-file .\buildxl.gdnsettings -f C:\repo\root\.config\guardian\buildxl_baseline`
4. Make sure to declare the new baseline or suppression files in the GuardianArguments structure.