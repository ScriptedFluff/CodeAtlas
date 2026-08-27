# Feature Specification: GitHub Release & Packaging Pipeline

## 1. Overview & Objective

Automate the build, packaging, and distribution of CodeAtlas via GitHub Actions.

The pipeline delivers three distribution artifacts per release:
1. **Windows Installer (`.msi`)**: Standard enterprise installer with PATH registration and Start Menu shortcuts.
2. **Portable Archives (`.zip` / `.tar.gz`)**: Standalone self-contained binaries for restricted environments where admin installation or MSI execution is prohibited.
3. **NuGet Package (`CodeAtlas.Abstractions.nupkg`)**: Standalone parser contracts published to NuGet.org / GitHub Packages.

---

## 2. Pipeline Artifact Matrix

| Artifact | Format | Target Users / Scenarios | Publishing Target |
|---|---|---|---|
| **Windows Installer** | `CodeAtlas-vX.Y.Z-win-x64.msi` | Standard developer machines; automatic PATH setup | GitHub Release |
| **Portable Windows** | `CodeAtlas-vX.Y.Z-win-x64.zip` | Enterprise/restricted environments (no admin rights) | GitHub Release |
| **Portable Linux** | `CodeAtlas-vX.Y.Z-linux-x64.tar.gz` | Linux dev environments & CI containers | GitHub Release |
| **Portable macOS** | `CodeAtlas-vX.Y.Z-osx-arm64.tar.gz` | Apple Silicon / macOS developers | GitHub Release |
| **Plugin Contracts** | `CodeAtlas.Abstractions.X.Y.Z.nupkg` | Community & 3rd-party parser developers | NuGet.org |

---

## 3. GitHub Actions Workflow (`.github/workflows/release.yml`)

```yaml
name: Release & Packaging Pipeline

on:
  push:
    tags:
      - 'v*.*.*'
  workflow_dispatch:
    inputs:
      version:
        description: 'Release Version (e.g. 1.0.0)'
        required: true
        default: '1.0.0'

env:
  DOTNET_VERSION: '8.0.x'

jobs:
  # -------------------------------------------------------------
  # Job 1: Test & Validate
  # -------------------------------------------------------------
  test:
    name: Run Test Suite
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
      - name: Restore Dependencies
        run: dotnet restore CodeAtlas.slnx
      - name: Run Tests
        run: dotnet test CodeAtlas.slnx -c Release --no-restore --verbosity normal

  # -------------------------------------------------------------
  # Job 2: Build & Publish NuGet Abstractions
  # -------------------------------------------------------------
  publish-nuget:
    name: Pack & Push Abstractions NuGet
    needs: test
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
      - name: Pack Abstractions
        run: |
          dotnet pack src/CodeAtlas.Abstractions/CodeAtlas.Abstractions.csproj \
            -c Release \
            -o ./nuget-dist \
            -p:PackageVersion=${{ github.ref_name || inputs.version }}
      - name: Push to NuGet.org
        if: startsWith(github.ref, 'refs/tags/v')
        run: |
          dotnet nuget push ./nuget-dist/*.nupkg \
            --api-key ${{ secrets.NUGET_API_KEY }} \
            --source https://api.nuget.org/v3/index.json \
            --skip-duplicate

  # -------------------------------------------------------------
  # Job 3: Build Portable Binaries (Zip / Tar.gz)
  # -------------------------------------------------------------
  build-portable:
    name: Build Portable Archive (${{ matrix.target }})
    needs: test
    strategy:
      matrix:
        include:
          - os: windows-latest
            target: win-x64
            archive_ext: zip
          - os: ubuntu-latest
            target: linux-x64
            archive_ext: tar.gz
          - os: macos-latest
            target: osx-arm64
            archive_ext: tar.gz
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
      - name: Publish Self-Contained Binary
        run: |
          dotnet publish src/CodeAtlas/CodeAtlas.csproj \
            -c Release \
            -r ${{ matrix.target }} \
            --self-contained true \
            -p:PublishSingleFile=true \
            -p:IncludeNativeLibrariesForSelfExtract=true \
            -o ./publish/${{ matrix.target }}
      - name: Create Archive (Windows)
        if: runner.os == 'Windows'
        run: |
          Compress-Archive -Path ./publish/${{ matrix.target }}/* -DestinationPath ./CodeAtlas-${{ matrix.target }}.zip
      - name: Create Archive (Unix)
        if: runner.os != 'Windows'
        run: |
          tar -czvf ./CodeAtlas-${{ matrix.target }}.tar.gz -C ./publish/${{ matrix.target }} .
      - name: Upload Build Artifact
        uses: actions/upload-artifact@v4
        with:
          name: portable-${{ matrix.target }}
          path: CodeAtlas-${{ matrix.target }}.*

  # -------------------------------------------------------------
  # Job 4: Build Windows MSI Installer
  # -------------------------------------------------------------
  build-msi:
    name: Build Windows MSI Installer
    needs: test
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET & WiX Toolset
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
      - name: Install WiX .NET Global Tool
        run: dotnet tool install --global wix
      - name: Publish Staging Files
        run: |
          dotnet publish src/CodeAtlas/CodeAtlas.csproj \
            -c Release \
            -r win-x64 \
            --self-contained true \
            -o ./msi-staging
      - name: Build MSI with WiX
        run: |
          wix build installer/CodeAtlas.wxs \
            -d SourceDir=./msi-staging \
            -o ./CodeAtlas-win-x64.msi
      - name: Upload MSI Artifact
        uses: actions/upload-artifact@v4
        with:
          name: msi-installer
          path: ./CodeAtlas-win-x64.msi

  # -------------------------------------------------------------
  # Job 5: Create GitHub Release
  # -------------------------------------------------------------
  create-release:
    name: Create GitHub Release
    needs: [build-portable, build-msi, publish-nuget]
    runs-on: ubuntu-latest
    if: startsWith(github.ref, 'refs/tags/v')
    steps:
      - uses: actions/checkout@v4
      - name: Download all built artifacts
        uses: actions/download-artifact@v4
        with:
          path: ./dist
      - name: Generate Checksums
        run: |
          cd ./dist
          find . -type f \( -name "*.zip" -o -name "*.tar.gz" -o -name "*.msi" \) -exec sha256sum {} + > SHA256SUMS.txt
      - name: Publish GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          files: |
            ./dist/**/*.zip
            ./dist/**/*.tar.gz
            ./dist/**/*.msi
            ./dist/SHA256SUMS.txt
          generate_release_notes: true
```

---

## 4. Key Benefits
1. **Zero Admin Rights Needed**: The portable `.zip` / `.tar.gz` can be extracted directly into user directories (`%USERPROFILE%\bin` or `~/.local/bin`) on locked-down enterprise machines.
2. **Automated NuGet Deployment**: Tagging a release (`git tag v1.0.0 && git push --tags`) automatically validates, packs, and uploads the abstractions library to NuGet.org.
3. **Enterprise MSI**: Provides seamless installation with automatic PATH variable configuration for standard setups.
