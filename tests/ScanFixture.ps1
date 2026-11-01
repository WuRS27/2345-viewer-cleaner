param(
    [Parameter(Mandatory=$true)][string]$Viewer,
    [Parameter(Mandatory=$true)][string]$Worker,
    [Parameter(Mandatory=$true)][string]$Updater,
    [Parameter(Mandatory=$true)][string]$Service
)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$fixture=Join-Path $parent ('2345CleanerScanFixture-'+[guid]::NewGuid().ToString('N'))
try {
    $install=Join-Path $fixture '2345Pic'
    New-Item -ItemType Directory -Path (Join-Path $install 'Protect') -Force | Out-Null
    Copy-Item -LiteralPath $Viewer -Destination (Join-Path $install '2345PicViewer.exe')
    Copy-Item -LiteralPath $Worker -Destination (Join-Path $install '2345PicWorker.exe')
    Copy-Item -LiteralPath $Updater -Destination (Join-Path $install '2345PicUpdate.exe')
    Copy-Item -LiteralPath $Service -Destination (Join-Path $install 'Protect\PicService.exe')
    [void][Reflection.Assembly]::LoadFrom((Join-Path $project 'release\ViewerExtrasCleaner.exe'))
    $result=[ViewerExtrasCleaner.Discovery]::Get($install)
    $found=@($result.Files | Where-Object { $_.Root -eq $install })
    if($found.Count -ne 3){throw "Expected 3 fixture items, received $($found.Count)"}
    if(@($found | Where-Object { $_.Original -like '*2345PicViewer.exe' }).Count -ne 0){throw 'Main viewer selected for removal'}
    if(-not (Test-Path -LiteralPath (Join-Path $install '2345PicWorker.exe'))){throw 'Read-only scan modified a file'}
    Write-Output 'PASS signed-vendor fixture detection: Protect, Worker, Updater'
    Write-Output 'PASS main viewer remains excluded from cleanup'
    Write-Output 'PASS fixture scan is read-only'
} finally {
    if(Test-Path -LiteralPath $fixture){
        $resolved=(Resolve-Path -LiteralPath $fixture).ProviderPath
        if([IO.Path]::GetDirectoryName($resolved) -ine $parent -or [IO.Path]::GetFileName($resolved) -notlike '2345CleanerScanFixture-*'){throw 'Refusing fixture deletion outside its verified parent'}
        if((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Refusing fixture reparse point'}
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
