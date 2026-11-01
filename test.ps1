$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$output = Join-Path $root 'tests\CleanerTests.exe'
& $compiler /nologo /target:exe /platform:anycpu /optimize+ /utf8output /codepage:65001 /r:System.dll /r:System.Core.dll /r:System.Runtime.Serialization.dll "/out:$output" (Join-Path $root 'src\Core.cs') (Join-Path $root 'src\Transactions.cs') (Join-Path $root 'tests\Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
