# Server mode
Server mode is an optimization to speed up back-to-back builds. It is recommended for use on dev machine builds but not in a datacenter. It is currently only available on Windows.

## How does server mode work? 
It works by spawning a second bxl.exe 'server' process as a child process of the user initated bxl.exe process. This second process is the one that actually performs the build. It communicates back with the original bxl.exe 'client' process to send console output and results back to the user. When the build completes, the server process stays alive and keeps some state in memory to make subsequent builds faster.

In a subsequent build, the user will launch a second bxl.exe client process which will connect to the existing server process to perform the build. If no build is requested of a server process for 60 minutes, it will shut itself down.

## Configuring server mode 
Server mode is controlled by the `/server` flag. Run `bxl.exe /help` for more details. It is enabled by default so you shouldn't ever need to add the flag. Job objects can be configured to disallow child processes to break away from the job object. Server mode honors this setting. So even if `/server+` is passed, a server mode build may not be performed if the process is launched by a job object which disallows breakaway. Some automation systems and wrappers such as Azure DevOps Build controllers or perl.exe disallow job object breakaway which effectively disables server mode.

### Server deployment directory
Since the server process is long running, issues could arise when trying to update bxl.exe and its runtime dependencies if the server is still running. To prevent this, server mode creates a deployment the first time it is run. This is just a copy of bxl.exe and its supporting dependencies. By default the deployment is created in a sibling directory to bxl.exe. The location can be configured via the `/serverDeploymentDir` parameter. See `bxl.exe /help:verbose` for more details.

### Idle time
By default the server process will exit when no build has been performed for 60 minutes. This value is configurable with `/serverMaxIdleTimeInMinutes`

