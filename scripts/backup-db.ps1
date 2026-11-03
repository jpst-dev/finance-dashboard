<#
.SYNOPSIS
  Backup do banco "finance" (Postgres do Docker Compose) em backups/finance-<yyyyMMdd-HHmmss>.sql.gz.

.DESCRIPTION
  Uso:  powershell -File scripts/backup-db.ps1 [-OutDir backups]
  O dump (SQL puro) e comprimido DENTRO do container e copiado com "docker compose cp",
  evitando que o PowerShell 5.1 corrompa bytes binarios em pipes/redirecionamentos.

  Seguranca:
  - Nenhuma senha na linha de comando; o exec usa o ambiente/confianca do proprio container.
  - O backup contem dados sensiveis. backups/ e ignorado pelo git; nunca versione esses arquivos.
  - Imprime apenas o caminho e o tamanho.
  Falha (exit code != 0) se o servico db nao estiver rodando ou se o dump vier vazio.
#>
[CmdletBinding()]
param([string]$OutDir = 'backups')

$ErrorActionPreference = 'Continue'  # falhas de comandos nativos sao tratadas via $LASTEXITCODE
$repo    = Split-Path -Parent $PSScriptRoot
$compose = Join-Path $repo 'docker-compose.yml'
if (-not [System.IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $repo $OutDir }

function Fail([string]$msg) { [Console]::Error.WriteLine("ERRO: $msg"); exit 1 }
function Dc { & docker compose -f $compose @args }

$running = @(Dc ps --status running --services 2>$null)
if ($LASTEXITCODE -ne 0 -or $running -notcontains 'db') { Fail "o servico 'db' nao esta rodando (docker compose up -d db)." }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$dest  = Join-Path $OutDir "finance-$stamp.sql.gz"
$tmp   = "/tmp/finance-$stamp.sql.gz"

try {
    Dc exec -T db bash -c "set -o pipefail; pg_dump -U postgres -d finance | gzip > $tmp && test -s $tmp" | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'pg_dump falhou ou gerou saida vazia.' }
    Dc cp "db:$tmp" $dest 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'nao foi possivel copiar o backup do container.' }
}
finally {
    Dc exec -T db rm -f $tmp 2>&1 | Out-Null
}

if (-not (Test-Path $dest) -or (Get-Item $dest).Length -le 0) {
    if (Test-Path $dest) { Remove-Item $dest -Force }
    Fail 'o arquivo de backup esta vazio.'
}
$size = (Get-Item $dest).Length
Write-Output "Backup: $dest ($size bytes)"
exit 0
