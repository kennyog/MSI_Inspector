#!/usr/bin/env bash
# Builds a folder of small test MSIs for trying out MSI Inspector, using msitools (wixl + msibuild).
# The set deliberately includes duplicate codes, shared custom actions, nested folders, and a broken file.
#   macOS:  brew install msitools      Linux: apt install msitools wixl
# Usage: tests/make-test-msis.sh [output-folder]
set -euo pipefail

OUT="${1:-test-msis}"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$OUT"
echo "Test file for MSI Inspector" > "$WORK/readme.txt"

# make_msi <path> <name> <version> <manufacturer> <ProductCode> <UpgradeCode> <PackageCode>
make_msi() {
  local path="$1" name="$2" version="$3" mfr="$4" product="$5" upgrade="$6" package="$7"
  mkdir -p "$(dirname "$OUT/$path")"
  cat > "$WORK/p.wxs" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">
  <Product Id="$product" Name="$name" Language="1033" Version="$version" Manufacturer="$mfr" UpgradeCode="$upgrade">
    <Package Id="$package" InstallerVersion="200" Compressed="yes" Comments="Test package for MSI Inspector" />
    <Media Id="1" Cabinet="data.cab" EmbedCab="yes" />
    <Directory Id="TARGETDIR" Name="SourceDir">
      <Directory Id="ProgramFilesFolder">
        <Directory Id="INSTALLDIR" Name="TestApp">
          <Component Id="Main" Guid="{8E0A6F52-3C1D-4B7A-9F20-6D5E4C3B2A10}">
            <File Id="Readme" Name="readme.txt" Source="$WORK/readme.txt" />
          </Component>
        </Directory>
      </Directory>
    </Directory>
    <Feature Id="Complete" Level="1"><ComponentRef Id="Main" /></Feature>
  </Product>
</Wix>
EOF
  wixl -o "$OUT/$path" "$WORK/p.wxs"
  # wixl picks its own PackageCode, so set the one we want in the summary information
  msibuild "$OUT/$path" -s "$name" "$mfr" "Intel;1033" "$package"
}

# add_actions <path> <row>...  where each row is: Action|Type|Source|Target
add_actions() {
  local path="$1"; shift
  local sql="CREATE TABLE \`CustomAction\` (\`Action\` CHAR(72) NOT NULL, \`Type\` SHORT NOT NULL, \`Source\` CHAR(72), \`Target\` CHAR(255) PRIMARY KEY \`Action\`)"
  msibuild "$OUT/$path" -q "$sql" >/dev/null 2>&1 || true
  for row in "$@"; do
    IFS='|' read -r action type source target <<< "$row"
    msibuild "$OUT/$path" -q "INSERT INTO \`CustomAction\` (\`Action\`, \`Type\`, \`Source\`, \`Target\`) VALUES ('$action', $type, '$source', '$target')"
  done
}

U_EXAMPLE="{5B0E1D5C-7A2F-4E61-9C3B-21D4E6F8A001}"
P_EXAMPLE2="{C7D2A9E4-1B3F-4A6C-8E5D-0F9A2B4C6002}"
PK_EXAMPLE2="{E4F6A8B0-2C4D-4E6F-8A0B-2C4D6E8F0003}"

make_msi "VendorA/ExampleApp-1.0.msi" "Example App" "1.0.0" "Example Vendor" \
  "{A1B2C3D4-0001-4000-8000-000000000001}" "$U_EXAMPLE" "{F0E1D2C3-0001-4000-8000-0000000000A1}"
add_actions "VendorA/ExampleApp-1.0.msi" \
  "SetInstallDir|51|INSTALLDIR|[ProgramFilesFolder]Example App" \
  "CheckPrereqs|1|PrereqDll|CheckPrereqs" \
  "WriteConfig|3078|ConfigScript|"

make_msi "VendorA/ExampleApp-2.0.msi" "Example App" "2.0.0" "Example Vendor" \
  "$P_EXAMPLE2" "$U_EXAMPLE" "$PK_EXAMPLE2"
add_actions "VendorA/ExampleApp-2.0.msi" \
  "SetInstallDir|51|INSTALLDIR|[ProgramFilesFolder]Example App" \
  "CheckPrereqs|1|PrereqDll|CheckPrereqs" \
  "WriteConfig|3078|ConfigScriptV2|" \
  "CleanupOldVersion|3106|INSTALLDIR|cmd.exe /c rd /s /q \"[INSTALLDIR]old\""

# A rebuild that wrongly kept the same ProductCode AND PackageCode as 2.0
make_msi "VendorA/Rebuilds/ExampleApp-2.0-rebuild.msi" "Example App" "2.0.1" "Example Vendor" \
  "$P_EXAMPLE2" "$U_EXAMPLE" "$PK_EXAMPLE2"
add_actions "VendorA/Rebuilds/ExampleApp-2.0-rebuild.msi" \
  "SetInstallDir|51|INSTALLDIR|[ProgramFilesFolder]Example App" \
  "CheckPrereqs|1|PrereqDll|CheckPrereqs"

make_msi "VendorB/SampleTool-5.0.msi" "Sample Tool" "5.0.2" "Sample Software Co" \
  "{B2C3D4E5-0005-4000-8000-000000000005}" "{6C1F2E6D-8B30-4F72-AD4C-32E5F7A9B004}" "{A9B8C7D6-0005-4000-8000-0000000000B5}"
add_actions "VendorB/SampleTool-5.0.msi" \
  "SetInstallDir|51|INSTALLDIR|[ProgramFilesFolder]Sample Tool" \
  "LaunchAfterInstall|210|SampleTool.exe|/firstrun" \
  "ShowUnsupportedOs|19||This version of Windows is not supported."

make_msi "VendorB/Nested/Deeper/Utility-1.2.msi" "Handy Utility" "1.2.0" "Sample Software Co" \
  "{C3D4E5F6-0012-4000-8000-000000000012}" "{7D2A3F7E-9C41-4A83-BE5D-43F6A8BAC005}" "{B8C7D6E5-0012-4000-8000-0000000000C2}"

# Not a real MSI: checks the app reports unreadable files instead of crashing
mkdir -p "$OUT/Broken"
echo "this is not an msi" > "$OUT/Broken/not-really-an-installer.msi"

# msibuild leaves temporary files behind when a statement is skipped
find "$OUT" -name ".gsf-save-*" -delete

echo "Built test MSIs in $OUT:"
find "$OUT" -name "*.msi" | sort
