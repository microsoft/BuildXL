# Changelog

### 0.4.3 | 2019-05-30

- Improved API documentation.

### 0.4.2 | 2019-05-24

- `testWorkspace` is now optional.

### 0.4.1 | 2019-05-02

- Fix Linux crash because `testRunnerEnv` is not merged with `process.env` for spawning the
testing process. [#14](https://github.com/Microsoft/vscode-test/issues/14c).

### 0.4.0 | 2019-04-18

- Add `testRunnerEnv` option. [#13](https://github.com/Microsoft/vscode-test/issues/13).

### 0.3.5 | 2019-04-17

- Fix macOS Insiders incorrect url resolve.

### 0.3.4 | 2019-04-17

- One more fix for Insiders url resolver.

### 0.3.3 | 2019-04-17

- Correct Insiders download link.

### 0.3.2 | 2019-04-17

- Correctly resolve Insider exectuable. [#12](https://github.com/Microsoft/vscode-test/issues/12).

### 0.3.1 | 2019-04-16

- Log errors from stderr of the command to launch VS Code.

### 0.3.0 | 2019-04-13

- 🙌 Add TypeScript as dev dependency. [#9](https://github.com/Microsoft/vscode-test/pull/9).
- 🙌 Adding a simpler way of running tests with only `vscodeExecutablePath` and `launchArgs`. [#8](https://github.com/Microsoft/vscode-test/pull/8).

### 0.2.0 | 2019-04-12

- 🙌 Set `ExecutionPolicy` for Windows unzip command. [#6](https://github.com/Microsoft/vscode-test/pull/6).
- 🙌 Fix NPM http/https proxy handling. [#5](https://github.com/Microsoft/vscode-test/pull/5).
- Fix the option `vscodeLaunchArgs` so it's being used for launching VS Code. [#7](https://github.com/Microsoft/vscode-test/issues/7).

### 0.1.5 | 2019-03-21

- Log folder to download VS Code into.

### 0.1.4 | 2019-03-21

- Add `-NoProfile`, `-NonInteractive` and `-NoLogo` for using PowerShell to extract VS Code. [#2](https://github.com/Microsoft/vscode-test/issues/2).
- Use `Microsoft.PowerShell.Archive\Expand-Archive` to ensure using built-in `Expand-Archive`. [#2](https://github.com/Microsoft/vscode-test/issues/2).

### 0.1.3 | 2019-03-21

- Support specifying testing locale. [#1](https://github.com/Microsoft/vscode-test/pull/1).
- Fix zip extraction failure where `.vscode-test/vscode-<VERSION>` dir doesn't exist on Linux. [#3](https://github.com/Microsoft/vscode-test/issues/3).