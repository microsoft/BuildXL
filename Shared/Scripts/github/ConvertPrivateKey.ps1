<#
This script will generate a copy of your private key in a single line so that it can be copied into the keyvault as a secret.
NOTE: Please make sure the file generated here is permanatly deleted from your machine after key rotation is done
#>
if (!(Test-Path "$HOME/.ssh/id_ed25519")) {
    Write-Error "$HOME/.ssh/id_ed25519 file does not exist. Please generate a new ssh ed25519 key pair." -ErrorAction Stop
}

$allLines = Get-Content "$HOME/.ssh/id_ed25519"
$allLines -join(" ") | Set-Content "$HOME/.ssh/single_line"