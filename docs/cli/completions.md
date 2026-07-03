# Shell tab completion

The Modulus CLI is built on System.CommandLine, which ships completion support for Bash, Zsh, and PowerShell via the [`dotnet-suggest`](https://www.nuget.org/packages/dotnet-suggest) global tool — no Modulus-specific setup required.

## 1. Install dotnet-suggest

```bash
dotnet tool install --global dotnet-suggest
```

## 2. Register the shim for your shell

### PowerShell

Add to your profile (`notepad $PROFILE`):

```powershell
# dotnet-suggest shim
$availableToExecute = Get-Command dotnet-suggest -ErrorAction Ignore
if ($availableToExecute) {
    Register-ArgumentCompleter -Native -CommandName modulus -ScriptBlock {
        param($wordToComplete, $commandAst, $cursorPosition)
        $fullpath = (Get-Command modulus).Source
        $arguments = $commandAst.Extent.ToString().Replace('"', '\"')
        dotnet-suggest get -e $fullpath --position $cursorPosition -- "$arguments" | ForEach-Object {
            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
        }
    }
}
```

### Bash

Append the official shim to your `~/.bashrc`:

```bash
curl -sSL https://raw.githubusercontent.com/dotnet/command-line-api/main/src/System.CommandLine.Suggest/dotnet-suggest-shim.bash >> ~/.bashrc
```

### Zsh

```bash
curl -sSL https://raw.githubusercontent.com/dotnet/command-line-api/main/src/System.CommandLine.Suggest/dotnet-suggest-shim.zsh >> ~/.zshrc
```

## 3. Try it

```bash
modulus add-<TAB>       # completes add-module, add-entity, add-command, ...
modulus doctor --<TAB>  # completes --solution, --json, --strict
```

Completions come from the CLI's own command tree, so new commands and options appear automatically after a tool update.
