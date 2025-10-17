# Overview

Plugins are a way to extend or modify certain behaviors in BuildXL. A plugin runs as a separate process. BuildXL communicates with plugin over gRPC, where the plugin acts as a server and BuildXL acts as the client and local network messages are sent back and forth. 

## How does it work
* Pass `/enablePlugins+` command arguments to BuildXL to allow plugin mode
* Pass `/pluginPaths:<list of paths>` (each absolute path can be separated by `;`) command arguments to BuildXL to tell where to find plugins.
  * Alternatively, you can pass the absolute path to a `.config` file, containing various plugin settings _(see PluginConfig below)_
* BuildXL will load plugins one by one
* BuildXL will shutdown all plugins when build is done
* BuildXL will choose the first plugin that can handle the request
* BuildXL will use its PluginClient to communicate with each plugin over gPRC (one client per plugin)

## Required implementations

Plugin implementation should implement a set of RPC operations (see [Interfaces.proto](../../../Public/Src/Utilities/Plugin.Grpc/Interfaces.proto)):
1. `Start`: instructs the plugin to start and load any necessary resources
2. `Stop`:  instructs the plugin to stop and clean up
3. `SupportedOperation`: gets operations that plugin support. This is used to register in BuildXL and dispatch messages accordingly
4. `Send`: sends `PluginMessage` to the plugin and expects to receive `PluginMessageResponse`. Both `PluginMessage` and `PluginMessageResponse` contain dynamic payloads. Based on the payload type in message received by plugin, it can infer type of request and respond accordingly. 
__Note:__ we don't support two plugins to have overlapped `supportedMessageType` (each plugin can handle multiple "operations"/"message types", but each "message type" can only be handled by one plugin). This restriction may be subject to change in future.

## Supported Operations or Message Types

Currently, plugins can do the following things (more functionality may be added in the future):
* __LogParse:__ when log messages are being written (`SandboxedProcessPipExecutor.TryLogOutputWithTimeoutAsync` & `SandboxedProcessPipExecutor.TryLogErrorAsync`), this will first send the messages to the plugin to modify the message before it is logged
* __ProcessResult:__ after BuildXL runs a pip and receives the result, it will call the plugin before it does any "analysis" or "post processing" of the pip result (`SandboxedProcessPipExecutor.ProcessSandboxedProcessResultAsync`). This allows the plugin to do initial analysis of a pip result and modify it before BuildXL analyzes it. This is useful if a pip has known intermittancy and should be retried or if a pip fails, but returns a successful status code.

## Optional extra properties that can be returned with SupportedOperation

In response to the `SupportedOperation` request, the only required property in the response is `SupportedOperation`. However, additional optional properties can also be included to further customize the communication between BuildXL and the plugin:
* __Timeout:__ (in milliseconds) this allows the plugin to customize the gRPC "deadline" that is used for each request (when exceeded - due to network bottlenecking or other factors - gRPC will mark the request as failed and BuildXL will unregister the plugin)
* __SupportedProcesses:__ a list of process names (case-insensitive, name & extension) to scope which messages should be sent to the plugin. If specified, messages will not be sent to the plugin unless the pip was for one of these processes.

## How to implement new plugin

### plugin client 
Plugin client implementation is in BuildXL codebase and it is supposed to bootstrap any plugin if you follow the requirements. Generally speaking, you don't need to make change to it, unless:

* A new type of plugin message 
* Add a new type of plugin supported operation
* Add new extensible point

### plugin
Your plugin implementation should have implementations for handle those required rpc operations, see `LogParsePluginServer.cs` as an example

## PluginConfig

The plugin can take a (subjectively) long time to start up: plugin process started -> `Start` message sent & returned -> `SupportedOperation` message sent & returned -> plugin stored along with its supported message type(s) as an "active plugin".

This can be shortened significantly by providing a config file. The config file provides the "answers" to the `SupportedOperation` call so BuildXL doesn't have to send it. Additionally, if using the "Supported Processes" feature to scope calls to your plugin to only specific processes, passing your plugin via a config file (and specifying `supportedProcesses`) means that BuildXL won't wait on plugin startup until it encounters one of those specified processes.

Example config:
```json
{
    "pluginPath" : "C:\\path\\to\\my\\plugin.exe",
    "timeout" : 3000, // Optional: specify a gRPC per-message deadline (in ms) different than the default
    "exitGracefully": false, // Optional (true by default): whether BuildXL should send a "Stop" message to the plugin before shutting down
    "supportedProcesses" : [ // Optional: specify a list of processes (case-insensitive, name & extension) to scope requests to the plugin
        "msbuild"
    ],
    "messageTypes" : [
        "ParseLogMessage"
    ]
}
```