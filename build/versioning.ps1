<# 
Generates a 4-part Windows-friendly version:
  - If on a tag vX.Y.Z => X.Y.Z.<build>
  - Else base from latest tag or 0.1.0 + commit count => A.B.C.<build>
Writes the version to STDOUT so the pipeline can capture it.
#>

$ErrorActionPreference = 'Stop'

function Get-BaseVersion {
    try {
        $tag = (git describe --tags --abbrev=0 2>$null)
        if ($tag -and $tag -match '^v?(\d+)\.(\d+)\.(\d+)$') {
            return [version]::Parse("$($Matches[1]).$($Matches[2]).$($Matches[3])")
        }
    } catch { }
    return [version]::Parse("0.1.0")
}

function Get-BuildRev {
    # YYDDDHHmm -> fits into UInt16 range? Not strictly, but fine for Windows four-part.
    $now = Get-Date
    $julian = "{0:D3}" -f [int]([datetime]::ParseExact(($now.ToString('MM/dd')), 'MM/dd', $null).DayOfYear)
    return "{0}{1}{2}" -f $now.ToString('yy'), $julian, $now.ToString('HHmm')
}

# Tagged?
$ref = $env:GITHUB_REF
$buildRev = Get-BuildRev

if ($ref -and $ref -match 'refs/tags/v(\d+)\.(\d+)\.(\d+)$') {
    $ver = "{0}.{1}.{2}.{3}" -f $Matches[1], $Matches[2], $Matches[3], $buildRev
    Write-Output $ver
    exit 0
}

# Untagged branch: derive patch by commit count since last tag
$base = Get-BaseVersion
$commits = 0
try {
    $commits = (git rev-list --count HEAD) 2>$null
} catch { $commits = 0 }

$patch = [int]$base.Build
if ($commits -gt 0) { $patch = $patch + [int]$commits }

$ver = "{0}.{1}.{2}.{3}" -f $base.Major, $base.Minor, $patch, $buildRev
Write-Output $ver
