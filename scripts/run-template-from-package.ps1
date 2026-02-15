# Example: run New-LcsTemplateFromPackage.ps1 with a source package path.
# CMD:  set SRC=C:\Packages\MyProduction.zip && powershell -NoProfile -File "%~dp0run-template-from-package.ps1"
# PowerShell: $env:SRC = "C:\Packages\MyProduction.zip"; .\run-template-from-package.ps1
$src = if ($env:SRC) { $env:SRC } else { "C:\Packages\MyProduction.zip" }
& "$PSScriptRoot\New-LcsTemplateFromPackage.ps1" -SourceZip $src -RemovePackagePayload:$true
