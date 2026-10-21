# MSI Inspector

A small Windows app for people who work with lots of MSI installers. Point it at a folder and it
finds every `.msi` file in it and all its subfolders, then shows their identifiers in a sortable,
spreadsheet-style grid.

## What it does

- **Scan a folder tree** for MSI files and list each one with its **ProductCode**, **UpgradeCode**,
  **PackageCode**, product name, version, manufacturer, and language.
- **Sort** by any column, **filter** by any text, and **copy** any cell or row.
- **Find duplicates** with one click:
  - **ProductCode**: two different packages claiming to be the same product release.
  - **UpgradeCode**: files from the same product family (expected for versions of one product).
  - **PackageCode**: different files that should never share one; usually a packaging bug.
- **Custom actions**: see every custom action across all scanned MSIs, search them by name,
  source, or target, and list the ones **shared by two or more MSIs**, with how many different
  implementations share each name.
- **Export** what's shown to CSV.

It opens every MSI **read-only**, using Windows Installer's own API, so it never changes your packages.

## Download

Get the latest version from the [Releases page](https://github.com/kennyog/MSI_Inspector/releases/latest):

- `MsiInspector-win-x64.exe` for most Windows PCs
- `MsiInspector-win-arm64.exe` for ARM devices (Surface Pro X, Windows on a Mac VM)

It's a single self-contained file; nothing else needs installing. Windows 10 or 11.

The app isn't code-signed yet, so the first time you run it Windows may show
"Windows protected your PC". Click **More info**, then **Run anyway**.

## Verify your download

Every release is built by GitHub Actions directly from this repository's source code. You can check that a download hasn't been tampered with in either of two ways:

- **Checksum**: compare against `SHA256SUMS.txt` on the release page:
  ```powershell
  Get-FileHash .\MsiInspector-win-x64.exe -Algorithm SHA256
  ```
- **Build attestation** (proves which commit and workflow built the file), with the GitHub CLI:
  ```powershell
  gh attestation verify .\MsiInspector-win-x64.exe --repo kennyog/MSI_Inspector
  ```

## Build it yourself

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then:

```powershell
dotnet publish src/MsiInspector -c Release -r win-x64 -o publish
```

The app is in `publish\MsiInspector.exe`. (It can also be compiled on macOS or Linux, but it only runs on Windows.)

## License

[MIT](LICENSE)
