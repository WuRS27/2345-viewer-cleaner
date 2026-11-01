$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler was not found.' }
$release = Join-Path $root 'release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
$source = @('Core.cs','Discovery.cs','Transactions.cs','Engine.cs','Program.cs') | ForEach-Object { Join-Path $root "src\$_" }
$references = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.ServiceProcess.dll','/r:System.Runtime.Serialization.dll')
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /debug- /utf8output /codepage:65001 /warn:4 "/win32manifest:$(Join-Path $root 'src\app.manifest')" "/out:$(Join-Path $release 'ViewerExtrasCleaner.exe')" @references @source
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output (Join-Path $release 'ViewerExtrasCleaner.exe')
