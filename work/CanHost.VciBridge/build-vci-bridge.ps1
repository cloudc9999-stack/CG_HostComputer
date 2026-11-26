$ErrorActionPreference = 'Stop'

$cscPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $cscPath)) {
    throw "未找到 Windows .NET Framework 编译器：$cscPath"
}

$sourcePath = Join-Path $PSScriptRoot 'CanHost.VciBridge.cs'
$outputPath = Join-Path $PSScriptRoot 'CanHost.VciBridge.exe'

& $cscPath /nologo /target:exe /platform:x86 /optimize+ /codepage:65001 "/out:$outputPath" $sourcePath
if ($LASTEXITCODE -ne 0) {
    throw "32 位 ControlCAN 桥接器编译失败，退出码：$LASTEXITCODE"
}

Write-Host "已生成：$outputPath"
