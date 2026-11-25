<#
.SYNOPSIS
  PocketLegend Mod Control By Clann - 배포용 zip 생성 스크립트

.DESCRIPTION
  1) 모든 모드를 Release 로 빌드
  2) 공식 BepInEx 5 (x64) 를 내려받아 캐시 (최초 1회)
  3) 모드마다 "압축 풀면 바로 실행되는" zip 생성 + 전체 모드 묶음 zip 생성
     zip 구조 (게임 폴더에 그대로 풀면 됨):
       winhttp.dll, doorstop_config.ini, .doorstop_version   ← BepInEx 로더
       BepInEx\core\*                                         ← BepInEx 본체
       BepInEx\config\BepInEx.cfg                             ← 이 모드들에 맞춘 BepInEx 설정
       BepInEx\plugins\PocketLegend.ModSwitch.dll             ← 모드 ON/OFF 메뉴 (모든 zip 공통)
       BepInEx\plugins\PocketLegend.<모드>.dll
       PocketLegendMods\README_<모드>.txt, LICENSE_BepInEx.txt

.EXAMPLE
  PS> cd "D:\SteamLibrary\steamapps\common\Pocket Legend\ModSource"
  PS> .\tools\Package.ps1
  결과: ModSource\dist\*.zip
#>
param(
    [string]$BepInExVersion = "5.4.23.6",
    [string]$GameBuild = "26703903",       # Steam 빌드 번호 (steamapps\appmanifest_3129790.acf 의 buildid)
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$Root     = Split-Path -Parent $PSScriptRoot          # ModSource
$GameDir  = Split-Path -Parent $Root                  # Pocket Legend
$Dist     = Join-Path $Root "dist"
$Cache    = Join-Path $Root ".cache"
$Utf8Bom  = New-Object System.Text.UTF8Encoding($true)

# ── 모드 목록 (폴더명 → 설명). 새 모드를 만들면 여기에 한 줄 추가 ──────────────────
$Mods = [ordered]@{
    "ItemSort"           = @{ Desc = "아이템 목록(아이템 메뉴, 선택 창, 전투 더미)에 정렬 버튼 추가: 획득순 / 유형별 / 이름순 / 강화순"; Balance = $false }
    "ItemCount"          = @{ Desc = "오른쪽 위 아이템 아이콘 아래에 유형별 보유 수(무기/방어구/스킬/귀속) 2x2 표시"; Balance = $false }
    "ShowCardShortcut"   = @{ Desc = "전투 손패 위에 숫자 단축키(1~0) 표시, 턴 종료 버튼 위에 손패 수 / 10 표시"; Balance = $false }
    "AlwaysShowItemInfo" = @{ Desc = "아이템 선택 창, 이벤트 선택지의 아이템 툴팁 등에서 우클릭으로 상세보기"; Balance = $false }
    "PeekHandInfo"       = @{ Desc = "전투 중 '꺼낼 아이템 선택' 창을 접어둔 상태에서 손패에 마우스를 올리면 정보 표시 (사용 불가, 보기 전용)"; Balance = $false }
    "EnhanceCost"        = @{ Desc = "대장간 강화 비용을 등급과 관계없이 강화석 1개로 고정 (설정에서 개수 변경 가능)"; Balance = $true }
    "BetterCardSkip"     = @{ Desc = "[시험 버전] 전투 승리 보상의 '카드 획득', '인벤토리 조각'을 넘기면 골드 2 지급 (설정에서 변경 가능)"; Balance = $true }
}
$Common = "ModSwitch"   # 모든 zip 에 함께 들어가는 공통 모드

function Get-ModInfo([string]$folder) {
    $src = Get-Content (Join-Path $Root "$folder\Plugin.cs") -Raw -Encoding UTF8
    $m = [regex]::Match($src, 'BepInPlugin\(Guid,\s*"([^"]+)",\s*"([^"]+)"\)')
    if (-not $m.Success) { throw "$folder\Plugin.cs 에서 BepInPlugin 이름/버전을 찾지 못함" }
    [pscustomobject]@{ Folder = $folder; Name = $m.Groups[1].Value; Version = $m.Groups[2].Value;
                       Dll = "PocketLegend.$folder.dll" }
}

# ── 1) 빌드 ─────────────────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    foreach ($folder in @($Common) + $Mods.Keys) {
        Write-Host "빌드: $folder" -ForegroundColor Cyan
        dotnet build (Join-Path $Root "$folder\$folder.csproj") -c Release --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "$folder 빌드 실패" }
    }
}

# ── 2) 공식 BepInEx 캐시 ─────────────────────────────────────────────────────────
$bepZip     = Join-Path $Cache "BepInEx_win_x64_$BepInExVersion.zip"
$bepDir     = Join-Path $Cache "BepInEx_win_x64_$BepInExVersion"
$bepLicense = Join-Path $Cache "BepInEx_LICENSE_$BepInExVersion.txt"
New-Item -ItemType Directory -Force $Cache | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
if (-not (Test-Path $bepZip)) {
    Write-Host "BepInEx $BepInExVersion 다운로드" -ForegroundColor Cyan
    Invoke-WebRequest "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip" -OutFile $bepZip -UseBasicParsing
}
if (-not (Test-Path $bepLicense)) {   # BepInEx 5 = MIT
    Invoke-WebRequest "https://raw.githubusercontent.com/BepInEx/BepInEx/v$BepInExVersion/LICENSE" -OutFile $bepLicense -UseBasicParsing
}
$doorstopLicense = Join-Path $Cache "UnityDoorstop_LICENSE.txt"   # winhttp.dll (UnityDoorstop) = LGPL-2.1
if (-not (Test-Path $doorstopLicense)) {
    Invoke-WebRequest "https://raw.githubusercontent.com/NeighTools/UnityDoorstop/master/LICENSE" -OutFile $doorstopLicense -UseBasicParsing
}
if (Test-Path $bepDir) { Remove-Item -Recurse -Force $bepDir }
[IO.Compression.ZipFile]::ExtractToDirectory($bepZip, $bepDir)

# ── 3) 패키지 만들기 ─────────────────────────────────────────────────────────────
function New-BaseLayout([string]$stage) {
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item -Recurse -Force (Join-Path $bepDir "*") $stage
    Remove-Item -Force (Join-Path $stage "changelog.txt") -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force (Join-Path $stage "BepInEx\plugins"), (Join-Path $stage "BepInEx\config"),
                                         (Join-Path $stage "PocketLegendMods") | Out-Null
    # 이 모드들은 플러그인 Update() 로 화면을 찾아 붙이므로 매니저 오브젝트를 보호, 콘솔은 일반 사용자용으로 끔
    [IO.File]::WriteAllText((Join-Path $stage "BepInEx\config\BepInEx.cfg"), @"
[Chainloader]

## If enabled, hides BepInEx Manager GameObject from Unity.
## This can fix loading issues in some games that attempt to prevent BepInEx from being loaded.
## Use this only if you know what this option means, as it can affect functionality of some older plugins.
# Setting type: Boolean
# Default value: false
HideManagerGameObject = true

[Logging.Console]

## Enables showing a console for log output.
# Setting type: Boolean
# Default value: false
Enabled = false
"@, $Utf8Bom)
    Copy-Item $bepLicense (Join-Path $stage "PocketLegendMods\LICENSE_BepInEx.txt")
    Copy-Item $doorstopLicense (Join-Path $stage "PocketLegendMods\LICENSE_UnityDoorstop.txt")
}

function Add-Mod([string]$stage, $info) {
    Copy-Item (Join-Path $GameDir "BepInEx\plugins\$($info.Dll)") (Join-Path $stage "BepInEx\plugins\")
}

function Get-ReadmeText($infos, [string]$title) {
    $list = ($infos | ForEach-Object {
        $d = $Mods[$_.Folder]
        $warn = if ($d.Balance) { "  ※ 게임 밸런스가 바뀌는 모드입니다." } else { "" }
        "- $($_.Name) v$($_.Version)`r`n    $($d.Desc)$warn"
    }) -join "`r`n"
    $switch = Get-ModInfo $Common
@"
$title
PocketLegend Mod Control By Clann (비공식 팬 모드)
======================================================

[포함된 모드]
$list
- $($switch.Name) v$($switch.Version) (공통)
    타이틀 화면 '모드 관리' 메뉴에서 각 모드를 켜고 끕니다.

[테스트 환경]
- Pocket Legend (Steam) 빌드 $GameBuild
- BepInEx $BepInExVersion (Windows x64)
- 게임이 업데이트되면 모드가 동작하지 않을 수 있습니다.

[설치]
1. Steam 라이브러리 > Pocket Legend 우클릭 > 관리 > 로컬 파일 보기
2. 열린 폴더(PocketLegend.exe 가 있는 곳)에 이 zip 의 내용물을 그대로 풀기
   (덮어쓰기를 물으면 '예' - 다른 모드 zip 과 같은 공통 파일입니다)
3. 게임 실행 → 타이틀 화면 '모드 관리'에서 ON/OFF 확인

[제거]
- 모드 하나만: BepInEx\plugins 폴더에서 해당 PocketLegend.*.dll 삭제
- 전부: winhttp.dll 삭제 (BepInEx 폴더까지 지우면 완전 제거)

[설정 / 문제 보고]
- 모드별 설정: BepInEx\config\pocketlegend.*.cfg (게임 1회 실행 후 생성)
- 문제가 생기면 BepInEx\LogOutput.log 파일을 함께 보내 주세요.
- 이 모드는 개발사와 무관한 비공식 모드입니다. 모드 사용 중 생긴 문제는 개발사가 아닌 모드 제작자에게 알려 주세요.

[라이선스 / 포함된 외부 구성요소 (수정 없이 공식 배포본 그대로 포함)]
- BepInEx $BepInExVersion : MIT (PocketLegendMods\LICENSE_BepInEx.txt)
    https://github.com/BepInEx/BepInEx
- UnityDoorstop (winhttp.dll) : LGPL-2.1 (PocketLegendMods\LICENSE_UnityDoorstop.txt)
    https://github.com/NeighTools/UnityDoorstop
- HarmonyX, MonoMod, Mono.Cecil (BepInEx\core) : MIT
- 게임 파일은 포함되어 있지 않습니다.
"@
}

function New-Zip([string]$stage, [string]$zipPath) {
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    # ZipFile.CreateFromDirectory 는 .NET Framework 에서 경로를 '\' 로 저장해 일부 압축 프로그램에서 폴더가 깨지므로
    # 항목을 직접 추가하면서 zip 표준 구분자 '/' 로 저장
    $base = (Resolve-Path $stage).Path.TrimEnd('\') + '\'
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem -Recurse -File -Force $stage | ForEach-Object {
            $entry = $_.FullName.Substring($base.Length).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $zip.Dispose() }
    $kb = [math]::Round((Get-Item $zipPath).Length / 1KB)
    Write-Host ("  → {0} ({1} KB)" -f (Split-Path -Leaf $zipPath), $kb) -ForegroundColor Green
}

if (Test-Path $Dist) { Remove-Item -Recurse -Force $Dist }
New-Item -ItemType Directory -Force $Dist | Out-Null
$stageRoot = Join-Path $Cache "stage"
$switchInfo = Get-ModInfo $Common
$allInfos = @()

# 모드별 zip
foreach ($folder in $Mods.Keys) {
    $info = Get-ModInfo $folder
    $allInfos += $info
    $stage = Join-Path $stageRoot $folder
    New-BaseLayout $stage
    Add-Mod $stage $switchInfo
    Add-Mod $stage $info
    [IO.File]::WriteAllText((Join-Path $stage "PocketLegendMods\README_$folder.txt"),
        (Get-ReadmeText @($info) "$($info.Name) v$($info.Version)"), $Utf8Bom)
    $safe = ($info.Name -replace '[^A-Za-z0-9]+', '')
    New-Zip $stage (Join-Path $Dist "PocketLegend-$safe-v$($info.Version).zip")
}

# 전체 묶음 zip
$date = Get-Date -Format "yyyy.MM.dd"
$stage = Join-Path $stageRoot "_All"
New-BaseLayout $stage
Add-Mod $stage $switchInfo
foreach ($info in $allInfos) { Add-Mod $stage $info }
[IO.File]::WriteAllText((Join-Path $stage "PocketLegendMods\README_AllMods.txt"),
    (Get-ReadmeText $allInfos "전체 모드 묶음 ($date)"), $Utf8Bom)
New-Zip $stage (Join-Path $Dist "PocketLegend-AllMods-$date.zip")

Remove-Item -Recurse -Force $stageRoot
Write-Host "`n완료: $Dist" -ForegroundColor Green
