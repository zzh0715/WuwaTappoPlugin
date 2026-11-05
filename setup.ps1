# 一键安装：把"游戏启动 + 插件启动"绑定为一个桌面快捷方式
# 用法：右键此文件 →「使用 PowerShell 运行」（或在 PowerShell 中执行 .\setup.ps1）
$ErrorActionPreference = 'Stop'

# 推导路径：本脚本所在目录 = 插件目录，其父目录 = 游戏安装目录
$pluginDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$gameDir = Split-Path -Parent $pluginDir
$gameExe = Join-Path $gameDir 'WuwaTappo.exe'
$pluginExe = Join-Path $pluginDir 'WuwaTappoPlugin.exe'
$vbs = Join-Path $pluginDir 'launch.vbs'

if (-not (Test-Path $gameExe)) {
    Write-Warning "未找到游戏：$gameExe"
    Write-Warning "请把插件目录放在游戏安装目录下（与 WuwaTappo.exe 同级的子目录），再运行本脚本。"
}
if (-not (Test-Path $pluginExe)) {
    Write-Warning "未找到插件：$pluginExe（请先运行 build.ps1 编译）"
}

# launch.vbs 不存在时自动生成（先启动游戏，3 秒后启动插件）
if (-not (Test-Path $vbs)) {
    $content = @'
' 一键启动：先启动游戏，再启动插件
Set fso = CreateObject("Scripting.FileSystemObject")
Set ws = CreateObject("WScript.Shell")
pluginDir = fso.GetParentFolderName(WScript.ScriptFullName)
gameDir = fso.GetParentFolderName(pluginDir)
ws.Run """" & gameDir & "\WuwaTappo.exe""", 1, False
WScript.Sleep 3000
ws.Run """" & pluginDir & "\WuwaTappoPlugin.exe""", 1, False
'@
    [System.IO.File]::WriteAllText($vbs, $content, [System.Text.Encoding]::Unicode)
    Write-Output "已生成启动脚本：$vbs"
}

# 在桌面创建快捷方式（指向 launch.vbs，图标使用游戏图标）
$desktop = [Environment]::GetFolderPath('Desktop')
$lnk = Join-Path $desktop 'WuwaTappo 启动.lnk'
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnk)
$sc.TargetPath = 'wscript.exe'
$sc.Arguments = '"' + $vbs + '"'
$sc.WorkingDirectory = $gameDir
if (Test-Path $gameExe) { $sc.IconLocation = $gameExe + ',0' }
$sc.Description = '一键启动 WuwaTappo 游戏 + 助手插件'
$sc.Save()

Write-Output ""
Write-Output "安装完成！桌面快捷方式：$lnk"
Write-Output "双击即可先启动游戏、3 秒后自动启动插件。"
