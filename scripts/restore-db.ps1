<#
.SYNOPSIS
  Restaura um backup .sql.gz em um banco NOVO (padrao: finance_restore) do Postgres do Compose.

.DESCRIPTION
  Uso:  powershell -File scripts/restore-db.ps1 -File backups/finance-XXXX.sql.gz [-Database finance_restore]
  O banco indicado e apagado (se existir) e recriado; o arquivo e copiado para o container e
  restaurado com "gunzip -c | psql -v ON_ERROR_STOP=1". O banco "finance" nunca e tocado.

  Seguranca:
  - Recusa -Database com valor 'finance', 'postgres', 'template*' (qualquer caixa) ou nome fora de [A-Za-z0-9_].
  - Sem senhas na linha de comando; usa o ambiente/confianca do container.
  - Para promover a restauracao ao banco real, faca-o manualmente e conscientemente.
  Sai com codigo != 0 em qualquer falha.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$File,
    [string]$Database = 'finance_restore'
)

$ErrorActionPreference = 'Continue'  # falhas de comandos nativos sao tratadas via $LASTEXITCODE
$repo    = Split-Path -Parent $PSScriptRoot
$compose = Join-Path $repo 'docker-compose.yml'

function Fail([string]$msg) { [Console]::Error.WriteLine("ERRO: $msg"); exit 1 }
function Dc { & docker compose -f $compose @args }

if ($Database -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { Fail "nome de banco invalido: '$Database'." }
if ($Database -imatch '^(finance|postgres|template.*)$') { Fail "recusado: '$Database' nao pode ser sobrescrito por este script." }
if (-not (Test-Path -LiteralPath $File -PathType Leaf)) { Fail "arquivo nao encontrado: $File" }
$src = (Resolve-Path -LiteralPath $File).Path

$running = @(Dc ps --status running --services 2>$null)
if ($LASTEXITCODE -ne 0 -or $running -notcontains 'db') { Fail "o servico 'db' nao esta rodando." }

$tmp = '/tmp/restore-input.sql.gz'
try {
    Dc cp $src "db:$tmp" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'nao foi possivel copiar o arquivo para o container.' }

    Dc exec -T db sh -c "gzip -t $tmp" | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'o arquivo nao e um gzip valido.' }

    Dc exec -T db psql -U postgres -d postgres -v ON_ERROR_STOP=1 -q -c "DROP DATABASE IF EXISTS $Database" -c "CREATE DATABASE $Database" | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "nao foi possivel recriar o banco $Database." }

    Dc exec -T db sh -c "gunzip -c $tmp | psql -U postgres -d $Database -v ON_ERROR_STOP=1 -q -o /dev/null" | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "a restauracao em $Database falhou." }
}
finally {
    Dc exec -T db rm -f $tmp 2>&1 | Out-Null
}

Write-Output "Restaurado no banco '$Database'."
Write-Output "Inspecionar: docker compose exec db psql -U postgres -d $Database"
Write-Output "Remover:     docker compose exec -T db psql -U postgres -d postgres -c 'DROP DATABASE $Database'"
exit 0
