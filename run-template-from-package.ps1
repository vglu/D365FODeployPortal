# Path: folder "ф2" (Cyrillic) — use Unicode to avoid encoding issues
$src = "D:\Downloads\$([char]0x0444)2\AX_AIO_Main_Production_2026.2.4.4 (1).zip"
& "$PSScriptRoot\New-LcsTemplateFromPackage.ps1" -SourceZip $src -RemovePackagePayload:$true
