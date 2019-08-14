# Changelog

### 0.1.5 | 2019-03-21

- Log folder to download VS Code into.

### 0.1.4 | 2019-03-21

- Add `-NoProfile`, `-NonInteractive` and `-NoLogo` for using PowerShell to extract VS Code. [#2](https://github.com/Microsoft/vscode-test/issues/2).
- Use `Microsoft.PowerShell.Archive\Expand-Archive` to ensure using built-in `Expand-Archive`. [#2](https://github.com/Microsoft/vscode-test/issues/2).

### 0.1.3 | 2019-03-21

- Support specifying testing locale. [#1](https://github.com/Microsoft/vscode-test/pull/1).
- Fix zip extraction failure where `.vscode-test/vscode-<VERSION>` dir doesn't exist on Linux. [#3](https://github.com/Microsoft/vscode-test/issues/3).