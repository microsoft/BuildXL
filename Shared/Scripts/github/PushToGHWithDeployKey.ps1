Param(
 [Parameter(mandatory=$true)]
 [String]$knownHostsSecureFilePath,
 # Keys default to environment variables so secrets are not passed as command-line arguments.
 [String]$privateKey = $env:GITHUB_DEPLOYKEY_PRIVATEKEY,
 [String]$publicKey = $env:GITHUB_DEPLOYKEY_PUBLICKEY
)

if ([string]::IsNullOrEmpty($privateKey) -or [string]::IsNullOrEmpty($publicKey)) {
    Write-Error "The GitHub deploy keys were not provided. Set GITHUB_DEPLOYKEY_PRIVATEKEY and GITHUB_DEPLOYKEY_PUBLICKEY." -ErrorAction Stop
}

# Creates a backup of a given file
function Backup-File {
    param([string]$filePath);

    if ((Test-Path $filePath)) {
        Rename-Item $filePath "$filePath.bak"
    }
}

# Creates a backup of the specified file if it exists before writing the specified content to it.
function Backup-And-Write-Content {
    param([string]$filePath, [string]$content);
    
    Backup-File $filePath
    Set-Content $filePath $content
}

# Deletes a generated file and restores a backup if one exists.
function Restore-Backup {
    param([string]$filePath);

    if (Test-Path -LiteralPath $filePath) {
        Remove-Item -LiteralPath $filePath -Force
    }

    $backupFilePath = "$filePath.bak"
    if (Test-Path -LiteralPath $backupFilePath) {
        Move-Item -LiteralPath $backupFilePath -Destination $filePath -Force
    }
}

$sshDirectory = "$HOME/.ssh"
$privateKeyFile = "$sshDirectory/id_ed25519"
$publicKeyFile = "$sshDirectory/id_ed25519.pub"
$knownHostsFile = "$sshDirectory/known_hosts"
$persistenceTestFile = "$sshDirectory/BUILDXL_GH_TEST_FILE"

# Test whether a known file exists under .ssh
# This may indicate that this machine was not cleaned up properly from a previous run
# If this is the case, ensure that the agent pool used for this task is one that does not have persistent files
# This job must use an agent pool that reimages machines after every run.
if ((Test-Path $persistenceTestFile)) {
    # If this file exists, do not proceed with github push, investigate which agent pool is assigned to this task
    Write-Error "This machine may not have been cleaned up from a previous run of this task! Verify the agent pool assigned to this task, and ensure that it reimages machines on every run. No changes were pushed to Github." -ErrorAction Stop
}

# Create .ssh directory if it does not exist
$sshDirCreated = $false
if (!(Test-Path $sshDirectory)) {
    New-Item -ItemType Directory -Path $sshDirectory
    $sshDirCreated = $true
}

try {
    # ----------Private Key----------
    # Get private key from keyvault and write it to file under .ssh.
    # Keys must have unix line endings.
    $header = "-----BEGIN OPENSSH PRIVATE KEY----- "
    $footer = "-----END OPENSSH PRIVATE KEY-----"

    $privateKey = $privateKey.replace($header, "")
    $privateKey = $privateKey.replace($footer, "")
    $privateKey = $privateKey.replace(" ", "`n")
    $privateKey = $header.substring(0, $header.Length-1) + "`n" + $privateKey + $footer + "`n"

    Backup-And-Write-Content $privateKeyFile $privateKey

    # ----------Public Key----------
    Backup-And-Write-Content $publicKeyFile $publicKey

    # ----------Known Hosts----------
    Backup-File $knownHostsFile
    Move-Item $knownHostsSecureFilePath $knownHostsFile

    # ----------SSH diagnostics----------
    $git = "c:\program files\git\bin\git.exe"
    $sshKeygen = "c:\program files\git\usr\bin\ssh-keygen.exe"
    $previousErrorActionPreference = $ErrorActionPreference
    $previousGitTrace = $env:GIT_TRACE
    $previousGitSshCommand = $env:GIT_SSH_COMMAND
    try {
        # Native stderr is captured below rather than treated as a PowerShell error.
        $ErrorActionPreference = "Continue"
        if (Test-Path $sshKeygen) {
            # Windows PowerShell needs explicit quotes to pass an empty argument to a native process.
            $derivedKey = & $sshKeygen -y -P '""' -f $privateKeyFile 2>&1
            $keyExitCode = $LASTEXITCODE
            if ($keyExitCode -eq 0) {
                $derivedParts = "$derivedKey" -split '\s+'
                $publicParts = $publicKey -split '\s+'
                $keysMatch = $derivedParts.Length -ge 2 -and $publicParts.Length -ge 2 -and
                    $derivedParts[0] -ceq $publicParts[0] -and $derivedParts[1] -ceq $publicParts[1]
                Write-Host "Deploy private key is readable; public key matches: $keysMatch"
            }
            else {
                Write-Host "Deploy private key could not be read noninteractively (ssh-keygen exit code $keyExitCode)."
            }
        }
        else {
            Write-Host "Git's ssh-keygen was not found; skipping deploy-key validation."
        }

        # This read-only probe uses the default SSH executable with verbose logging. It does not
        # change the SSH command used for the subsequent push.
        $env:GIT_TRACE = "1"
        $env:GIT_SSH_COMMAND = "ssh -v -o BatchMode=yes -o ConnectTimeout=15"
        Write-Host "Probing GitHub SSH with Git trace and verbose SSH (custom SSH commands are overridden for this probe)."
        & $git ls-remote "git@github.com:microsoft/BuildXL.git" HEAD 2>&1 | ForEach-Object {
            $line = "$_"
            if ($line -notmatch '(?i)sending env|executing proxy command') {
                foreach ($secret in @($env:GITHUB_DEPLOYKEY_PRIVATEKEY, $privateKey, $publicKey)) {
                    if (-not [string]::IsNullOrEmpty($secret)) {
                        $line = $line.Replace($secret, "[REDACTED]")
                    }
                }
                Write-Host $line
            }
        }
        Write-Host "GitHub SSH probe exit code: $LASTEXITCODE"
    }
    finally {
        $env:GIT_TRACE = $previousGitTrace
        $env:GIT_SSH_COMMAND = $previousGitSshCommand
        $ErrorActionPreference = $previousErrorActionPreference
    }

    # ----------Push to GH----------
    try {
        $ErrorActionPreference = "Continue"
        & $git "push" "git@github.com:microsoft/BuildXL.git" "HEAD:main" "--force" "--verbose" 2>&1 | ForEach-Object { Write-Host "$_" }
        $gitExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}
finally {
    # Clean up keys and known_hosts on both successful and failed pushes.
    $cleanupErrors = @()
    if ($sshDirCreated) {
        try {
            Remove-Item -LiteralPath $sshDirectory -Recurse -Force
        }
        catch {
            $cleanupErrors += $_.Exception
        }
    }
    else {
        foreach ($filePath in @($privateKeyFile, $publicKeyFile, $knownHostsFile)) {
            try {
                Restore-Backup $filePath
            }
            catch {
                $cleanupErrors += $_.Exception
            }
        }

        try {
            # Leave this sentinel so a later run can detect a persistent agent.
            New-Item -Path $persistenceTestFile -ItemType File -Force | Out-Null
        }
        catch {
            $cleanupErrors += $_.Exception
        }
    }

    if ($cleanupErrors.Count -gt 0) {
        $cleanupMessages = $cleanupErrors | ForEach-Object { $_.Message }
        throw "Failed to clean up SSH files: $($cleanupMessages -join '; ')"
    }
}

if ($gitExitCode -ne 0) {
    Write-Error "Force push to Github failed with exit code $gitExitCode" -ErrorAction Stop
}
