<#
.SYNOPSIS
  스팀 가이드 원본(docs\SteamGuide_KO.txt)을 미리보기 + 칸별 [복사] 버튼이 있는 HTML 로 변환

.DESCRIPTION
  원본은 "##### 번호 이름 #####" 줄로 칸을 나눈다. 두 ##### 줄 사이가 스팀 칸에 붙여 넣을 내용.
    1-n : 1단계 "가이드 기본 정보" 칸 (제목, 대표 이미지, 언어, 가이드 설명, 분류)
    2-n : 2단계 "가이드 내용"의 섹션 n (섹션 제목 / 섹션 내용)
  이 스크립트는 원본을 바꾸지 않고 docs\SteamGuide_KO_preview.html 을 만든다.
    - 섹션 내용의 BBCode 는 스팀과 비슷한 모양으로 그려서 보여 줌
    - 칸마다 [복사] 버튼: 누르면 원본 그대로(BBCode 포함) 클립보드에 복사 → 스팀 칸에 붙여 넣기
  미리보기 파일은 생성물이라 git 에 올리지 않음 (.gitignore)

.EXAMPLE
  PS> .\tools\Preview-SteamGuide.ps1 -Open
#>
param([switch]$Open)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$src  = Join-Path $Root "docs\SteamGuide_KO.txt"
$out  = Join-Path $Root "docs\SteamGuide_KO_preview.html"

$text = [IO.File]::ReadAllText($src, [Text.Encoding]::UTF8) -replace "`r`n", "`n"

function ConvertTo-HtmlText([string]$s) {
    $s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace('"', "&quot;")
}

# BBCode → HTML (먼저 < > & 를 이스케이프해서 본문 글자가 태그로 해석되지 않게)
function ConvertFrom-BBCode([string]$bb) {
    $h = ConvertTo-HtmlText $bb.Trim()
    $rules = [ordered]@{
        '\[h1\](.*?)\[/h1\]'            = '<h2>$1</h2>'
        '\[h2\](.*?)\[/h2\]'            = '<h3>$1</h3>'
        '\[h3\](.*?)\[/h3\]'            = '<h4>$1</h4>'
        '\[b\](.*?)\[/b\]'              = '<b>$1</b>'
        '\[i\](.*?)\[/i\]'              = '<i>$1</i>'
        '\[u\](.*?)\[/u\]'              = '<u>$1</u>'
        '\[code\](.*?)\[/code\]'        = '<code>$1</code>'
        '\[url=([^\]]+)\](.*?)\[/url\]' = '<a href="$1">$2</a>'
        '\[list\]'  = '<ul>';  '\[/list\]'  = '</ul>'
        '\[olist\]' = '<ol>';  '\[/olist\]' = '</ol>'
        '\[\*\]'    = '<li>'
        '\[table\]' = '<table>'; '\[/table\]' = '</table>'
        '\[tr\]'    = '<tr>';    '\[/tr\]'    = '</tr>'
        '\[th\]'    = '<th>';    '\[/th\]'    = '</th>'
        '\[td\]'    = '<td>';    '\[/td\]'    = '</td>'
    }
    foreach ($k in $rules.Keys) { $h = [regex]::Replace($h, $k, $rules[$k], 'Singleline') }
    # 블록 태그 앞뒤 줄바꿈은 지우고, 나머지 줄바꿈(표 칸 안 포함)은 <br>
    $h = [regex]::Replace($h, '\n*(</?(h2|h3|h4|ul|ol|table|tr)>|<li>|</?t[hd]>|<code>|</code>)\n*', '$1')
    $h.Replace("`n", "<br>`n")
}

# 1) "##### 이름 #####" 줄로 블록 나누기
$markers = [regex]::Matches($text, '(?m)^#{5,}[ \t]*(.+?)[ \t]*#{5,}[ \t]*$')
if ($markers.Count -eq 0) { throw "SteamGuide_KO.txt 에서 '##### 이름 #####' 줄을 찾지 못함" }
$intro = $text.Substring(0, $markers[0].Index).Trim()
$blocks = for ($i = 0; $i -lt $markers.Count; $i++) {
    $start = $markers[$i].Index + $markers[$i].Length
    $end = if ($i + 1 -lt $markers.Count) { $markers[$i + 1].Index } else { $text.Length }
    $name = $markers[$i].Groups[1].Value
    [pscustomobject]@{
        Name = $name
        No   = ($name -split '\s+')[0]          # 1-1, 2-3 ...
        Body = $text.Substring($start, $end - $start).Trim()
    }
}

# 2) [복사] 버튼: 원본 텍스트는 숨긴 textarea 에 두고 버튼이 그 값을 복사
$script:copyId = 0
function New-CopyButton([string]$raw, [string]$label) {
    $script:copyId++
    $id = "raw$($script:copyId)"
    "<button class=""copy"" data-label=""$label"" onclick=""copyBlock('$id', this)"">$label</button>" +
    "<textarea id=""$id"" hidden>$(ConvertTo-HtmlText $raw)</textarea>"
}

# 3) 1단계 칸들
$metaRows = foreach ($b in ($blocks | Where-Object { $_.No -like '1-*' })) {
    $value = (ConvertTo-HtmlText $b.Body).Replace("`n", "<br>`n")
    $button = if ($b.Name -like '*이미지*') { "<span class=""hint"">파일 선택으로 업로드</span>" } else { New-CopyButton $b.Body "복사" }
    "<tr><th>$(ConvertTo-HtmlText $b.Name)</th><td>$value</td><td class=""btn"">$button</td></tr>"
}
$metaHtml = "<table class=""meta"">" + ($metaRows -join "`n") + "</table>"

# 4) 2단계 섹션들: 같은 번호(2-n)의 "섹션 제목" + "섹션 내용"
$sectionHtml = foreach ($group in ($blocks | Where-Object { $_.No -like '2-*' } | Group-Object No)) {
    $title   = ($group.Group | Where-Object { $_.Name -like '*제목*' } | Select-Object -First 1).Body
    $content = ($group.Group | Where-Object { $_.Name -like '*내용*' } | Select-Object -First 1).Body
    $no = $group.Name.Substring(2)
    "<section><div class=""sec-head""><span class=""sec-no"">섹션 $no</span>" +
    (New-CopyButton $title "제목 복사") + (New-CopyButton $content "내용 복사") + "</div>" +
    "<h1>$(ConvertTo-HtmlText $title)</h1>`n" + (ConvertFrom-BBCode $content) + "</section>"
}
$sectionHtml = $sectionHtml -join "`n"
$introHtml = (ConvertTo-HtmlText $intro).Replace("`n", "<br>`n")

$html = @"
<!DOCTYPE html>
<html lang="ko">
<head>
<meta charset="utf-8">
<title>스팀 가이드 미리보기 - SteamGuide_KO.txt</title>
<style>
  body { background: #1b2838; color: #acb2b8; font: 15px/1.6 "Malgun Gothic", sans-serif; margin: 0; }
  .wrap { max-width: 900px; margin: 0 auto; padding: 24px 32px 64px; background: #16202d; min-height: 100vh; }
  .note { background: #2a475e; color: #c7d5e0; padding: 10px 14px; border-radius: 4px; font-size: 13px; }
  .label { color: #66c0f4; font-size: 12px; letter-spacing: 1px; margin: 28px 0 8px; }
  h1 { color: #ffffff; font-size: 22px; font-weight: normal; margin: 6px 0 12px; }
  h2, h3 { color: #66c0f4; font-size: 18px; font-weight: normal; margin: 22px 0 8px; }
  b { color: #e5e5e5; }
  a { color: #66c0f4; }
  code { display: block; background: #0e141b; color: #c7d5e0; padding: 8px 12px; margin: 6px 0; font-family: Consolas, monospace; }
  ul, ol { margin: 6px 0; padding-left: 26px; }
  li { margin: 4px 0; }
  table { border-collapse: collapse; margin: 10px 0; }
  th, td { border: 1px solid #2a475e; padding: 6px 10px; text-align: left; vertical-align: top; }
  th { background: #2a475e; color: #ffffff; font-weight: normal; }
  table.meta { width: 100%; font-size: 13px; }
  table.meta th { width: 170px; }
  table.meta td.btn { width: 90px; text-align: center; }
  section { border-top: 1px solid #2a475e; margin-top: 18px; padding-top: 10px; }
  .sec-head { display: flex; align-items: center; gap: 8px; }
  .sec-no { color: #66c0f4; font-size: 12px; letter-spacing: 1px; margin-right: auto; }
  button.copy { background: #4c6b22; color: #d2efa9; border: 0; border-radius: 2px; padding: 4px 12px; cursor: pointer; font: inherit; font-size: 13px; }
  button.copy:hover { background: #5c7e2b; }
  button.copy.done { background: #2a475e; color: #ffffff; }
  .hint { color: #8f98a0; font-size: 12px; }
</style>
<script>
  // 원본 텍스트(BBCode 포함)를 클립보드로. file:// 에서 막히면 예전 방식(execCommand)으로
  function copyBlock(id, button) {
    var text = document.getElementById(id).value;
    function done() {
      button.textContent = "복사됨 ✓";
      button.classList.add("done");
      setTimeout(function () { button.textContent = button.getAttribute("data-label"); button.classList.remove("done"); }, 1500);
    }
    function fallback() {
      var area = document.createElement("textarea");
      area.value = text;
      document.body.appendChild(area);
      area.select();
      document.execCommand("copy");
      document.body.removeChild(area);
      done();
    }
    if (navigator.clipboard && window.isSecureContext) navigator.clipboard.writeText(text).then(done, fallback);
    else fallback();
  }
</script>
</head>
<body><div class="wrap">
<div class="note">미리보기 전용입니다. 스팀 칸에는 각 [복사] 버튼으로 복사한 원본을 붙여 넣으세요. 실제 스팀 화면과 글꼴/간격은 조금 다를 수 있습니다.<br><br>$introHtml</div>
<div class="label">1단계 · 가이드 기본 정보</div>
$metaHtml
<div class="label">2단계 · 가이드 내용 (섹션 추가 → 제목과 내용을 각각 붙여 넣기)</div>
$sectionHtml
</div></body>
</html>
"@

[IO.File]::WriteAllText($out, $html, (New-Object System.Text.UTF8Encoding($true)))
Write-Host "미리보기: $out" -ForegroundColor Green
if ($Open) { Start-Process $out }
