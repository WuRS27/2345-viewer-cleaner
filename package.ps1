$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$release=Join-Path $root 'release'
$source=Join-Path $release 'Source'
New-Item -ItemType Directory -Path (Join-Path $source 'src'),(Join-Path $source 'tests') -Force | Out-Null
$sourceFiles=@('Core.cs','Discovery.cs','Transactions.cs','Engine.cs','Program.cs','app.manifest')
foreach($file in $sourceFiles){Copy-Item -LiteralPath (Join-Path $root "src\$file") -Destination (Join-Path $source "src\$file") -Force}
foreach($file in @('build.ps1','test.ps1')){Copy-Item -LiteralPath (Join-Path $root $file) -Destination (Join-Path $source $file) -Force}
Copy-Item -LiteralPath (Join-Path $root 'tests\Tests.cs') -Destination (Join-Path $source 'tests\Tests.cs') -Force
Copy-Item -LiteralPath (Join-Path $root 'tests\ScanFixture.ps1') -Destination (Join-Path $source 'tests\ScanFixture.ps1') -Force
$readme=Get-Content -LiteralPath (Join-Path $root '使用说明.txt') -Raw -Encoding utf8
[IO.File]::WriteAllText((Join-Path $release '使用说明.txt'),$readme,(New-Object Text.UTF8Encoding $true))
$hash=(Get-FileHash -LiteralPath (Join-Path $release 'ViewerExtrasCleaner.exe') -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $release 'SHA256.txt'),($hash+'  ViewerExtrasCleaner.exe'+[Environment]::NewLine),(New-Object Text.UTF8Encoding $false))
$archive=Join-Path $root '2345看图王附加组件清理工具-v1.0.zip'
Compress-Archive -LiteralPath @((Join-Path $release 'ViewerExtrasCleaner.exe'),(Join-Path $release '使用说明.txt'),(Join-Path $release 'SHA256.txt'),$source) -DestinationPath $archive -Force
[pscustomobject]@{Archive=$archive;Size=(Get-Item -LiteralPath $archive).Length;ExecutableSHA256=$hash} | ConvertTo-Json
