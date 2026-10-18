<#
.SYNOPSIS
    Auditoría de diagramas Mermaid de AulaChatNet.

.DESCRIPTION
    Comprueba que los bloques ```mermaid de los .md del proyecto (README.md y
    docs/) cumplen las convenciones acordadas. Son convenciones del proyecto,
    no errores del lenguaje: algunas Mermaid las admite, pero en este
    repositorio se evitan porque han dado problemas de renderizado.

    Reglas comprobadas:
      1. «;» en cualquier instrucción      -> prohibido en este proyecto
      2. «style» dentro de sequenceDiagram -> sequenceDiagram no lo admite
      3. «direction» dentro de subgraph     -> se evita
      4. «style <id>» de un subgraph       -> se evita el estilo a subgrafos
      5. Bloque ```mermaid sin cerrar      -> error de formato

    La auditoría se ejecuta SIEMPRE antes de dar por bueno un documento:
        powershell -File scripts\auditar-mermaid.ps1

.PARAMETER Rutas
    Ficheros o carpetas a auditar. Por defecto, README.md y docs/.

.NOTES
    Compatible con Windows PowerShell 5.1: sin operadores de PS7 (??, ?:).
    Código de salida 0 si todo pasa, 1 si hay incidencias.
#>
[CmdletBinding()]
param(
    [string[]] $Rutas = @('README.md', 'docs')
)

# Windows PowerShell 5.1 escribe en la consola con la página de códigos de la
# consola (OEM) y los acentos y las comillas angulares salen rotos. Se fuerza
# UTF-8 desde el principio para que el informe se lea bien.
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
}
catch {
    # Sin consola (salida redirigida a fichero): no hay nada que corregir
}

# ─────────────────────────────────────────────────────────────────────
# Recopilación de ficheros .md (se aceptan ficheros y carpetas)
# ─────────────────────────────────────────────────────────────────────
$ficheros = New-Object System.Collections.Generic.List[string]

foreach ($ruta in $Rutas) {
    if (Test-Path -LiteralPath $ruta -PathType Container) {
        Get-ChildItem -LiteralPath $ruta -Recurse -Filter *.md -File |
            ForEach-Object { $ficheros.Add($_.FullName) }
    }
    elseif (Test-Path -LiteralPath $ruta -PathType Leaf) {
        $ficheros.Add((Resolve-Path -LiteralPath $ruta).Path)
    }
    else {
        Write-Warning "No existe: $ruta"
    }
}

if ($ficheros.Count -eq 0) {
    Write-Host "No hay ficheros .md que auditar." -ForegroundColor Yellow
    exit 0
}

# ─────────────────────────────────────────────────────────────────────
# Extrae los bloques ```mermaid con su línea de inicio (1-based)
# ─────────────────────────────────────────────────────────────────────
function Get-BloquesMermaid {
    param([string] $Fichero)

    $lineas = Get-Content -LiteralPath $Fichero -Encoding UTF8
    $bloques = New-Object System.Collections.Generic.List[object]
    $dentro = $false
    $lineaInicio = 0
    $contenido = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $lineas.Count; $i++) {
        $linea = $lineas[$i]

        if (-not $dentro -and $linea -match '^\s*```mermaid\s*$') {
            $dentro = $true
            $lineaInicio = $i + 1
            $contenido.Clear()
        }
        elseif ($dentro -and $linea -match '^\s*```\s*$') {
            $bloques.Add([pscustomobject]@{
                Inicio    = $lineaInicio
                Cerrado   = $true
                Contenido = $contenido.ToArray()
            })
            $dentro = $false
        }
        elseif ($dentro) {
            $contenido.Add($linea)
        }
    }

    # Un ```mermaid sin cerrar ya es un error de formato en sí mismo
    if ($dentro) {
        $bloques.Add([pscustomobject]@{
            Inicio    = $lineaInicio
            Cerrado   = $false
            Contenido = $contenido.ToArray()
        })
    }

    return $bloques
}

# ─────────────────────────────────────────────────────────────────────
# Reglas, aplicadas bloque a bloque con contexto (sequenceDiagram / subgraph)
# ─────────────────────────────────────────────────────────────────────
function Get-Incidencias {
    param([object] $Bloque)

    $incidencias = New-Object System.Collections.Generic.List[object]

    # El anidamiento de subgrafos se lleva con un contador: un «end» solo
    # cierra el subgráfico cuando hay uno abierto.
    $enSequence = $false
    $profundidadSubgraph = 0
    $subgraphs = New-Object 'System.Collections.Generic.HashSet[string]'

    if (-not $Bloque.Cerrado) {
        $incidencias.Add([pscustomobject]@{
            Linea = $Bloque.Inicio
            Regla = 'formato'
            Texto = 'Falta el ``` que cierra el bloque mermaid'
        })
    }

    for ($i = 0; $i -lt $Bloque.Contenido.Count; $i++) {
        $sinEspacios = $Bloque.Contenido[$i].Trim()

        # Regla 1: «;» en cualquier instrucción
        if ($sinEspacios -match ';') {
            $incidencias.Add([pscustomobject]@{
                Linea = $Bloque.Inicio + $i + 1   # +1: el contenido empieza tras el fence ```
                Regla = 'punto y coma'
                Texto = "Prohibido «;» en Mermaid: $sinEspacios"
            })
        }

        # Anotar el tipo de diagrama (para la regla 2)
        if ($sinEspacios -match '^(sequenceDiagram|flowchart|graph|classDiagram|erDiagram|stateDiagram|journey|gantt|pie|mindmap|timeline|gitGraph)\b') {
            $enSequence = ($sinEspacios -like 'sequenceDiagram*')
        }

        # Registrar subgrafos (para la regla 4). Solo se coge el identificador
        # hasta la «[»: en «subgraph INDICE["título"]» el id es INDICE.
        if ($sinEspacios -match '^subgraph\s+([A-Za-z0-9_]+)') {
            [void]$subgraphs.Add($Matches[1])
            $profundidadSubgraph++
        }
        elseif ($sinEspacios -eq 'end' -and $profundidadSubgraph -gt 0) {
            $profundidadSubgraph--
        }

        # Regla 2: style dentro de sequenceDiagram
        if ($enSequence -and $sinEspacios -match '^style\s') {
            $incidencias.Add([pscustomobject]@{
                Linea = $Bloque.Inicio + $i + 1   # +1: el contenido empieza tras el fence ```
                Regla = 'sequenceDiagram'
                Texto = 'sequenceDiagram no admite «style»'
            })
        }

        # Regla 3: direction dentro de subgraph
        if ($profundidadSubgraph -gt 0 -and $sinEspacios -match '^direction\s') {
            $incidencias.Add([pscustomobject]@{
                Linea = $Bloque.Inicio + $i + 1   # +1: el contenido empieza tras el fence ```
                Regla = 'direction en subgraph'
                Texto = 'Se evita «direction» dentro de un subgraph'
            })
        }

        # Regla 4: estilo aplicado a un subgraph
        if ($sinEspacios -match '^style\s+(\S+)') {
            if ($subgraphs.Contains($Matches[1])) {
                $incidencias.Add([pscustomobject]@{
                    Linea = $Bloque.Inicio + $i + 1   # +1: el contenido empieza tras el fence ```
                    Regla = 'estilo a subgraph'
                    Texto = "Se evita el estilo a subgrafos: style $($Matches[1])"
                })
            }
        }
    }

    return $incidencias
}

# ─────────────────────────────────────────────────────────────────────
# Ejecución
# ─────────────────────────────────────────────────────────────────────
$totales = 0
$conIncidencias = 0

foreach ($fichero in $ficheros) {
    $nombre = Resolve-Path -LiteralPath $fichero -Relative

    # El «@()» es imprescindible: una función que devuelve UN solo bloque lo
    # devuelve como objeto suelto y «.Count» no existiría (o daría vacío).
    $bloques = @(Get-BloquesMermaid -Fichero $fichero)

    if ($bloques.Count -eq 0) {
        continue
    }

    $fallos = New-Object System.Collections.Generic.List[object]

    foreach ($bloque in $bloques) {
        $totales++
        foreach ($incidencia in @(Get-Incidencias -Bloque $bloque)) {
            $fallos.Add($incidencia)
        }
    }

    if ($fallos.Count -eq 0) {
        Write-Host ("  OK    {0}  ({1} diagramas)" -f $nombre, $bloques.Count) -ForegroundColor Green
    }
    else {
        $conIncidencias++
        Write-Host ("  FALLO  {0}" -f $nombre) -ForegroundColor Red
        foreach ($fallo in $fallos) {
            Write-Host ("         línea {0,5}  [{1}] {2}" -f $fallo.Linea, $fallo.Regla, $fallo.Texto) -ForegroundColor Red
        }
    }
}

Write-Host ""
if ($conIncidencias -eq 0) {
    Write-Host ("Auditoría Mermaid correcta: {0} diagramas en {1} ficheros, 0 incidencias." -f $totales, $ficheros.Count) -ForegroundColor Green
    exit 0
}

Write-Host ("Auditoría Mermaid: {0} ficheros con incidencias (de {1} auditados, {2} diagramas)." -f $conIncidencias, $ficheros.Count, $totales) -ForegroundColor Red
exit 1
