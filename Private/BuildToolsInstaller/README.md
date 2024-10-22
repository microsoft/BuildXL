## 1ES Build Tools Installer

### Introduction
This program is used to make build tools available in a build running on an Azure Pipeline. The goal is that this program can be an entrypoint to many such build tools: different tools can implement their own installer logic, with patterns and general strategies that can be reused (e.g., downloading from a well-known Nuget mirror). Each 'tool installer' takes its specific configuration from a JSON file that the user can provide through the `--config` argument (see _Usage_ below)


## Usage
  1ES.BuildToolsInstaller [options]

### Options:


  `--tool <BuildXL> (REQUIRED)`:        The file to read and display on the console.

  `--toolsDirectory <toolsDirectory>`:  The location where packages should be downloaded. Defaults to AGENT_TOOLSDIRECTORY if defined, or the working directory if not

  `--config <configPath>`: Specific tool installer configuration file.

## Specific installers
### BuildXL

The installer is meant to be executed in two distinct scenarios:
1)	Image creation time  (running as part of an image prerequisite)
2)	Pipeline runtime (running within a task injected by 1ESPT)

In particular, the installer assumes that it is working in one of these two scenarios by checking some well known environment variable that is always present at pipeline runtime.

The relevant BuildXL package (based on the operating system and a provided version number) is downloaded from a well-known (or configuration-provided) feed. The version is resolved combining the user-provided configuration (see below), and a global configuration that is distributed by 1ES and is downloaded as part of the installation process.

At image creation time, the installer will place a number of versions of the tools in a well-known location (namely, the agent’s “tool cache”, which is available through the `AGENT_TOOLSDIRECTORY` environment variable at pipeline runtime). 

At pipeline runtime, the installer is run from a task before the build task that needs the tool, and the installed version is made available through the pipeline variable `ONEES_BUILDXL_LOCATION`. 

### Configuration schema
Configuration is optional, and it allows the user to provide some overrides.

```json
{
    "Version": string,          // Optional. Specify a version number to download
    "FeedOverride": string     // Optional. Forces the download to happen from this feed
}
```