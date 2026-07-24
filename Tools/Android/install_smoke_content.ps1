param(
    [string]$UnityVersion = "6000.0.73f1",
    [string]$ApkPath = "Builds/Android/UniversalGachaSimulator-smoke.apk",
    [string]$PackageId = "com.personal.universalgacha"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$adb = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
$resolvedApk = (Resolve-Path (Join-Path $repoRoot $ApkPath)).Path
$contentRoot = (Resolve-Path (Join-Path $repoRoot "LocalContent\Imports")).Path

if (-not (Test-Path -LiteralPath $adb)) {
    throw "ADB was not found for Unity $UnityVersion."
}

$devices = & $adb devices
$connected = @($devices | Select-Object -Skip 1 | Where-Object { $_ -match "\sdevice$" })
if ($connected.Count -ne 1) {
    throw "Connect exactly one authorized Android device before running this smoke installer."
}

& $adb install -r $resolvedApk
if ($LASTEXITCODE -ne 0) { throw "APK installation failed." }

$remoteContent = "/sdcard/Android/data/$PackageId/files/Content"
& $adb shell mkdir -p $remoteContent
if ($LASTEXITCODE -ne 0) { throw "Could not create the app content directory." }

& $adb push (Join-Path $contentRoot ".") $remoteContent
if ($LASTEXITCODE -ne 0) { throw "Private content transfer failed." }

& $adb shell am force-stop $PackageId
& $adb shell monkey -p $PackageId -c android.intent.category.LAUNCHER 1
if ($LASTEXITCODE -ne 0) { throw "The smoke app could not be launched." }

Write-Output "Installed the APK, copied private content, and launched $PackageId."
