LCS Template (structure only)
==============================

This folder is a structural skeleton for Unified→LCS conversion. It contains:
  - LcsSkeleton/
    - AOSService/Packages/files/   (empty; converter fills with dynamicsax-*.zip from Unified)
    - AOSService/Scripts/License/  (empty; converter adds license files)
    - HotfixInstallationInfo.xml   (overwritten by converter)

What the converter does when you use this template:
  - Copies this structure to the output
  - Replaces AOSService/Packages/files with the converted modules (.zip) from the Unified package
  - Overwrites HotfixInstallationInfo.xml with generated metadata
  - Writes license files to AOSService/Scripts/License/

What is NOT included (and why):
  - AXUpdateInstaller.exe, Microsoft.Dynamics.*.dll, other binaries in AOSService/Packages
  - Full Scripts folder content (beyond License)
  - These are Microsoft LCS runtime files. We cannot redistribute them.

For full LCS package parity (e.g. same layout as original LCS):
  1. Use a full LCS package you already have, for example:
     - Custom deployable package from your dev box:
       PackagesLocalDirectory\bin\CustomDeployablePackage\ImportISVLicense.zip
       (or the unpacked folder). Path example:
       C:\Users\<you>\AppData\Local\Microsoft\Dynamics365\RuntimeSymLinks\<env>\PackagesLocalDirectory\bin\CustomDeployablePackage\ImportISVLicense.zip
     - Or unpack an LCS package from the LCS asset library.
  2. Set that folder or .zip as the "LCS Template" path in Settings.
  The converter will then use your copy as the skeleton and only replace
  HotfixInstallationInfo.xml, Packages/files/*.zip, and Scripts/License.
  You can leave Scripts/License in the template empty; the converter overwrites it.

Default template in Docker: ImportISVLicense.zip (full LCS from CustomDeployablePackage) is included and used by default (DeployPortal__LcsTemplatePath=/app/Resources/LcsTemplate/ImportISVLicense.zip). You can override with DeployPortal__LcsTemplatePath or mount your own template.
