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

    Remove-Item -Path $filePath

    if ((Test-Path $filePath)) {
        Rename-Item "$filePath.bak" $filePath
    }
}

$sshDirectory = "$HOME/.ssh"
$privateKeyFile = "$sshDirectory/id_ed25519"
$publicKeyFile = "$sshDirectory/id_ed25519.pub"
$knownHostsFile = "$sshDirectory/known_hosts"
$persistenceTestFile = "$sshDirectory/BUILDXL_GH_TEST_FILE"

# Test whether a known file exists under .ssh
# This may indicate that this machine was not cleaned up properly from a previous run
# If this is the case, ensure that the agent pool used for this job is one that does not have persistent files
# The correct agent pool for this task is the "Azure Pipelines" pool.
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

# ----------Private Key----------
# Get private key from keyvault, and write it to file under .ssh
# Keys must have unix line endings
$privateKey = "$(Key-Github-DeployKey-PrivateKey)"

$header = "-----BEGIN OPENSSH PRIVATE KEY----- "
$footer = "-----END OPENSSH PRIVATE KEY-----"

$privateKey = $privateKey.replace($header, "")
$privateKey = $privateKey.replace($footer, "")
$privateKey = $privateKey.replace(" ", "`n")
$privateKey = $header.substring(0, $header.Length-1)  + "`n" + $privateKey + $footer  + "`n"

Backup-And-Write-Content $privateKeyFile $privateKey

# ----------Public Key----------
# Write public key from key vault
$publicKey = "$(Key-Github-DeployKey-PublicKey)"
Backup-And-Write-Content $publicKeyFile $publicKey

# ----------Known Hosts----------
# known_hosts file is stored as a secure file for the pipeline
Backup-File $knownHostsFile
Move-Item $(File_Known_Hosts.secureFilePath) $knownHostsFile

# ----------Push to GH----------
& "c:\program files\git\bin\git" "push" "git@github.com:microsoft/BuildXL.git" "HEAD:master" "--force" "--verbose" | Write-Output
$gitExitCode = $LASTEXITCODE

# Write test file (this should be automatically deleted after every run, if it exists on the next run then this machine was not properly cleaned up)
New-Item -path $persistenceTestFile -type file

# ----------Clean up----------
# Clean up keys + known_hosts file before checking exit code
if ($sshDirCreated) {
    Remove-Item -Path $sshDirectory -Recurse -Force
}
else {
    Restore-Backup $privateKeyFile
    Restore-Backup $publicKeyFile
    Restore-Backup $knownHostsFile
}

if ($gitExitCode -ne "0") {
    Write-Error "Force push to Github failed with exit code $($gitExitCode)" -ErrorAction Stop
}