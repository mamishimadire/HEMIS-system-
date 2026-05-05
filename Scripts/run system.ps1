$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
Set-Location $projectRoot

$preferredPort = 5076
$launchPath = "/Account/Login?force=true"
$runRoot = Join-Path $projectRoot ".run"
$buildFolder = Join-Path $runRoot "build"
$logFolder = Join-Path $runRoot "logs"
$stdoutLog = Join-Path $logFolder "run_system.stdout.log"
$stderrLog = Join-Path $logFolder "run_system.stderr.log"
$pidFile = Join-Path $runRoot "hemisaudit.pid"
$healthTimeoutSeconds = 90

function Get-FreshLoopbackHost {
    $activeHosts = Get-CimInstance Win32_Process |
        Where-Object {
            $_.Name -eq "dotnet.exe" -and
            $_.CommandLine -and
            $_.CommandLine -match "HemisAudit\.dll" -and
            $_.CommandLine -match "--urls\s+http://([^:/\s]+):"
        } |
        ForEach-Object { $Matches[1] }

    $candidates = 20..250 |
        ForEach-Object { "127.0.0.$_" } |
        Where-Object { $_ -notin $activeHosts }

    if (-not $candidates) {
        throw "No free loopback host could be selected for HemisAudit."
    }

    return Get-Random -InputObject $candidates
}

function Get-HemisAuditProcessInfo {
    $processes = Get-CimInstance Win32_Process |
        Where-Object {
            $_.Name -eq "dotnet.exe" -and
            $_.CommandLine -and
            $_.CommandLine -match "HemisAudit\.dll"
        }

    foreach ($process in $processes) {
        $commandLine = $process.CommandLine
        $url = $null

        if ($commandLine -match "--urls\s+(\S+)") {
            $url = $Matches[1]
        }

        if (-not $url) {
            $listenPorts = Get-NetTCPConnection -State Listen -OwningProcess $process.ProcessId -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($listenPorts) {
                $url = "http://localhost:$($listenPorts.LocalPort)"
            }
        }

        if ($url) {
            [pscustomobject]@{
                ProcessId = $process.ProcessId
                Url = $url
                CommandLine = $commandLine
            }
        }
    }
}

function Test-UrlReady {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,
        [int]$TimeoutSeconds = 5
    )

    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $TimeoutSeconds
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 500
    }
    catch {
        return $false
    }
}

function Wait-ForUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,
        [int]$TimeoutSeconds = 90
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-UrlReady -Url $Url -TimeoutSeconds 5) {
            return $true
        }
        Start-Sleep -Seconds 1
    }

    return $false
}

function Open-Browser {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url
    )

    $edgeCandidates = @(
        (Join-Path $env:ProgramFiles "Microsoft\Edge\Application\msedge.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Microsoft\Edge\Application\msedge.exe")
    ) | Where-Object { $_ -and (Test-Path $_) }

    if ($edgeCandidates.Count -gt 0) {
        $edgePath = $edgeCandidates | Select-Object -First 1
        Start-Process -FilePath $edgePath -ArgumentList @("--inprivate", $Url) | Out-Null
        Write-Host "Opened latest HemisAudit build in Edge InPrivate: $Url" -ForegroundColor Green
        return
    }

    $chromeCandidates = @(
        (Join-Path $env:ProgramFiles "Google\Chrome\Application\chrome.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Google\Chrome\Application\chrome.exe")
    ) | Where-Object { $_ -and (Test-Path $_) }

    if ($chromeCandidates.Count -gt 0) {
        $chromePath = $chromeCandidates | Select-Object -First 1
        Start-Process -FilePath $chromePath -ArgumentList @("--incognito", $Url) | Out-Null
        Write-Host "Opened latest HemisAudit build in Chrome Incognito: $Url" -ForegroundColor Green
        return
    }

    Start-Process $Url | Out-Null
    Write-Host "Opened $Url in the default browser" -ForegroundColor Green
}

function Start-HemisAudit {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port,
        [Parameter(Mandatory = $true)]
        [string]$LoopbackHost
    )

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw ".NET SDK was not found on PATH. Install .NET, then run this script again."
    }

    # Use a fresh loopback IP instead of localhost so stale browser cookies
    # from previous runs do not cause Kestrel to reject the first request.
    $baseUrl = "http://${LoopbackHost}:$Port"

    foreach ($path in @($runRoot, $buildFolder, $logFolder)) {
        if (-not (Test-Path $path)) {
            New-Item -ItemType Directory -Path $path | Out-Null
        }
    }

    Write-Host "Building HemisAudit for startup..." -ForegroundColor Cyan
    dotnet build ".\HemisAudit.csproj" -o $buildFolder | Out-Host

    if (Test-Path $stdoutLog) { Remove-Item $stdoutLog -Force }
    if (Test-Path $stderrLog) { Remove-Item $stderrLog -Force }

    $dllPath = Join-Path $buildFolder "HemisAudit.dll"
    if (-not (Test-Path $dllPath)) {
        throw "Build completed, but '$dllPath' was not found."
    }

    Write-Host "Starting HemisAudit on $baseUrl" -ForegroundColor Cyan
    $process = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList "`"$dllPath`" --urls $baseUrl" `
        -WorkingDirectory $projectRoot `
        -RedirectStandardOutput $stdoutLog `
        -RedirectStandardError $stderrLog `
        -PassThru

    Set-Content -Path $pidFile -Value $process.Id

    if (-not (Wait-ForUrl -Url $baseUrl -TimeoutSeconds $healthTimeoutSeconds)) {
        $stdout = if (Test-Path $stdoutLog) { Get-Content $stdoutLog -Tail 60 | Out-String } else { "" }
        $stderr = if (Test-Path $stderrLog) { Get-Content $stderrLog -Tail 60 | Out-String } else { "" }
        throw "HemisAudit started but did not respond on $baseUrl within $healthTimeoutSeconds seconds.`nSTDOUT:`n$stdout`nSTDERR:`n$stderr"
    }

    return [pscustomobject]@{
        ProcessId = $process.Id
        Url = $baseUrl
    }
}

$existingProcesses = @(Get-HemisAuditProcessInfo)

if ($existingProcesses.Count -gt 0) {
    foreach ($existing in $existingProcesses) {
        $statusText = if (Test-UrlReady -Url $existing.Url -TimeoutSeconds 5) { "running" } else { "stale" }
        Write-Host "Stopping $statusText HemisAudit process on $($existing.Url) (PID $($existing.ProcessId)) so the latest build is always launched..." -ForegroundColor Yellow
        Stop-Process -Id $existing.ProcessId -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Seconds 2
}

$selectedHost = Get-FreshLoopbackHost
$started = Start-HemisAudit -Port $preferredPort -LoopbackHost $selectedHost
Open-Browser -Url "$($started.Url)$launchPath"
Write-Host "HemisAudit is ready on $($started.Url)" -ForegroundColor Green
