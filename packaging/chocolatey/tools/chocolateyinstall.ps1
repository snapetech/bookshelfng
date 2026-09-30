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

  function Set-BookshelfServiceSetting {
    param(
      [Parameter(Mandatory = $true)] [string] $Name,
      [Parameter(Mandatory = $true)] [string] $Value
    )

    & $nssm.Source set BookshelfNG $Name $Value
    if ($LASTEXITCODE -ne 0) { throw "NSSM could not set BookshelfNG $Name." }
  }

  $service = Get-Service -Name BookshelfNG -ErrorAction SilentlyContinue
  if ($service) {
    if ($service.Status -ne 'Stopped') {
      Stop-Service -Name BookshelfNG -Force
      $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
  }
  else {
    & $nssm.Source install BookshelfNG $exePath "-nobrowser -data=$dataDir"
    if ($LASTEXITCODE -ne 0) { throw 'NSSM could not install the BookshelfNG service.' }
  }

  Set-BookshelfServiceSetting 'Application' $exePath
  Set-BookshelfServiceSetting 'AppDirectory' $installDir
  Set-BookshelfServiceSetting 'AppParameters' "-nobrowser -data=$dataDir"
  Set-BookshelfServiceSetting 'Start' 'SERVICE_AUTO_START'

  Start-Service -Name BookshelfNG
  Write-Host 'BookshelfNG service started.'
}
