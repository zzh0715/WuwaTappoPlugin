# 编译 WuwaTappo 助手（使用 Windows 自带的 .NET Framework 4.8 编译器 csc）
# 用法：右键"使用 PowerShell 运行"，或命令行 .\build.ps1
$ErrorActionPreference = 'Stop'

$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
if (-not (Test-Path $csc)) { throw "找不到 $csc（需要 .NET Framework 4.8）" }

$gac = 'C:\Windows\Microsoft.Net\assembly\GAC_MSIL'
$uia = Join-Path $gac 'UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll'
$uitypes = Join-Path $gac 'UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll'
$wb = Join-Path $gac 'WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll'

& $csc /target:winexe /platform:x64 /optimize+ /nologo `
    "/out:WuwaTappoPlugin.exe" `
    "/r:$fw\System.dll" "/r:$fw\System.Windows.Forms.dll" "/r:$fw\System.Drawing.dll" "/r:$fw\System.Core.dll" "/r:$wb" `
    "/r:$uia" "/r:$uitypes" `
    "Program.cs"

if ($LASTEXITCODE -eq 0) {
    Write-Output '编译成功：WuwaTappoPlugin.exe'
} else {
    throw "编译失败（exit $LASTEXITCODE）"
}
