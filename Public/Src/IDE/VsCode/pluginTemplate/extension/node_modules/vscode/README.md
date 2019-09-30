# vscode-extension-vscode

## ⚠️ Use @types/vscode and vscode-test instead ⚠️

The funcionality of `vscode` module has been splitted into `@types/vscode` and `vscode-test`. They have fewer dependencies, allow greater flexibility in writing tests and will continue to receive updates. Although `vscode` will continue to work, we suggest that you migrate to `@types/vscode` and `vscode-test`.

[Release Notes](https://code.visualstudio.com/updates/v1_36#_splitting-vscode-package-into-typesvscode-and-vscodetest) | [Migration Guide](https://code.visualstudio.com/api/working-with-extensions/testing-extension#migrating-from-vscode)

---

The `vscode` NPM module provides VS Code extension authors tools to write extensions. It provides the `vscode.d.ts` node module (all accessible API for extensions) as well as commands for compiling and testing extensions.

For more information around extension authoring for VS Code, please see http://code.visualstudio.com/docs/extensions/overview

# License

[MIT](LICENSE)
