# =====================================================================
#  Medición de rendimiento con los 200+ juegos (Persona A) — para dejar corriendo de noche
#  Uso (PowerShell, dentro de la carpeta A_Orquestador):
#      powershell -ExecutionPolicy Bypass -File .\medir_noche.ps1
#  Si se corta (apagón, se cerró la ventana, se reinició), se vuelve a ejecutar IGUAL:
#  la opción "continuar" reutiliza las mediciones ya guardadas en resultados_real.csv.
# =====================================================================

# "Continue": en Windows PowerShell 5.1, con "Stop" cualquier aviso que dotnet escriba en stderr cortaría la medición.
$ErrorActionPreference = "Continue"
Set-Location -Path $PSScriptRoot

# 1) Que Windows NO se suspenda mientras corre (no necesita permisos de administrador;
#    vuelve a la normalidad al cerrar esta ventana). La pantalla sí se puede apagar.
Add-Type -Namespace Win32 -Name Energia -MemberDefinition @"
[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint esFlags);
"@
$ES_CONTINUOUS = [uint32]"0x80000000"; $ES_SYSTEM_REQUIRED = [uint32]"0x00000001"
[Win32.Energia]::SetThreadExecutionState($ES_CONTINUOUS -bor $ES_SYSTEM_REQUIRED) | Out-Null

# 2) Recordatorio: medir solo con la versión FINAL de los scrapers (hltb_id ya completados por B).
Write-Host "Revisando cuántos juegos del REPO siguen sin hltb_id..." -ForegroundColor Cyan
dotnet run --project ..\ScraperJuegos -c Release -- hltb-pendientes
Write-Host ""
Write-Host "Si quedan muchos juegos sin hltb_id, cierre esta ventana y pida a B correr:" -ForegroundColor Yellow
Write-Host "    dotnet run -- hltb-autocompletar --aplicar   (en la carpeta ScraperJuegos)" -ForegroundColor Yellow
Write-Host "Empieza en 20 segundos (Ctrl+C para cancelar)..."
Start-Sleep -Seconds 20

# 3) Medición: 200+ juegos, 3 repeticiones, 1/2/4/máximo trabajadores y análisis por niveles.
$inicio = Get-Date
$log = "medicion_$(Get-Date -Format 'yyyyMMdd_HHmm').txt"
Write-Host "Inicio: $inicio. Registro en $log. Dura varias horas: deje la computadora conectada a la corriente y a la misma red." -ForegroundColor Green
dotnet run -c Release -- real ..\ScraperJuegos\Data\juegos.csv 500 medir continuar reps=3 2>&1 | Tee-Object -FilePath $log

$fin = Get-Date
Write-Host ""
Write-Host "Terminó: $fin (duración: $([math]::Round(($fin - $inicio).TotalHours, 2)) horas)." -ForegroundColor Green
Write-Host "Resultados: resultados_real.csv  |  Registro completo: $log  |  Fallos: log_fallos.txt"
[Win32.Energia]::SetThreadExecutionState($ES_CONTINUOUS) | Out-Null
