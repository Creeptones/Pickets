[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Compiler)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$testRoot = Join-Path $repoRoot ('.build-temp\uninstall-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
& $Compiler "/O$testRoot" (Join-Path $PSScriptRoot 'RecoveryFixture.iss')
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
$installDir = Join-Path $testRoot 'installed'
$setup = Start-Process (Join-Path $testRoot 'RecoveryFixture.exe') -WindowStyle Hidden -Wait -PassThru `
    -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',("/DIR=`"$installDir`"")
if ($setup.ExitCode -ne 0) { throw "Fixture installation failed: $($setup.ExitCode)" }
$uninstaller = Join-Path $installDir 'unins000.exe'
$payload = Join-Path $installDir 'recovery.cmd'
$resultFile = Join-Path $installDir 'recovery-exit.txt'

function Invoke-TestUninstall {
    param([string]$LogName)
    $process = Start-Process $uninstaller -WindowStyle Hidden -Wait -PassThru `
        -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',("/LOG=`"$(Join-Path $testRoot $LogName)`"")
    return $process.ExitCode
}

try {
    foreach ($failureCode in 1,2,3,4) {
        [IO.File]::WriteAllText($resultFile, [string]$failureCode)
        $code = Invoke-TestUninstall "failure-$failureCode.log"
        if ($code -eq 0 -or !(Test-Path -LiteralPath $payload) -or !(Test-Path -LiteralPath $uninstaller)) {
            throw "Uninstall did not preserve the app after recovery failure $failureCode (exit $code)."
        }
    }
    [IO.File]::WriteAllText($resultFile, '0')
    $code = Invoke-TestUninstall 'success.log'
    if ($code -ne 0 -or (Test-Path -LiteralPath $payload)) {
        throw "Uninstall did not complete after successful recovery (exit $code)."
    }
    Write-Output 'PASS: all four recovery failures block removal; successful recovery allows uninstall.'
}
finally {
    # Remove only the isolated fixture through its own uninstaller if an assertion failed.
    if (Test-Path -LiteralPath $uninstaller) {
        [IO.File]::WriteAllText($resultFile, '0')
        $null = Invoke-TestUninstall 'cleanup.log'
    }
}
