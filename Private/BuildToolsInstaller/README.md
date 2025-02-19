## 1ES Build Tools Installer

### Introduction
This program is used to make build tools available in a build running on an Azure Pipeline. The goal is that this program can be an entrypoint to many such build tools: different tools can implement their own installer logic, with patterns and general strategies that can be reused (e.g., downloading from a well-known Nuget mirror). Each 'tool installer' takes its specific configuration from a string file that the user can provide in the main configuration file (see _Configuration file_ below).

## Usage
  `1ES.BuildToolsInstaller install [options]`

### Options:

- `--tools <tools>` (REQUIRED)         Path to the JSON file listing the tools to install.
- `--toolsDirectory` <toolsDirectory>  The location where packages should be downloaded. Defaults to AGENT_TOOLSDIRECTORY if defined, or the working directory if not
- `--feedOverride` <feedOverride>      Uses this Nuget feed as the default upstream
- `-?, -h, --help`                     Show help and usage information

## Configuration file
The tools to install are given in a JSON file, with the path given to `--tools`, with the following format:

```json
{
  "Tools": [
    {
      "Tool": <tool-name>,
      "Version": <version-specification>,
      "OutputVariable": <string>,
      "IgnoreCache": <bool>,
      "AdditionalConfiguration": <string>
    },
    ...
  ]
}
```

where:

- `Tool`: The name of the tool to install
- `Version`: A version 'specification', from which the version to install will be resolved. This is typically a ring name, but some tools might allow explicit versions to be specified.
- `OutputVariable`: The directory where the tool was installed will be available in this variable. If the installer is running on an ADO job, this will set a pipeline variable (so this should be a valid variable name). If the installer is not running in an Azure DevOps pipeline, an environment variable is set instead.
- `AdditionalConfiguration`: Each tool is responsible for interpreting this value, which might for instance point to an additional configuration file, or hold some serialized arguments to be used for the installer
- `IgnoreCache`: If true, the tool will be installed even if it is already present in the cache

## Specific installers
### BuildXL

The installer is meant to be executed in two distinct scenarios:
1)	Image creation time  (running as part of an image prerequisite)
2)	Pipeline runtime (running within a task injected by 1ESPT)

In particular, the installer assumes that it is working in one of these two scenarios by checking some well known environment variable that is always present at pipeline runtime.

The relevant BuildXL package (based on the operating system and a provided version number) is downloaded from a well-known (or configuration-provided) feed. The version is resolved combining the user-provided configuration (see below), and a global configuration that is distributed by 1ES and is downloaded as part of the installation process.

At image creation time, the installer will place a number of versions of the tools in a well-known location (namely, the agent’s “tool cache”, which is available through the `AGENT_TOOLSDIRECTORY` environment variable at pipeline runtime). 
