param(
    [switch]$Full,
    [string]$UnityPath = $env:UNITY_EDITOR_PATH
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($UnityPath)) {
    $version = (Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt') |
        Select-String '^m_EditorVersion: (.+)$').Matches.Groups[1].Value
    $UnityPath = @("D:\Unity\$version\Editor\Unity.exe",
        "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe") |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($UnityPath) -or -not (Test-Path -LiteralPath $UnityPath)) {
    throw "Unity Editor was not found at $UnityPath. Pass -UnityPath with the installed Unity.exe path."
}

$logPath = Join-Path $projectRoot 'Artifacts\career-balance-unity.log'
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'Artifacts') -Force | Out-Null
$arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', $projectRoot,
    '-executeMethod', 'HackThePlanet.Editor.BalanceSuite.RunBatch', '-logFile', $logPath)
if ($Full) { $arguments += '-balanceFull' }

$quotedArguments = $arguments | ForEach-Object { '"' + $_ + '"' }
$process = Start-Process -FilePath $UnityPath -ArgumentList $quotedArguments -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Tail 60 }
    throw "Balance suite failed with Unity exit code $($process.ExitCode)."
}

Get-Content -LiteralPath (Join-Path $projectRoot 'Artifacts\vesper-validation.txt')
