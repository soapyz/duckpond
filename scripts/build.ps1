# 一键构建发布包：生成 dist\ 目录并压成 zip
param(
    [string]$OutDir = "dist",
    [string]$ZipName = "鸭塘-DuckPond-win64.zip"
)

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    Write-Host "[1/3] 构建自包含发布版 ..."
    dotnet publish -c Release -r win-x64 --self-contained true -o $OutDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }

    Write-Host "[2/3] 打包 zip ..."
    $zip = Join-Path $root $ZipName
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $root $OutDir) -DestinationPath $zip -CompressionLevel Optimal

    Write-Host "[3/3] 完成"
    Write-Host ("输出目录: " + (Join-Path $root $OutDir))
    Write-Host ("压缩包  : " + $zip + "  (" + [math]::Round((Get-Item $zip).Length / 1MB, 1) + " MB)")
}
finally {
    Pop-Location
}
