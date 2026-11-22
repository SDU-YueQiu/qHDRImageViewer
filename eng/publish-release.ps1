# Builds a signed, sideload-installable Release MSIX plus a portable zip.
#
#   .\eng\publish-release.ps1
#
# Produces under artifacts\release\<version>\ :
#   qHdrImageViewer_<version>_x64.msix   signed installable package
#   qHdrImageViewer-signing.cer          self-signed certificate to trust
#   install.ps1                         imports the certificate and installs the MSIX
#   INSTALL.md                          short user instructions
#   qHdrImageViewer-<version>-win-x64-portable.zip  (unless -SkipPortable)
#
# The signing certificate is generated once and reused from dist\keys\ so the
# publisher identity stays stable across rebuilds. Both dist\ and artifacts\ are
# git-ignored.

param(
    [string]$Configuration = 'Release',
    [ValidateSet('x64')]
    [string]$Platform = 'x64',
    [string]$Version = '',
    [string]$PfxPath = '',
    [switch]$SkipPortable,
    [switch]$SkipNative,
    [switch]$NoClean
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$csproj = Join-Path $repo 'qHdrImageViewer.csproj'
$manifestPath = Join-Path $repo 'Package.appxmanifest'
$keysDir = Join-Path $repo 'dist\keys'
$artifactsRoot = Join-Path $repo 'artifacts\release'

[xml]$project = Get-Content -Raw -Path $csproj
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $project.Project.PropertyGroup.Version | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw 'Version was not provided and could not be read from qHdrImageViewer.csproj.'
}

# MSIX identity version must be 4 numeric parts.
$msixVersion = if ($Version.Split('.').Count -ge 4) { $Version } else { "$Version.0" }
$versionParts = $msixVersion.Split('.')
while ($versionParts.Count -lt 4) { $versionParts += '0' }
$msixVersion = ($versionParts[0..3] -join '.')

[xml]$manifest = Get-Content -Raw -Path $manifestPath
$publisher = $manifest.Package.Identity.Publisher
$packageName = $manifest.Package.Identity.Name

$nativeDll = Join-Path $repo "native\HdrImageViewer.Native\build\$Platform\Release\HdrImageViewer.Native.dll"
if (-not $SkipNative -and -not (Test-Path $nativeDll)) {
    Write-Warning "Native bridge not present ($nativeDll); building the release without it."
    $SkipNative = $true
}

Push-Location $repo
try {
    # ------------------------------------------------------------------ signing
    if ([string]::IsNullOrWhiteSpace($PfxPath)) {
        New-Item -ItemType Directory -Force -Path $keysDir | Out-Null
        $PfxPath = Join-Path $keysDir 'qHdrImageViewer-signing.pfx'
        $cerPath = Join-Path $keysDir 'qHdrImageViewer-signing.cer'
        if (-not (Test-Path $PfxPath)) {
            Write-Host "== Generating self-signed sideload certificate for $publisher =="
            $cert = New-SelfSignedCertificate `
                -Type Custom `
                -Subject $publisher `
                -KeyUsage DigitalSignature `
                -KeySpec Signature `
                -KeyExportPolicy Exportable `
                -FriendlyName 'qHdrImageViewer sideload signing' `
                -CertStoreLocation 'Cert:\CurrentUser\My' `
                -NotAfter (Get-Date).AddYears(10) `
                -TextExtension @(
                    '2.5.29.37={text}1.3.6.1.5.5.7.3.3',
                    '2.5.29.19={text}'
                )
            $password = ConvertTo-SecureString -String 'qHdrImageViewerLocal' -Force -AsPlainText
            Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $password | Out-Null
            Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
            Write-Host "Certificate: $cerPath"
        }
        else {
            if (-not (Test-Path $cerPath)) {
                Write-Warning "PFX exists but no .cer next to it: $cerPath"
            }
            $password = ConvertTo-SecureString -String 'qHdrImageViewerLocal' -Force -AsPlainText
        }
        $pfxPassword = 'qHdrImageViewerLocal'
    }
    else {
        $resolved = (Resolve-Path $PfxPath).Path
        $PfxPath = $resolved
        $pfxPassword = 'qHdrImageViewerLocal'
        $cerPath = [System.IO.Path]::ChangeExtension($resolved, '.cer')
    }

    # ------------------------------------------------------------ MSIX package
    Write-Host ''
    Write-Host "== Publishing MSIX $msixVersion =="
    $publishParams = @{
        Configuration = $Configuration
        Platform      = $Platform
        PfxPath       = $PfxPath
        PfxPassword   = $pfxPassword
    }
    if ($SkipNative) { $publishParams['SkipNative'] = $true }
    if ($NoClean) { $publishParams['NoClean'] = $true }
    & (Join-Path $PSScriptRoot 'publish-msix.ps1') @publishParams
    if ($LASTEXITCODE -ne 0) { throw "publish-msix failed (exit $LASTEXITCODE)" }

    $builtMsix = Get-ChildItem -Path (Join-Path $repo "AppPackages\qHdrImageViewer_*_$($Platform)_Test") -Filter "qHdrImageViewer_*_$($Platform).msix" -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $builtMsix) { throw 'Signed MSIX was not produced under AppPackages.' }

    # --------------------------------------------------------------- artifacts
    $outDir = Join-Path $artifactsRoot $msixVersion
    Remove-Item -LiteralPath $outDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $msixOut = Join-Path $outDir "qHdrImageViewer_$($msixVersion)_$($Platform).msix"
    Copy-Item -LiteralPath $builtMsix.FullName -Destination $msixOut -Force
    $cerOut = $null
    if (Test-Path $cerPath) {
        $cerOut = Join-Path $outDir 'qHdrImageViewer-signing.cer'
        Copy-Item -LiteralPath $cerPath -Destination $cerOut -Force
    }

    $installScript = Join-Path $outDir 'install.ps1'
    @'
# Installs qHdrImageViewer: trusts the bundled sideload certificate (machine
# trusted-people store, so this needs one elevation prompt) and installs the MSIX.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSCommandPath
$cer = Join-Path $here 'qHdrImageViewer-signing.cer'
$msix = Get-ChildItem -Path $here -Filter '*.msix' |
    Where-Object { $_.Name -notmatch 'unsigned' } |
    Select-Object -First 1
if (-not $msix) { throw 'MSIX not found next to install.ps1.' }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host 'Requesting administrator permission to trust the sideload certificate...'
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments
    exit
}

if (Test-Path $cer) {
    Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    Write-Host 'Certificate installed into the machine trusted-people store.'
}

Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown
Write-Host "Installed $($msix.Name). Launch qHdrImageViewer from the Start menu."
'@ | Set-Content -LiteralPath $installScript -Encoding utf8

    if ($cerOut) {
        $installMd = Join-Path $outDir 'INSTALL.md'
        @"
# qHdrImageViewer $msixVersion ($Platform) - sideload install

The MSIX is signed with a self-signed certificate, so the certificate has to be
trusted before Windows will install the package.

## One step (recommended)

Right-click **install.ps1** -> *Run with PowerShell*, or run:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

The script asks for administrator permission once (needed to put the certificate
into the machine trusted-people store), then installs the app. On a machine where
the current account is already administrator with UAC set to elevate without
prompting, the whole step is silent.

## Manual

1. Double-click **qHdrImageViewer-signing.cer** -> *Install Certificate* ->
   *Local Machine* (administrator required) -> *Place all certificates in the
   following store* -> *Trusted People*.
2. Double-click the MSIX and confirm the install.

## Uninstall

Settings -> Apps -> Installed apps -> qHdrImageViewer -> Uninstall, or:

```powershell
Get-AppxPackage LinYuSen.qHdrImageViewer | Remove-AppxPackage
```

Optionally remove the certificate later from *certlm.msc* -> Trusted People.

## Notes

- Requires Windows 11 24H2 (build 26100) or newer and the .NET 10 Desktop
  Runtime + Windows App Runtime 2.5, which the app installer can fetch from
  Microsoft if missing.
- This build includes the pinned codec bundle (encoders\x64\: libjxl,
  libavif, libheif, Ultra HDR and their command-line tools) and the native
  OpenEXR bridge (`HdrImageViewer.Native.dll`), so JPEG XL, OpenEXR, HEIF/AVIF
  and Ultra HDR work without installing MSYS2 or any external tools.
- RTX VSR requires an NVIDIA RTX GPU and the NVIDIA video-effects runtime
  DLLs. The app searches `<app>\vsr`, `%LOCALAPPDATA%\qHdrImageViewer\vsr` and
  common ComfyUI `nvvfx\libs` locations; when they are missing the feature is
  disabled with a hint instead of failing. NVIDIA does not permit redistributing
  these DLLs, so they are not part of this package.
"@ | Set-Content -LiteralPath $installMd -Encoding utf8
    }

    # ---------------------------------------------------------- portable zip
    $zipOut = $null
    if (-not $SkipPortable) {
        Write-Host ''
        Write-Host '== Publishing portable zip =='
        $portableParams = @{
            Configuration = $Configuration
            Platform      = $Platform
            Version       = $Version
            SkipTests     = $true
            SkipNative    = $true
        }
        & (Join-Path $PSScriptRoot 'publish-portable.ps1') @portableParams
        if ($LASTEXITCODE -ne 0) { throw "publish-portable failed (exit $LASTEXITCODE)" }
        $builtZip = Get-ChildItem -Path (Join-Path $repo 'artifacts') -Filter "qHdrImageViewer-$Version-win-$Platform-portable.zip" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($builtZip) {
            $zipOut = Join-Path $outDir $builtZip.Name
            Copy-Item -LiteralPath $builtZip.FullName -Destination $zipOut -Force
        }
        else {
            Write-Warning 'Portable zip was not produced.'
        }
    }

    # ------------------------------------------------- text summary artifact
    $summary = Join-Path $outDir 'BUILD_INFO.txt'
    @(
        "Version:   $msixVersion"
        "Publisher: $publisher"
        "Package:   $packageName"
        "Platform:  $Platform ($Configuration)"
        "MSIX:      $(Split-Path -Leaf $msixOut)"
        "Portable:  $(if ($zipOut) { Split-Path -Leaf $zipOut } else { 'not built' })"
        "Signed:    $(if (Test-Path $cerOut) { 'self-signed (install.ps1 trusts it)' } else { 'signed' })"
        "Built:     $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    ) | Set-Content -LiteralPath $summary -Encoding utf8

    $size = [math]::Round((Get-Item $msixOut).Length / 1MB, 1)
    Write-Host ''
    Write-Host "Release output: $outDir"
    Write-Host "MSIX: $msixOut ($size MB)"
    if ($zipOut) { Write-Host "Portable: $zipOut" }
}
finally {
    Pop-Location
}
