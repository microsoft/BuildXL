# LanguageServerProtocol

A library to handle [Language Server Protocol](https://github.com/Microsoft/language-server-protocol).

A sample program is available at: [matarillo/vscode-languageserver-csharp-example](https://github.com/matarillo/vscode-languageserver-csharp-example)

## Installation

[NuGet Package](https://www.nuget.org/packages/LanguageServerProtocol/) is available. Run the following command in [NuGet Package Manager Console](https://docs.microsoft.com/ja-jp/nuget/tools/package-manager-console).

```
PM> Install-Package LanguageServerProtocol
```

## Usage

- Define a connection class derived from `LanguageServer.ServiceConnection`.
- To handle messages from client to server, override virtual methods.
- To handle messages from server to client, call methods of `LanguageServer.Client.ClientProxy`, `LanguageServer.Client.WindowProxy`, `LanguageServer.Client.WorkspaceProxy`, and `LanguageServer.Client.TextDocumentProxy` classes via `Proxy` property of the connection.
- To start listening, call `Listen()` method of the connection.
