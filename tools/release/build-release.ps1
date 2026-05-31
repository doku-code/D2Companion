param(
    [string]$Version = "0.1.0-beta",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Configuration = "Release",
    [string]$ArtifactsRoot = ".\artifacts"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Test-PathInside([string]$Candidate, [string]$Container) {
    $candidateFull = (Get-FullPath $Candidate).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    $containerFull = (Get-FullPath $Container).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    return $candidateFull.Equals($containerFull, [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidateFull.StartsWith($containerFull + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-AllowedArtifactsRoot([string]$Candidate, [string]$RepoRoot) {
    if (Test-PathInside $Candidate $RepoRoot) {
        return $true
    }

    $repoParent = Split-Path $RepoRoot -Parent
    $expectedSibling = Get-FullPath (Join-Path $repoParent "D2Companion-public-release")
    $candidateFull = (Get-FullPath $Candidate).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    $expectedFull = $expectedSibling.TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    return $candidateFull.Equals($expectedFull, [System.StringComparison]::OrdinalIgnoreCase)
}

function Remove-DirectoryIfPresent([string]$Path, [string]$Root) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if (-not (Test-PathInside $resolved $Root)) {
        throw "Refusing to remove outside artifacts root: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

function Remove-ReleaseExcludedFiles([string]$PackageRoot) {
    $relativeDeletes = @(
        "appsettings.Development.json",
        "data\companion.sqlite",
        "data\companion.sqlite-wal",
        "data\companion.sqlite-shm",
        "data\catalog.json",
        "data\styx.log",
        "data\debug",
        "styx\bin\config.js",
        "styx\bin\log"
    )

    foreach ($relative in $relativeDeletes) {
        $target = Join-Path $PackageRoot $relative
        if (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target -Recurse -Force
        }
    }

    Get-ChildItem -LiteralPath $PackageRoot -Recurse -Force -File |
        Where-Object {
            $_.Name -match "\.(sqlite|sqlite-wal|sqlite-shm|sqlite3|log|jsonl|pdb|bat)$"
        } |
        Remove-Item -Force
}

function New-D2AssetPack([string]$PackageRoot) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $assetsRoot = Join-Path $PackageRoot "wwwroot\assets"
    if (-not (Test-Path -LiteralPath $assetsRoot -PathType Container)) {
        throw "Cannot create asset pack because wwwroot\assets was not found."
    }

    $packName = "d2companion-assets.d2pack"
    $packPath = Join-Path $assetsRoot $packName

    $runtimeFolders = Get-ChildItem -LiteralPath $assetsRoot -Directory |
        Sort-Object Name
    $allowedExtensions = @(
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico",
        ".woff", ".woff2", ".json"
    )
    $files = foreach ($folder in $runtimeFolders) {
        Get-ChildItem -LiteralPath $folder.FullName -Recurse -Force -File |
            Where-Object { $allowedExtensions -contains $_.Extension.ToLowerInvariant() }
    }

    if (-not $files) {
        if (Test-Path -LiteralPath $packPath -PathType Leaf) {
            return [pscustomobject]@{
                FileCount = 0
                PackPath = $packPath
                RemovedFolders = @()
                Reused = $true
            }
        }

        throw "No runtime asset files were found to pack."
    }

    if (Test-Path -LiteralPath $packPath) {
        Remove-Item -LiteralPath $packPath -Force
    }

    $wwwrootFull = (Resolve-Path -LiteralPath (Join-Path $PackageRoot "wwwroot")).Path.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $archive = [System.IO.Compression.ZipFile]::Open($packPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($wwwrootFull.Length)
            $entryName = $relative.Replace("\", "/")
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive,
                $file.FullName,
                $entryName,
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }

    foreach ($folder in $runtimeFolders) {
        Remove-Item -LiteralPath $folder.FullName -Recurse -Force
    }

    return [pscustomobject]@{
        FileCount = @($files).Count
        PackPath = $packPath
        RemovedFolders = @($runtimeFolders | ForEach-Object { $_.Name })
        Reused = $false
    }
}

function Assert-ReleasePackageClean([string]$PackageRoot) {
    $badRelativePaths = @(
        "tests",
        "publish",
        "obj",
        "bin",
        "data\debug",
        "styx\bin\config.js",
        "styx\bin\log"
    )

    foreach ($relative in $badRelativePaths) {
        $target = Join-Path $PackageRoot $relative
        if (Test-Path -LiteralPath $target) {
            throw "Release package contains forbidden path: $relative"
        }
    }

    $badFiles = Get-ChildItem -LiteralPath $PackageRoot -Recurse -Force -File |
        Where-Object {
            $_.Name -match "\.(sqlite|sqlite-wal|sqlite-shm|sqlite3|log|jsonl|cs|csproj|sln|bat)$"
        }

    if ($badFiles) {
        $sample = ($badFiles | Select-Object -First 10 | ForEach-Object { $_.FullName }) -join [Environment]::NewLine
        throw "Release package contains forbidden file type(s):$([Environment]::NewLine)$sample"
    }

    $assetPack = Join-Path $PackageRoot "wwwroot\assets\d2companion-assets.d2pack"
    if (-not (Test-Path -LiteralPath $assetPack)) {
        throw "Release package is missing the runtime asset pack: wwwroot\assets\d2companion-assets.d2pack"
    }

    foreach ($relative in @(
        "wwwroot\assets\app",
        "wwwroot\assets\backgrounds",
        "wwwroot\assets\d2ui",
        "wwwroot\assets\fonts",
        "wwwroot\assets\gfx",
        "wwwroot\assets\items",
        "wwwroot\assets\mercenary",
        "wwwroot\assets\ui"
    )) {
        if (Test-Path -LiteralPath (Join-Path $PackageRoot $relative)) {
            throw "Release package contains raw packed asset folder: $relative"
        }
    }
}

function Assert-AssetPackContains([string]$PackageRoot) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $packPath = Join-Path $PackageRoot "wwwroot\assets\d2companion-assets.d2pack"
    $requiredEntries = @(
        "assets/items/box.png",
        "assets/gfx/rin/0.png",
        "assets/mercenary/rogue-scout.png",
        "assets/d2ui/muleviewer-stash-inventory.png",
        "assets/d2ui/protate-0.png",
        "assets/fonts/font16.png",
        "assets/backgrounds/kurast-harrogath.jpg"
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($packPath)
    try {
        $entries = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            $entries.Add($entry.FullName.Replace("\", "/")) | Out-Null
        }

        foreach ($entry in $requiredEntries) {
            if (-not $entries.Contains($entry)) {
                throw "Asset pack is missing required runtime asset: $entry"
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-StyxRequiredDependencyNames() {
    return @("js-sha256", "node-persist", "ws")
}

function Test-StyxDependenciesPresent([string]$StyxRoot) {
    $nodeModules = Join-Path $StyxRoot "node_modules"
    foreach ($name in (Get-StyxRequiredDependencyNames)) {
        if (-not (Test-Path -LiteralPath (Join-Path $nodeModules $name) -PathType Container)) {
            return $false
        }
    }

    return $true
}

function Invoke-NpmCommand([string]$WorkingDirectory, [string]$Arguments) {
    $npm = Get-Command npm.cmd -ErrorAction SilentlyContinue
    if ($null -eq $npm) {
        $npm = Get-Command npm -ErrorAction SilentlyContinue
    }
    if ($null -eq $npm) {
        throw "Styx dependencies are missing and npm was not found. Run npm install in styx/ on the packaging machine, or install Node.js/npm before building the release zip."
    }

    Push-Location $WorkingDirectory
    try {
        & $npm.Source $Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "npm $Arguments failed for Styx dependencies with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Ensure-StyxDependencies([string]$StyxRoot) {
    if (Test-StyxDependenciesPresent $StyxRoot) {
        return "already-present"
    }

    Write-Host "      Styx node_modules missing; preparing dependencies with npm ci..."
    if (Test-Path -LiteralPath (Join-Path $StyxRoot "package-lock.json")) {
        Invoke-NpmCommand $StyxRoot "ci"
    } else {
        Invoke-NpmCommand $StyxRoot "install"
    }

    if (-not (Test-StyxDependenciesPresent $StyxRoot)) {
        throw "Styx dependencies are still missing after npm install. Required: $((Get-StyxRequiredDependencyNames) -join ', ')"
    }

    return "prepared"
}

$repoRoot = Get-FullPath (Join-Path $PSScriptRoot "..\..")
$artifactsFull = if ([System.IO.Path]::IsPathRooted($ArtifactsRoot)) {
    Get-FullPath $ArtifactsRoot
} else {
    Get-FullPath (Join-Path $repoRoot $ArtifactsRoot)
}
$stagingRoot = Join-Path $artifactsFull "staging"
$releaseRoot = Join-Path $artifactsFull "release"
$publishDir = Join-Path $stagingRoot "publish"
$packageName = "D2Companion-$Version-$RuntimeIdentifier"
$packageRoot = Join-Path $stagingRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName.zip"
$publishIntermediateRoot = Join-Path ([System.IO.Path]::GetTempPath()) "D2Companion\build-release\$packageName-$PID"
$bundledNodeSourceCandidates = @(
    (Join-Path $repoRoot "tools\runtime\node"),
    (Join-Path $repoRoot "runtimes\node")
)
$bundledNodeSource = $bundledNodeSourceCandidates | Where-Object {
    Test-Path -LiteralPath (Join-Path $_ "node.exe") -PathType Leaf
} | Select-Object -First 1
$bundledNodeDestination = Join-Path $packageRoot "runtimes\node"

if (-not (Test-AllowedArtifactsRoot $artifactsFull $repoRoot)) {
    throw "ArtifactsRoot must be inside the repository or the sibling D2Companion-public-release folder: $artifactsFull"
}

Write-Host ""
Write-Host "=== D2Companion release package ===" -ForegroundColor Cyan
Write-Host "Version:      $Version"
Write-Host "Runtime:      $RuntimeIdentifier"
Write-Host "Artifacts:    $artifactsFull"
Write-Host ""

New-Item -ItemType Directory -Path $artifactsFull -Force | Out-Null
Remove-DirectoryIfPresent $stagingRoot $artifactsFull
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $publishIntermediateRoot -Force | Out-Null
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Write-Host "[1/6] Publishing self-contained Windows app..."
dotnet publish (Join-Path $repoRoot "D2CompanionMvc.csproj") `
    /p:PublishProfile=win-x64 `
    /p:PublishDir="$publishDir\" `
    /p:IntermediateOutputPath="$publishIntermediateRoot\" `
    /p:Configuration=$Configuration `
    /p:RuntimeIdentifier=$RuntimeIdentifier `
    /p:DebugType=None `
    /p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Write-Host "[2/6] Preparing package folder..."
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageRoot -Recurse -Force

$sourceExe = Join-Path $packageRoot "D2CompanionMvc.exe"
$friendlyExe = Join-Path $packageRoot "D2Companion.exe"
if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "Expected published executable was not found: $sourceExe"
}
Move-Item -LiteralPath $sourceExe -Destination $friendlyExe -Force

Write-Host "[3/6] Copying Styx runtime files..."
$styxSource = Join-Path $repoRoot "styx"
$styxDestination = Join-Path $packageRoot "styx"
$styxDependencyState = Ensure-StyxDependencies $styxSource
Remove-DirectoryIfPresent $styxDestination $packageRoot
robocopy $styxSource $styxDestination /E /XD log /XF config.js *.log *.jsonl /NP /NJH /NJS | Out-Null
if ($LASTEXITCODE -gt 7) { throw "Failed to copy Styx runtime files. Robocopy exit code: $LASTEXITCODE" }
if (-not (Test-StyxDependenciesPresent $styxDestination)) {
    throw "Release package is missing Styx runtime dependencies under styx\node_modules."
}
if (-not (Test-Path -LiteralPath (Join-Path $styxDestination "bin\config.example.js"))) {
    throw "Release package is missing styx\bin\config.example.js."
}

if (-not [string]::IsNullOrWhiteSpace($bundledNodeSource)) {
    $bundledNodeExe = Join-Path $bundledNodeSource "node.exe"
    if (-not (Test-Path -LiteralPath $bundledNodeExe)) {
        throw "Optional bundled Node folder exists but node.exe was not found: $bundledNodeExe"
    }

    Write-Host "      Including bundled portable Node runtime from $bundledNodeSource."
    New-Item -ItemType Directory -Path (Split-Path $bundledNodeDestination -Parent) -Force | Out-Null
    robocopy $bundledNodeSource $bundledNodeDestination /E /XD .cache /XF *.log *.jsonl /NP /NJH /NJS | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "Failed to copy bundled Node runtime. Robocopy exit code: $LASTEXITCODE" }
} else {
    Write-Host "      Bundled Node runtime not found at tools\runtime\node or runtimes\node; live capture will use system Node.js 18+."
}

Write-Host "[4/6] Packing runtime assets..."
$assetPackSummary = New-D2AssetPack $packageRoot
if ($assetPackSummary.Reused) {
    Write-Host "      Using existing wwwroot\assets\d2companion-assets.d2pack."
} else {
    Write-Host "      Packed $($assetPackSummary.FileCount) file(s) into wwwroot\assets\d2companion-assets.d2pack."
    Write-Host "      Removed raw asset folders: $($assetPackSummary.RemovedFolders -join ', ')"
}
Write-Host "      Loose assets kept: wwwroot\favicon.ico"

Write-Host "[5/6] Removing excluded/generated files and adding release README..."
Remove-ReleaseExcludedFiles $packageRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\RELEASE_PACKAGE.md") -Destination (Join-Path $packageRoot "README.md") -Force
Assert-ReleasePackageClean $packageRoot
Assert-AssetPackContains $packageRoot

if (-not (Test-Path -LiteralPath $friendlyExe)) {
    throw "Release executable was not found: $friendlyExe"
}
if (-not (Test-Path -LiteralPath (Join-Path $packageRoot "wwwroot"))) {
    throw "Release package is missing wwwroot assets."
}
if (-not (Test-Path -LiteralPath (Join-Path $packageRoot "wwwroot\favicon.ico"))) {
    throw "Release package is missing the app icon."
}
if (-not (Test-Path -LiteralPath (Join-Path $packageRoot "styx\index.js"))) {
    throw "Release package is missing Styx runtime files."
}
if (-not (Select-String -LiteralPath (Join-Path $packageRoot "styx\bin\config.example.js") -Pattern "127.0.0.1:5178/api/ingest/styx/snapshot" -Quiet)) {
    throw "Release Styx config template does not point at the local D2Companion ingest endpoint."
}
$bundledNodePackaged = Test-Path -LiteralPath (Join-Path $packageRoot "runtimes\node\node.exe")
$styxDependenciesPackaged = Test-StyxDependenciesPresent $styxDestination

Write-Host "[6/6] Creating zip..."
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -Force

$zipItem = Get-Item -LiteralPath $zipPath
$exeItem = Get-Item -LiteralPath $friendlyExe
$zipSizeMb = [math]::Round($zipItem.Length / 1MB, 2)
$exeSizeMb = [math]::Round($exeItem.Length / 1MB, 2)

Write-Host ""
Write-Host "Release package created:" -ForegroundColor Green
Write-Host "  Zip: $zipPath ($zipSizeMb MB)"
Write-Host "  Exe: $friendlyExe ($exeSizeMb MB)"
if ($bundledNodePackaged) {
    Write-Host "  Node: bundled at runtimes\node\node.exe"
} else {
    Write-Host "  Node: not bundled; system Node.js 18+ is required for live Styx capture"
}
if ($styxDependenciesPackaged) {
    Write-Host "  Styx deps: included under styx\node_modules ($styxDependencyState); runtime npm required: no"
} else {
    Write-Host "  Styx deps: missing; runtime npm required: yes"
}
Write-Host ""
Write-Host "Review the zip manually before uploading it anywhere."
