param(
    [string]$InputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'docs\wm00010000-300000.xls'),
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'TestArtifacts\wm-reference-rows.json')
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$rootPrefix = $projectRoot.TrimEnd('\') + '\'

function Resolve-ProjectPath([string]$path) {
    $resolved = [IO.Path]::GetFullPath($path)
    if (-not $resolved.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing a path outside the project root: $resolved"
    }
    return $resolved
}

$resolvedInput = Resolve-ProjectPath $InputPath
$resolvedOutput = Resolve-ProjectPath $OutputPath
if (-not [IO.File]::Exists($resolvedInput)) { throw "Input workbook not found: $resolvedInput" }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null

$excel = $null
$workbook = $null
$worksheet = $null
$usedRange = $null
try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $workbook = $excel.Workbooks.Open($resolvedInput, 0, $true)
    $worksheet = $workbook.Worksheets.Item(1)
    $usedRange = $worksheet.UsedRange
    $values = $usedRange.Value2
    $rowCount = [int]$usedRange.Rows.Count
    $columnCount = [int]$usedRange.Columns.Count

    $columns = @{}
    for ($column = 1; $column -le $columnCount; $column++) {
        $header = if ($null -eq $values[1, $column]) { '' } else { ([string]$values[1, $column]).Trim() }
        if ($header) { $columns[$header] = $column }
    }

    foreach ($required in @('등록번호', '자료명', '저자', '출판사', '출판년', '청구기호', '소장분관', '소장서고', '자료상태')) {
        if (-not $columns.ContainsKey($required)) { throw "Required header not found: $required" }
    }

    function Get-Cell([int]$row, [string]$header) {
        if (-not $columns.ContainsKey($header)) { return '' }
        $column = $columns[$header]
        if ($null -eq $values[$row, $column]) { return '' }
        return ([string]$values[$row, $column]).Trim()
    }

    $records = [Collections.Generic.List[object]]::new()
    for ($row = 2; $row -le $rowCount; $row++) {
        $registrationNumber = (Get-Cell $row '등록번호').ToUpperInvariant()
        if ($registrationNumber -notmatch '^WM\d+$') { continue }
        $records.Add([ordered]@{
            '선택' = Get-Cell $row '선택'
            '등록번호' = $registrationNumber
            '콘텐츠번호' = Get-Cell $row '콘텐츠번호'
            '자료명' = Get-Cell $row '자료명'
            '저자' = Get-Cell $row '저자'
            '출판사' = Get-Cell $row '출판사'
            '출판년' = Get-Cell $row '출판년'
            '청구기호' = Get-Cell $row '청구기호'
            '소장분관' = Get-Cell $row '소장분관'
            '소장서고' = Get-Cell $row '소장서고'
            '자료상태' = Get-Cell $row '자료상태'
            '비고' = Get-Cell $row '비고'
            '처리결과' = Get-Cell $row '처리결과'
            '보존서고 등록' = Get-Cell $row '보존서고 등록'
        })
    }

    $json = $records | ConvertTo-Json -Depth 3 -Compress
    [IO.File]::WriteAllText($resolvedOutput, $json, [Text.UTF8Encoding]::new($false))
    Write-Output "Extracted $($records.Count) rows to $resolvedOutput"
}
finally {
    if ($workbook) { $workbook.Close($false); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($workbook) }
    if ($usedRange) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($usedRange) }
    if ($worksheet) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($worksheet) }
    if ($excel) { $excel.Quit(); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel) }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
