$ErrorActionPreference = 'Stop'
$nssm = Get-Command nssm.exe -ErrorAction SilentlyContinue
if ($nssm) {
  & $nssm.Source stop BookshelfNG confirm 2>$null
  & $nssm.Source remove BookshelfNG confirm 2>$null
}
Remove-Item -Recurse -Force (Join-Path $env:ProgramData 'BookshelfNG\app') -ErrorAction SilentlyContinue
