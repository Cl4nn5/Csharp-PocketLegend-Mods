<#
.SYNOPSIS
  PocketLegend Mod Control By Clann - 배포용 zip 생성 스크립트

.DESCRIPTION
  1) 모든 모드를 Release 로 새로 빌드 (-t:Rebuild)
  2) 공식 BepInEx 5 (x64) 를 내려받아 캐시 (최초 1회)
  3) zip 생성 (게임 폴더에 그대로 풀면 됨)
       PocketLegend-1-Base-<날짜>.zip       BepInEx(모드 로더) + 모드 관리(Mod Switch)  ← 먼저 설치
       PocketLegend-2-UX-<날짜>.zip         조작 편의 모드 묶음 (DLL 만, Base 필요)
       PocketLegend-2-UI-<날짜>.zip         정보 표시 모드 묶음 (DLL 만, Base 필요)
       PocketLegend-2-Balance-<날짜>.zip    밸런스 변경 모드 묶음 (DLL 만, Base 필요)
       PocketLegend-AllInOne-<날짜>.zip     전부 (Base + 모든 모드), 이것 하나만 풀어도 됨
     zip 안의 구조:
       winhttp.dll, doorstop_config.ini, .doorstop_version   ← BepInEx 로더 (Base / AllInOne)
       BepInEx\core\*, BepInEx\config\BepInEx.cfg            ← BepInEx 본체와 설정 (Base / AllInOne)
       BepInEx\plugins\PocketLegend.*.dll                    ← 모드
       PocketLegendMods\README_*.txt, LICENSE_*.txt          ← 안내문 / 라이선스
       Readme_mod.md                                         ← 게임 폴더 맨 위 안내문 (AllInOne)

.EXAMPLE
  PS> cd "D:\SteamLibrary\steamapps\common\Pocket Legend\ModSource"
  PS> .\tools\Package.ps1
  결과: ModSource\dist\*.zip
#>
param(
    [string]$BepInExVersion = "5.4.23.6",
    [string]$GameBuild = "26896844",       # Steam 빌드 번호 (steamapps\appmanifest_3129790.acf 의 buildid)
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
#    분류(UX / UI / BAL), 한국어 이름, 싱글 전용 여부는 각 Plugin.cs 의 상수(ModCategory, NameKo, SingleOnly)에서 자동으로 읽음
$Mods = [ordered]@{
    # UX: 조작 편의
    "ItemSort"           = @{ Desc = "아이템 목록(아이템 메뉴, 선택 창, 전투 더미)에 정렬 버튼 추가: 획득순 / 유형별 / 이름순 / 강화순" }
    "AlwaysShowItemInfo" = @{ Desc = "아이템 선택 창, 이벤트 선택지의 아이템 툴팁 등에서 우클릭으로 상세보기 (전투 인벤토리의 우클릭 사용은 그대로)" }
    "PeekHandInfo"       = @{ Desc = "전투 중 '꺼낼 아이템 선택' 창을 접어둔 상태에서 손패에 마우스를 올리면 정보 표시 (보기 전용)" }
    "InventoryFit"       = @{ Desc = "Alt 키로 전투 인벤토리를 반투명하게 해서 가려진 유물·캐릭터 보기 (누르고 있는 동안 / 누를 때마다 전환, '나의 인벤토리' 화면에서 변경)" }
    # UI: 정보 표시
    "ItemCount"          = @{ Desc = "오른쪽 위 '아이템' 아이콘 아래에 유형별 보유 수(무기/방어구/스킬/귀속) 2x2 표, 아이콘 오른쪽 아래에 총 보유 개수 동그라미" }
    "ShowCardShortcut"   = @{ Desc = "전투 손패 위에 숫자 단축키(1~0) 표시, 턴 종료 버튼 위에 손패 수 / 10 표시" }
    "TotalInvenInfo"     = @{ Desc = "전투 인벤토리의 사용 가능한 아이템 기준 총/최대 피해·방어 표, 고고는 마력, 스택은 일월경·빈칸 (위치: 인벤토리 아래 / 캐릭터 머리 위)" }
    # BAL: 밸런스 변경 (협동 플레이 중 자동 비적용)
    "EnhanceCost"        = @{ Desc = "대장간 강화 비용을 등급과 관계없이 강화석 1개로 고정 (설정에서 개수 변경 가능)" }
    "BetterCardSkip"     = @{ Desc = "전투 승리 보상의 '카드 획득', '인벤토리 조각' 줄에 '보상 넘기기 (+2)' 버튼: 넘기면 골드 2 (설정에서 변경 가능)" }
    "BetterStartChoices" = @{ Desc = "게임 시작 선택지에 '무작위 일반 유물 받기' 추가, 아이템 보상·인벤토리 조각을 희귀 등급으로" }
}
# 분류 → zip 이름 / 안내문 제목
$Groups = [ordered]@{
    "UX"  = @{ Zip = "UX";      Title = "편의 모드 (UX) - 조작을 편하게" }
    "UI"  = @{ Zip = "UI";      Title = "정보 표시 모드 (UI) - 화면에 정보를 더 보여 줌" }
    "BAL" = @{ Zip = "Balance"; Title = "밸런스 변경 모드 (BAL) - 싱글 전용, 협동 플레이 중에는 자동으로 적용되지 않음" }
}
$Common = "ModSwitch"   # 모든 zip 에 함께 들어가는 공통 모드

function Get-ModInfo([string]$folder) {
    $src = Get-Content (Join-Path $Root "$folder\Plugin.cs") -Raw -Encoding UTF8
    $m = [regex]::Match($src, 'BepInPlugin\(Guid,\s*"([^"]+)",\s*"([^"]+)"\)')
    if (-not $m.Success) { throw "$folder\Plugin.cs 에서 BepInPlugin 이름/버전을 찾지 못함" }
    $cat    = [regex]::Match($src, 'ModCategory\s*=\s*"([^"]*)"').Groups[1].Value
    $ko     = [regex]::Match($src, 'NameKo\s*=\s*"([^"]*)"').Groups[1].Value
    $single = [regex]::Match($src, 'SingleOnly\s*=\s*(true|false)').Groups[1].Value -eq "true"
    [pscustomobject]@{ Folder = $folder; Name = $m.Groups[1].Value; Version = $m.Groups[2].Value;
                       Dll = "PocketLegend.$folder.dll"; Category = $cat; NameKo = $ko; SingleOnly = $single;
                       Balance = ($cat -eq "BAL"); Menu = if ($cat) { "${cat}_$ko" } else { $ko } }
}

# ── 1) 빌드 ─────────────────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    foreach ($folder in @($Common) + $Mods.Keys) {
        Write-Host "빌드: $folder" -ForegroundColor Cyan
        # -t:Rebuild: 개발용 빌드(-p:ModDev=true, 자체 테스트 포함) 결과가 남아 있어도 배포용으로 새로 컴파일
        dotnet build (Join-Path $Root "$folder\$folder.csproj") -c Release -t:Rebuild --nologo -v q
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

function Get-ModLine($info) {
    $tag = if ($info.SingleOnly) { " [싱글 전용]" } else { "" }
    "- $($info.Name) v$($info.Version)  (모드 관리: $($info.Menu)$tag)`r`n    $($Mods[$info.Folder].Desc)"
}

function Get-ModSections($infos) {
    $sections = @()
    foreach ($cat in $Groups.Keys) {
        $inCat = @($infos | Where-Object { $_.Category -eq $cat })
        if ($inCat.Count) { $sections += "[$($Groups[$cat].Title)]`r`n" + (($inCat | ForEach-Object { Get-ModLine $_ }) -join "`r`n") }
    }
    $sections -join "`r`n`r`n"
}

# $kind: Base / Pack / AllInOne → 설치 방법만 다름
function Get-ReadmeText($infos, [string]$title, [string]$kind, [string]$date) {
    $switch = Get-ModInfo $Common
    $mods = if ($infos.Count) { (Get-ModSections $infos) + "`r`n`r`n" } else { "" }
    $install = switch ($kind) {
        "Pack" { @"
[설치] ※ Base zip(PocketLegend-1-Base)을 먼저 설치해야 동작합니다.
1. Steam 라이브러리 > Pocket Legend 우클릭 > 관리 > 로컬 파일 보기
2. 열린 폴더(PocketLegend.exe 가 있는 곳)에 Base zip 내용물을 풀기 (이미 했다면 생략)
3. 같은 폴더에 이 zip 의 내용물도 그대로 풀기
4. 게임 실행 → 타이틀 화면 '모드 관리'에서 ON/OFF 확인
"@ }
        "Base" { @"
[설치]
1. Steam 라이브러리 > Pocket Legend 우클릭 > 관리 > 로컬 파일 보기
2. 열린 폴더(PocketLegend.exe 가 있는 곳)에 이 zip 의 내용물을 그대로 풀기
3. 원하는 모드 묶음 zip(PocketLegend-2-UX / UI / Balance)도 같은 폴더에 풀기
   (Base 만 설치하면 '모드 관리' 메뉴만 생기고 다른 기능은 없습니다)
4. 게임 실행 → 타이틀 화면 '모드 관리'에서 ON/OFF 확인
"@ }
        default { @"
[설치]
1. Steam 라이브러리 > Pocket Legend 우클릭 > 관리 > 로컬 파일 보기
2. 열린 폴더(PocketLegend.exe 가 있는 곳)에 이 zip 의 내용물을 그대로 풀기
3. 게임 실행 → 타이틀 화면 '모드 관리'에서 원하지 않는 모드는 OFF
"@ }
    }
@"
$title
PocketLegend Mod Control By Clann (비공식 팬 모드)  $date
======================================================

[먼저 읽어 주세요]
1. 이 모드는 개발사와 무관한 비공식 팬 모드입니다.
   모드 사용 중 생긴 문제나 문의는 개발사가 아닌 모드 제작자(스팀 커뮤니티 가이드 댓글 / 디스코드 _clann)에게 부탁드립니다.
2. 협동 플레이에서는 모드를 꺼 주세요.
   협동 플레이에서는 골드를 일행이 함께 쓰기 때문에 다른 분께도 영향이 갑니다.
   타이틀 화면 '모드 관리'에서 끌 수 있습니다. (밸런스가 바뀌는 모드는 협동 중 자동으로 적용되지 않지만, 꺼 두는 것을 권장합니다)
3. 설치 전에 세이브를 백업해 주세요. 아래 폴더를 통째로 다른 곳에 복사해 두면 됩니다.
   %USERPROFILE%\AppData\LocalLow\ShadingBox\PocketLegend\Save
   (Windows 탐색기 주소창에 그대로 붙여 넣으면 열립니다)

$mods[공통 - Base 에 포함]
- BepInEx $BepInExVersion : 모드를 불러오는 로더 (게임 파일은 바꾸지 않음)
- $($switch.Name) v$($switch.Version)
    타이틀 화면 '게임종료' 위의 '모드 관리' 메뉴에서 모드별 ON/OFF와 세부 옵션을 바꿉니다.
    게임 언어가 한국어면 한국어, 그 외에는 영어로 표시됩니다.

$install

[제거]
- 모드 하나만: BepInEx\plugins 폴더에서 해당 PocketLegend.*.dll 삭제
- 전부: winhttp.dll 삭제 (BepInEx 폴더까지 지우면 완전 제거)

[설정 / 버그 제보]
- 모드별 설정: BepInEx\config\pocketlegend.*.cfg (게임 1회 실행 후 생성, 게임을 끈 상태에서 메모장으로 수정)
- 피드백 / 버그 제보: 스팀 커뮤니티 가이드 댓글 또는 디스코드 _clann
  (어떤 상황에서 생겼는지 + 켜 둔 모드 + BepInEx\LogOutput.log 파일을 함께 알려 주시면 빨리 고칠 수 있습니다)

[테스트 환경]
- Pocket Legend (Steam) 빌드 $GameBuild
- BepInEx $BepInExVersion (Windows x64)
- 게임이 업데이트되면 모드가 동작하지 않을 수 있습니다. 확인 후 업데이트하겠습니다.

[라이선스 / 포함된 외부 구성요소 (수정 없이 공식 배포본 그대로 포함)]
- BepInEx $BepInExVersion : MIT (PocketLegendMods\LICENSE_BepInEx.txt)
    https://github.com/BepInEx/BepInEx
- UnityDoorstop (winhttp.dll) : LGPL-2.1 (PocketLegendMods\LICENSE_UnityDoorstop.txt)
    https://github.com/NeighTools/UnityDoorstop
- HarmonyX, MonoMod, Mono.Cecil (BepInEx\core) : MIT
- 게임 파일은 포함되어 있지 않습니다.
"@
}

function Get-ModReadmeMarkdown($infos, [string]$date) {
    $switch = Get-ModInfo $Common
    $sections = foreach ($cat in $Groups.Keys) {
        $inCat = @($infos | Where-Object { $_.Category -eq $cat })
        if (-not $inCat.Count) { continue }
        $lines = $inCat | ForEach-Object {
            $tag = if ($_.SingleOnly) { " ``[싱글 전용]``" } else { "" }
            "- **$($_.Name)** v$($_.Version) (모드 관리: $($_.Menu)$tag) — $($Mods[$_.Folder].Desc)"
        }
        "## $($Groups[$cat].Title)`r`n" + ($lines -join "`r`n")
    }
    $list = $sections -join "`r`n`r`n"
@"
# [비공식 모드] PocketLegend Mod Control By Clann

Pocket Legend 편의 모드 모음입니다. BepInEx 포함이라 압축만 풀면 됩니다. ($date)

## ⚠ 먼저 읽어 주세요
1. **개발사와 무관한 비공식 팬 모드입니다.** 모드 사용 중 생긴 문제나 문의는 개발사가 아닌 **모드 제작자(스팀 커뮤니티 가이드 댓글 / 디스코드 ``_clann``)**에게 부탁드립니다.
2. **협동 플레이에서는 모드를 꺼 주세요.** 협동 플레이에서는 골드를 일행이 함께 쓰기 때문에 다른 분께도 영향이 갑니다. (타이틀 화면 '모드 관리'에서 끌 수 있습니다. 밸런스가 바뀌는 모드는 협동 중 자동으로 적용되지 않지만, 꺼 두는 것을 권장합니다)
3. **설치 전에 세이브를 백업해 주세요.** 아래 폴더를 통째로 다른 곳에 복사해 두면 됩니다. (탐색기 주소창에 그대로 붙여 넣으면 열립니다)
``````
%USERPROFILE%\AppData\LocalLow\ShadingBox\PocketLegend\Save
``````

## 설치
1. Steam > Pocket Legend 우클릭 > 관리 > 로컬 파일 보기
2. 열린 폴더(``PocketLegend.exe`` 가 있는 곳)에 압축 풀기
3. 게임 실행 → 타이틀 화면 **모드 관리** 메뉴에서 원하지 않는 모드는 OFF

## 제거
- 모드 하나만: ``BepInEx\plugins`` 에서 해당 ``PocketLegend.*.dll`` 삭제
- 전부: 게임 폴더의 ``winhttp.dll`` 삭제 (``BepInEx`` 폴더까지 지우면 완전 제거)

$list

## 공통
- **BepInEx** $BepInExVersion — 모드를 불러오는 로더 (게임 파일은 바꾸지 않음)
- **$($switch.Name)** v$($switch.Version) — 타이틀 화면 '모드 관리' 메뉴에서 모드별 ON/OFF와 세부 옵션 (게임 언어가 한국어면 한국어, 그 외 영어)

## 설정 / 버그 제보
- 모드별 설정: ``BepInEx\config\pocketlegend.*.cfg`` (게임 1회 실행 후 생성)
- 피드백 / 버그 제보: 스팀 커뮤니티 가이드 댓글 또는 디스코드 ``_clann`` (어떤 상황에서 생겼는지 + 켜 둔 모드 + ``BepInEx\LogOutput.log`` 파일)

## 테스트 환경
- Pocket Legend (Steam) 빌드 $GameBuild
- BepInEx $BepInExVersion (Windows x64)
- 게임이 업데이트되면 동작하지 않을 수 있습니다.

## 라이선스
- BepInEx $BepInExVersion : MIT / UnityDoorstop (``winhttp.dll``) : LGPL-2.1 — 전문은 ``PocketLegendMods`` 폴더
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
$date = Get-Date -Format "yyyy.MM.dd"
$switchInfo = Get-ModInfo $Common
$allInfos = @($Mods.Keys | ForEach-Object { Get-ModInfo $_ })

foreach ($info in $allInfos) {
    if (-not $Groups.Contains($info.Category)) { throw "$($info.Folder): ModCategory '$($info.Category)' 가 UX / UI / BAL 이 아님" }
}

# 1) Base: BepInEx + 모드 관리
$stage = Join-Path $stageRoot "Base"
New-BaseLayout $stage
Add-Mod $stage $switchInfo
[IO.File]::WriteAllText((Join-Path $stage "PocketLegendMods\README_Base.txt"),
    (Get-ReadmeText @() "Base (BepInEx $BepInExVersion + $($switchInfo.Name) v$($switchInfo.Version))" "Base" $date), $Utf8Bom)
New-Zip $stage (Join-Path $Dist "PocketLegend-1-Base-$date.zip")

# 2) 분류별 묶음 (DLL 만, Base 위에 풀기)
foreach ($cat in $Groups.Keys) {
    $infos = @($allInfos | Where-Object { $_.Category -eq $cat })
    if (-not $infos.Count) { continue }
    $zipName = $Groups[$cat].Zip
    $stage = Join-Path $stageRoot $zipName
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    New-Item -ItemType Directory -Force (Join-Path $stage "BepInEx\plugins"), (Join-Path $stage "PocketLegendMods") | Out-Null
    foreach ($info in $infos) { Add-Mod $stage $info }
    [IO.File]::WriteAllText((Join-Path $stage "PocketLegendMods\README_$zipName.txt"),
        (Get-ReadmeText $infos $Groups[$cat].Title "Pack" $date), $Utf8Bom)
    New-Zip $stage (Join-Path $Dist "PocketLegend-2-$zipName-$date.zip")
}

# 3) All-in-One: Base + 모든 모드
$stage = Join-Path $stageRoot "AllInOne"
New-BaseLayout $stage
Add-Mod $stage $switchInfo
foreach ($info in $allInfos) { Add-Mod $stage $info }
[IO.File]::WriteAllText((Join-Path $stage "PocketLegendMods\README_AllInOne.txt"),
    (Get-ReadmeText $allInfos "All-in-One (모든 모드)" "AllInOne" $date), $Utf8Bom)
# 게임 폴더 맨 위에 풀리는 안내문 (게임/다른 모드의 README 와 겹치지 않도록 Readme_mod.md)
[IO.File]::WriteAllText((Join-Path $stage "Readme_mod.md"), (Get-ModReadmeMarkdown $allInfos $date), $Utf8Bom)
New-Zip $stage (Join-Path $Dist "PocketLegend-AllInOne-$date.zip")

Remove-Item -Recurse -Force $stageRoot
Write-Host "`n완료: $Dist" -ForegroundColor Green
