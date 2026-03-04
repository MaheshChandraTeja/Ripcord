param(
  [Parameter(Mandatory=$true)] [string] $AppExeDir,
  [Parameter(Mandatory=$true)] [string] $Manifest,
  [Parameter(Mandatory=$true)] [string] $OutDir,
  [Parameter(Mandatory=$true)] [string] $Version,
  [Parameter(Mandatory=$false)] [string] $TimestampUrl = "http://timestamp.digicert.com",
  # One of the following may be provided for signing:
  [string] $CertThumbprint,
  [string] $PfxFile,
  [string] $PfxPassword
)

$ErrorActionPreference = 'Stop'

function Resolve-Tool($name) {
  $kitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
  if (Test-Path $kitsRoot) {
    $cands = Get-ChildItem -Path $kitsRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending
    foreach ($d in $cands) {
      $p = Join-Path $d.FullName "x64\$name.exe"
      if (Test-Path $p) { return $p }
    }
  }
  $p2 = Get-Command $name -ErrorAction SilentlyContinue
  if ($p2) { return $p2.Source }
  throw "Cannot find $name.exe (Windows SDK) on this agent."
}

$makeappx = Resolve-Tool "makeappx"
$signtool = Resolve-Tool "signtool"

# Validate inputs
if (!(Test-Path $AppExeDir)) { throw "AppExeDir not found: $AppExeDir" }
if (!(Test-Path $Manifest))  { throw "Manifest not found: $Manifest" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Stage folder for packing
$stage = Join-Path $env:RUNNER_TEMP "msix_stage_$(Get-Random)"
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# Copy app binaries + manifest
Copy-Item -Path (Join-Path $AppExeDir "*") -Destination $stage -Recurse -Force
Copy-Item -Path $Manifest -Destination (Join-Path $stage "AppxManifest.xml") -Force

# Bump version inside manifest (Identity Version="A.B.C.D")
[xml]$xml = Get-Content (Join-Path $stage "AppxManifest.xml")
$identity = $xml.Package.Identity
if (-not $identity) { throw "Manifest does not contain <Identity>." }
$identity.Version = $Version
$xml.Save((Join-Path $stage "AppxManifest.xml"))

# Create package
$pkgPath = Join-Path $OutDir ("Ripcord_{0}.msix" -f $Version)
& $makeappx pack /o /v /d $stage /p $pkgPath | Write-Host

# Optional signing
$doSign = $false
if ($PfxFile -and (Test-Path $PfxFile)) { $doSign = $true }
elseif ($CertThumbprint) { $doSign = $true }

if ($doSign) {
  $args = @("sign", "/fd", "SHA256", "/v")
  if ($TimestampUrl) { $args += @("/tr", $TimestampUrl, "/td", "SHA256") }

  if ($PfxFile -and (Test-Path $PfxFile)) {
    $args += @("/f", $PfxFile)
    if ($PfxPassword) { $args += @("/p", $PfxPassword) }
  } elseif ($CertThumbprint) {
    # Prefer CurrentUser\My, then LocalMachine\My
    $args += @("/sha1", $CertThumbprint, "/sm")
  }

  $args += $pkgPath
  & $signtool @args | Write-Host
} else {
  Write-Warning "MSIX not signed (no certificate provided). Provide SIGNING_CERT_THUMBPRINT or SIGNING_PFX + SIGNING_PFX_PASSWORD in CI secrets."
}

Write-Host "MSIX package created at: $pkgPath"
Write-Output $pkgPath
