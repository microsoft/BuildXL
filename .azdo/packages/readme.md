# Nuget Output Filter Generation

The `generate-output-package-filter.yml` template generates an output filter from a set of nuget package names for the current build.

Package lists must be newline separated without the external package prefix ("Microsoft."). When creating a new package list, put the it must be named `packageListName.txt` and placed in the same directory as this readme file.