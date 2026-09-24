$ErrorActionPreference = 'Stop'

$packageArgs = @{
  packageName    = 'bookshelfng'
  fileType       = 'zip'
  url64bit       = 'https://github.com/snapetech/bookshelfng/releases/download/__RELEASE_TAG__/__ARCHIVE__'
  checksum64     = '__SHA256__'
  checksumType64 = 'sha256'
  unzipLocation  = Join-Path $env:ProgramData 'BookshelfNG\app'
}

Install-ChocolateyZipPackage @packageArgs

$installDir = Join-Path $packageArgs.unzipLocation '__ARCHIVE_ROOT__'
$exePath = Join-Path $installDir 'Readarr.exe'
$dataDir = Join-Path $env:ProgramData 'BookshelfNG\data'
$nssm = Get-Command nssm.exe -ErrorAction SilentlyContinue

if ($nssm) {
  New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
  & $nssm.Source install BookshelfNG $exePath "-nobrowser -data=$dataDir"
  if ($LASTEXITCODE -ne 0) { throw 'NSSM could not install the BookshelfNG service.' }
  & $nssm.Source set BookshelfNG AppDirectory $installDir
  & $nssm.Source set BookshelfNG Start SERVICE_AUTO_START
  Write-Host 'BookshelfNG service installed. Start it with: Start-Service BookshelfNG'
}
