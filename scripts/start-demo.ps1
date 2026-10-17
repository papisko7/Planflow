<#
.SYNOPSIS
  One-command startup of the whole PlanFlow stack for a demo (Windows PowerShell 5.1+ / PowerShell 7).

.DESCRIPTION
  Checks the tools, picks the network path, starts Docker (Postgres, Redis, Mailpit), builds/tests only what changed,
  applies pending DB migrations, starts the backend, opens the tunnels the chosen path needs and finally starts Expo.

  WIFI    (phone and laptop in the same network): no Cloudflare. The app talks to the backend directly over the LAN IP,
          Metro is reached over the LAN IP. ngrok is optional (only needed for the Google Calendar OAuth callback).
  HOTSPOT (laptop on the phone's hotspot / isolated network): Cloudflare tunnel for Metro (8081) + ngrok tunnel for the
          API (5209); the Cloudflare URL is passed to Expo as EXPO_PACKAGER_PROXY_URL.

.PARAMETER Mode          Auto (detect + confirm with a key), Wifi or Hotspot (skips the question).
.PARAMETER SkipTests     Build only, never run backend/mobile tests.
.PARAMETER ForceTests    Run the tests even if nothing changed since the last green run.
.PARAMETER NoMailpit     Do not start the Mailpit container (local mailbox on http://localhost:8025).
.PARAMETER NoNgrokOnWifi Do not start ngrok on the WIFI path.
.PARAMETER SkipFirewall  Do not check/create the Windows Firewall rule on the WIFI path.
#>
[CmdletBinding()]
param(
    [ValidateSet('Auto', 'Wifi', 'Hotspot')] [string]$Mode = 'Auto',
    [switch]$SkipTests,
    [switch]$ForceTests,
    [switch]$NoMailpit,
    [switch]$NoNgrokOnWifi,
    [switch]$SkipFirewall
)

$ErrorActionPreference = 'Continue'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# ----------------------------------------------------------------------------------------------------------------------
# Configuration (local development values only - the same ones you used by hand)
# ----------------------------------------------------------------------------------------------------------------------
$Root       = Split-Path -Parent $PSScriptRoot
$BackendDir = Join-Path $Root 'backend'
$ApiDir     = Join-Path $BackendDir 'src\PlanFlow.Api'
$MobileDir  = Join-Path $Root 'mobile'
$StateDir   = Join-Path $PSScriptRoot '.demo'
$LogDir     = Join-Path $StateDir 'logs'
$StateFile  = Join-Path $StateDir 'state.json'

$ApiPort    = 5209
$MetroPort  = 8081
$DbConn     = 'Host=localhost;Port=55432;Database=planflow;Username=postgres;Password=postgres'
$FallbackNgrokDomain = 'defendant-curry-patriot.ngrok-free.dev'
$FirewallRuleName    = "PlanFlow demo (API $ApiPort + Metro $MetroPort)"

$NgrokCandidates = @(
    "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Ngrok.Ngrok_Microsoft.Winget.Source_8wekyb3d8bbwe\ngrok.exe"
)
$CloudflaredCandidates = @(
    'C:\Program Files (x86)\cloudflared\cloudflared.exe',
    'C:\Program Files\cloudflared\cloudflared.exe'
)

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$script:Started = @()      # processes this script started (stopped again on exit)
$script:Summary = @()      # lines for the final overview
$script:Clock   = [System.Diagnostics.Stopwatch]::StartNew()

# ----------------------------------------------------------------------------------------------------------------------
# Small helpers
# ----------------------------------------------------------------------------------------------------------------------
function Write-Section([string]$Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text)      { Write-Host "  [OK] $Text" -ForegroundColor Green }
function Write-Warn([string]$Text)    { Write-Host "  [!!] $Text" -ForegroundColor Yellow }
function Write-Info([string]$Text)    { Write-Host "       $Text" -ForegroundColor Gray }
function Fail([string]$Text)          { throw [System.InvalidOperationException]::new($Text) }

function Ask-YesNo([string]$Question, [bool]$Default) {
    $hint = if ($Default) { 'T/n' } else { 't/N' }
    Write-Host "  ? $Question [$hint] " -NoNewline -ForegroundColor White
    while ($true) {
        try { $key = [Console]::ReadKey($true) } catch { Write-Host '(brak konsoli - domyślnie)'; return $Default }
        if ($key.Key -eq 'Enter') { Write-Host ''; return $Default }
        if ($key.KeyChar -in 't', 'T', 'y', 'Y') { Write-Host 'tak'; return $true }
        if ($key.KeyChar -in 'n', 'N') { Write-Host 'nie'; return $false }
    }
}

function Test-Command([string]$Name) { [bool](Get-Command $Name -ErrorAction SilentlyContinue) }

function Find-Exe([string[]]$Candidates, [string]$CommandName) {
    foreach ($c in $Candidates) { if (Test-Path -LiteralPath $c) { return $c } }
    $cmd = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Get-ListeningProcess([int]$Port) {
    $conn = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $conn) { return $null }
    return Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
}

function Stop-Tree([int]$ProcessId) {
    & taskkill /PID $ProcessId /T /F 2>&1 | Out-Null
}

# Returns the HTTP status code of the URL, or $null if nothing answered at all (any answer = the server is alive).
function Get-HttpStatus([string]$Url, [string]$Method = 'GET', [string]$Body = $null, [int]$TimeoutSec = 6) {
    try {
        $args = @{ Uri = $Url; Method = $Method; UseBasicParsing = $true; TimeoutSec = $TimeoutSec
                   Headers = @{ 'ngrok-skip-browser-warning' = '1' } }
        if ($Body) { $args.Body = $Body; $args.ContentType = 'application/json' }
        return [int](Invoke-WebRequest @args).StatusCode
    }
    catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        return $null
    }
}

function Wait-Until([scriptblock]$Condition, [int]$TimeoutSec, [scriptblock]$Abort = $null) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) { return $true }
        if ($Abort -and (& $Abort)) { return $false }
        Start-Sleep -Milliseconds 700
    }
    return $false
}

function Show-Tail([string]$Path, [int]$Lines = 15, [string]$Filter = $null) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $text = Get-Content -LiteralPath $Path -Tail 400 -ErrorAction SilentlyContinue
    if ($Filter) { $hit = $text | Where-Object { $_ -match $Filter }; if ($hit) { $text = $hit } }
    $text | Select-Object -Last $Lines | ForEach-Object { Write-Host "       | $_" -ForegroundColor DarkGray }
    Write-Host "       (pełny log: $Path)" -ForegroundColor DarkGray
}

# Runs a command through cmd.exe, captures stdout+stderr in logs\<name>.log, returns the exit code.
# Why not "Start-Process -Wait": it also waits for every child the command leaves behind (MSBuild node, compiler server,
# testhost), so a build that has long finished could keep the script frozen. Here only the command itself is awaited,
# a heartbeat line shows that it is alive and -TimeoutSec kills a command that is really stuck.
function Invoke-Logged([string]$CommandLine, [string]$WorkDir, [string]$LogName, [int]$TimeoutSec = 0, [string]$Label = $null) {
    $log = Join-Path $LogDir "$LogName.log"
    $err = Join-Path $LogDir "$LogName.err.log"
    $p = Start-Process -FilePath 'cmd.exe' -ArgumentList "/c $CommandLine" -WorkingDirectory $WorkDir -NoNewWindow -PassThru `
        -RedirectStandardOutput $log -RedirectStandardError $err
    $null = $p.Handle   # keep the handle so ExitCode stays readable after exit

    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $nextBeat = 20
    $timedOut = $false
    while (-not $p.HasExited) {
        Start-Sleep -Milliseconds 500
        if ($TimeoutSec -gt 0 -and $clock.Elapsed.TotalSeconds -gt $TimeoutSec) { $timedOut = $true; Stop-Tree $p.Id; break }
        if ($Label -and $clock.Elapsed.TotalSeconds -ge $nextBeat) {
            $nextBeat += 20
            $last = try { Get-Content -LiteralPath $log -Tail 1 -ErrorAction Stop } catch { '' }
            if ($last -and $last.Length -gt 70) { $last = $last.Substring(0, 70) + '...' }
            Write-Info ("{0}: trwa {1:n0} s {2}" -f $Label, $clock.Elapsed.TotalSeconds, $(if ($last) { "| $last" } else { '' }))
        }
    }
    if ($timedOut) { Write-Warn "${LogName}: przekroczono limit $TimeoutSec s - polecenie zostało przerwane" }
    $code = if ($timedOut) { 124 } else { $p.ExitCode }

    if (Test-Path $err) { Get-Content $err | Add-Content $log; Remove-Item $err -Force -ErrorAction SilentlyContinue }
    return [pscustomobject]@{ ExitCode = $code; Log = $log; TimedOut = $timedOut }
}

# Leftovers of an earlier (aborted) build/test run keep files locked and can block the next "dotnet test".
# Only test/compiler helpers are stopped - never plain dotnet.exe (the editor's language server and the backend use it).
function Stop-StaleBuildProcesses {
    $stale = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(testhost.*|vstest\.console|VBCSCompiler|MSBuild)$' }
    foreach ($p in $stale) { Write-Warn "kończę wiszący proces $($p.ProcessName) (PID $($p.Id))"; Stop-Tree $p.Id }
    & dotnet build-server shutdown 2>&1 | Out-Null   # releases the MSBuild / Razor / compiler servers of the dotnet CLI
}

# ---- change detection ------------------------------------------------------------------------------------------------
function Get-Fingerprint([string[]]$Paths, [string[]]$Extensions) {
    $files = foreach ($p in $Paths) {
        if (Test-Path -LiteralPath $p -PathType Leaf) { Get-Item -LiteralPath $p }
        elseif (Test-Path -LiteralPath $p) {
            Get-ChildItem -LiteralPath $p -Recurse -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '\\(bin|obj|node_modules|\.expo|dist|build|\.demo)\\' -and $Extensions -contains $_.Extension }
        }
    }
    $text = ($files | Sort-Object FullName | ForEach-Object { '{0}|{1}|{2}' -f $_.FullName, $_.Length, $_.LastWriteTimeUtc.Ticks }) -join "`n"
    $sha = [System.Security.Cryptography.SHA256]::Create()
    return [BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($text))).Replace('-', '')
}

function Get-State {
    if (Test-Path $StateFile) { try { return Get-Content $StateFile -Raw | ConvertFrom-Json } catch { } }
    return [pscustomobject]@{ backend = ''; mobile = '' }
}
function Save-State([string]$Key, [string]$Value) {
    $s = Get-State
    if ($s.PSObject.Properties.Name -notcontains $Key) { $s | Add-Member -NotePropertyName $Key -NotePropertyValue $Value } else { $s.$Key = $Value }
    $s | ConvertTo-Json | Set-Content -Path $StateFile -Encoding UTF8
}

# ---- network detection -----------------------------------------------------------------------------------------------
function Get-NetworkInfo {
    $virtual = 'vEthernet|VirtualBox|VMware|WSL|Hyper-V|Docker|Loopback|Tailscale|ZeroTier|VPN|TAP-|Bluetooth'
    $cfg = Get-NetIPConfiguration -ErrorAction SilentlyContinue |
        Where-Object { $_.IPv4DefaultGateway -and $_.IPv4Address -and $_.NetAdapter.Status -eq 'Up' -and
                       $_.InterfaceAlias -notmatch $virtual -and $_.InterfaceDescription -notmatch $virtual } |
        Sort-Object { $_.NetIPv4Interface.InterfaceMetric } | Select-Object -First 1
    if (-not $cfg) { return $null }

    $ssid = $null
    if ($cfg.InterfaceAlias -match 'Wi-?Fi|WLAN|Wireless') {
        $line = (netsh wlan show interfaces 2>$null) | Where-Object { $_ -match '^\s*SSID\s*:\s*(.+)$' } | Select-Object -First 1
        if ($line -match '^\s*SSID\s*:\s*(.+)$') { $ssid = $Matches[1].Trim() }
    }
    $ip = @($cfg.IPv4Address)[0].IPAddress
    $gw = @($cfg.IPv4DefaultGateway)[0].NextHop

    # Phone hotspots hand out recognisable ranges / carry the phone's name. A guess only - the user confirms with a key.
    $looksLikeHotspot = ($gw -like '172.20.10.*') -or ($gw -like '192.168.43.*') -or
        ($ssid -match 'iPhone|Galaxy|Pixel|Android|Xiaomi|Redmi|POCO|OnePlus|Huawei|Honor|Realme|OPPO|vivo|Motorola|Moto |Nothing|hotspot|Phone')
    return [pscustomobject]@{ Alias = $cfg.InterfaceAlias; Ip = $ip; Gateway = $gw; Ssid = $ssid; LooksLikeHotspot = $looksLikeHotspot }
}

function Select-Path {
    if ($Mode -ne 'Auto') { return $Mode.ToLower() }
    $net = Get-NetworkInfo
    if (-not $net) { Fail 'Brak aktywnego połączenia sieciowego (Wi-Fi/Ethernet z bramą). Połącz się z siecią i uruchom ponownie.' }

    $name = if ($net.Ssid) { "Wi-Fi '$($net.Ssid)'" } else { $net.Alias }
    Write-Info "Wykryto: $name | IP $($net.Ip) | brama $($net.Gateway)"
    $suggest = if ($net.LooksLikeHotspot) { 'hotspot' } else { 'wifi' }
    $label = if ($suggest -eq 'hotspot') { 'HOTSPOT (tunele Cloudflare + ngrok)' } else { 'WIFI (ta sama sieć, bez Cloudflare)' }
    Write-Host "  Proponowana ścieżka: $label" -ForegroundColor White
    Write-Host '  [Enter] zgoda   [W] Wi-Fi / ta sama sieć   [H] hotspot' -ForegroundColor White
    while ($true) {
        try { $key = [Console]::ReadKey($true) } catch { return $suggest }
        if ($key.Key -eq 'Enter') { return $suggest }
        if ($key.KeyChar -in 'w', 'W') { return 'wifi' }
        if ($key.KeyChar -in 'h', 'H') { return 'hotspot' }
    }
}

function Get-LanIp {
    $net = Get-NetworkInfo
    if (-not $net) { Fail 'Nie udało się ustalić adresu IP w sieci lokalnej.' }
    return $net.Ip
}

# ----------------------------------------------------------------------------------------------------------------------
# Steps
# ----------------------------------------------------------------------------------------------------------------------
function Step-Tools {
    Write-Section '1/9 Narzędzia i repozytorium'

    if (-not (Test-Command git)) { Fail 'Brak gita w PATH. Zainstaluj Git for Windows.' }
    $branch = (& git -C $Root rev-parse --abbrev-ref HEAD 2>$null)
    $last   = (& git -C $Root log -1 --format='%h %s' 2>$null)
    $dirty  = @(& git -C $Root status --porcelain 2>$null).Count
    Write-Ok "git: gałąź '$branch', niezatwierdzonych zmian: $dirty"
    if ($last.Length -gt 90) { $last = $last.Substring(0, 90) + '...' }
    Write-Info "ostatni commit: $last"

    if (-not (Test-Command dotnet)) { Fail 'Brak .NET SDK w PATH. Zainstaluj .NET 10 SDK.' }
    $dotnetVersion = (& dotnet --version 2>$null).Trim()
    if ([int]($dotnetVersion.Split('.')[0]) -lt 10) { Fail ".NET SDK $dotnetVersion jest za stary - potrzebny 10.x." }
    Write-Ok ".NET SDK $dotnetVersion"

    if (-not (Test-Command node)) { Fail 'Brak Node.js w PATH. Zainstaluj Node 20+.' }
    $nodeVersion = (& node --version 2>$null).Trim()
    if ([int]($nodeVersion.TrimStart('v').Split('.')[0]) -lt 20) { Fail "Node $nodeVersion jest za stary - potrzebny 20+." }
    Write-Ok "Node $nodeVersion"

    if (-not (Test-Command docker)) { Fail 'Brak Dockera w PATH. Zainstaluj/uruchom Docker Desktop.' }
    & docker info 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warn 'Docker nie działa - uruchamiam Docker Desktop (to chwilę potrwa)...'
        $dd = 'C:\Program Files\Docker\Docker\Docker Desktop.exe'
        if (-not (Test-Path $dd)) { Fail 'Docker Desktop nie jest uruchomiony, a nie znalazłem go w Program Files. Uruchom go ręcznie.' }
        Start-Process $dd | Out-Null
        $up = Wait-Until { & docker info 2>$null | Out-Null; $LASTEXITCODE -eq 0 } 150
        if (-not $up) { Fail 'Docker nie wystartował w 150 s. Otwórz Docker Desktop, poczekaj aż będzie "Running" i uruchom skrypt ponownie.' }
    }
    Write-Ok 'Docker działa'

    # dotnet-ef is needed for the migration check
    & dotnet ef --version 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warn 'Brak narzędzia dotnet-ef - instaluję...'
        & dotnet tool install --global dotnet-ef 2>&1 | Out-Null
        & dotnet ef --version 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { Fail 'Nie udało się zainstalować dotnet-ef (dotnet tool install --global dotnet-ef).' }
    }
    Write-Ok 'dotnet-ef'
}

function Step-Network {
    Write-Section '2/9 Sieć i ścieżka uruchomienia'
    $script:Path = Select-Path

    $script:NgrokExe = Find-Exe $NgrokCandidates 'ngrok'
    $script:CloudflaredExe = Find-Exe $CloudflaredCandidates 'cloudflared'

    # ngrok static domain: taken from mobile\.env so it stays the single source of truth
    $script:NgrokDomain = $FallbackNgrokDomain
    $envFile = Join-Path $MobileDir '.env'
    if (Test-Path $envFile) {
        $m = Select-String -Path $envFile -Pattern '^\s*EXPO_PUBLIC_API_URL\s*=\s*https?://([^/\s]+)' | Select-Object -First 1
        if ($m -and $m.Matches[0].Groups[1].Value -match 'ngrok') { $script:NgrokDomain = $m.Matches[0].Groups[1].Value }
    }

    if ($script:Path -eq 'hotspot') {
        Write-Ok 'Ścieżka: HOTSPOT (Cloudflare dla Metro + ngrok dla API)'
        if (-not $script:CloudflaredExe) { Fail 'Brak cloudflared.exe (C:\Program Files (x86)\cloudflared\). Zainstaluj cloudflared.' }
        if (-not $script:NgrokExe)       { Fail 'Brak ngrok.exe (WinGet: Ngrok.Ngrok). Zainstaluj ngrok.' }
    }
    else {
        Write-Ok 'Ścieżka: WIFI (ta sama sieć, bez Cloudflare)'
        $script:LanIp = Get-LanIp
        Write-Info "adres laptopa w sieci: $($script:LanIp)"
        $script:UseNgrok = (-not $NoNgrokOnWifi) -and [bool]$script:NgrokExe
        if ($script:UseNgrok) {
            Write-Info "ngrok też wystartuje (callback Google Calendar OAuth wraca przez https://$($script:NgrokDomain))"
        }
    }

    if ($script:Path -eq 'hotspot' -or $script:UseNgrok) {
        & $script:NgrokExe config check 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            if ($script:Path -eq 'hotspot') { Fail 'ngrok nie ma poprawnej konfiguracji/authtokena (ngrok config add-authtoken <token>).' }
            Write-Warn 'ngrok nie ma poprawnej konfiguracji - pomijam go na ścieżce WIFI.'
            $script:UseNgrok = $false
        }
    }

    # Google OAuth redirect (user-secrets) must point at the ngrok domain, otherwise "Connect calendar" fails after login
    if ($script:Path -eq 'hotspot' -or $script:UseNgrok) {
        $secret = (& dotnet user-secrets list --project $ApiDir 2>$null) | Where-Object { $_ -match 'GoogleOAuth:RedirectUri' }
        if ($secret -and $secret -notmatch [regex]::Escape($script:NgrokDomain)) {
            Write-Warn "GoogleOAuth:RedirectUri w user-secrets nie wskazuje na $($script:NgrokDomain) - łączenie Google Calendar może się nie udać."
        }
    }
}

function Step-Cleanup {
    Write-Section '3/9 Porządki po poprzednim uruchomieniu'
    $old = Get-Process ngrok, cloudflared -ErrorAction SilentlyContinue
    foreach ($p in $old) { Write-Warn "kończę stary proces $($p.ProcessName) (PID $($p.Id))"; Stop-Tree $p.Id }

    foreach ($port in $ApiPort, $MetroPort) {
        $proc = Get-ListeningProcess $port
        if (-not $proc) { continue }
        if ($proc.ProcessName -in 'node', 'dotnet', 'PlanFlow.Api') {
            if (Ask-YesNo "Port $port zajmuje $($proc.ProcessName) (PID $($proc.Id)) - to pewnie poprzednie uruchomienie. Zakończyć?" $true) {
                Stop-Tree $proc.Id
                Start-Sleep -Seconds 1
            }
            else { Fail "Port $port jest zajęty przez $($proc.ProcessName). Zwolnij go i uruchom ponownie." }
        }
        else { Fail "Port $port zajmuje '$($proc.ProcessName)' (PID $($proc.Id)). Zwolnij go i uruchom ponownie." }
    }
    Write-Ok "porty $ApiPort i $MetroPort wolne"

    if ($script:Path -eq 'wifi' -and -not $SkipFirewall) {
        # The phone has to reach the laptop from outside, so Windows Firewall must let the two ports in.
        if (Get-NetFirewallRule -DisplayName $FirewallRuleName -ErrorAction SilentlyContinue) {
            Write-Ok 'zapora: reguła dla portów demo już istnieje'
        }
        else {
            $create = "New-NetFirewallRule -DisplayName '$FirewallRuleName' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $ApiPort,$MetroPort -Profile Any -RemoteAddress LocalSubnet | Out-Null"
            $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
            if ($isAdmin) { Invoke-Expression $create; Write-Ok 'zapora: dodano regułę dla portów demo' }
            elseif (Ask-YesNo "Telefon nie połączy się bez reguły zapory dla portów $ApiPort/$MetroPort. Dodać ją teraz (okno UAC)?" $true) {
                Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile', '-Command', $create
                if (Get-NetFirewallRule -DisplayName $FirewallRuleName -ErrorAction SilentlyContinue) { Write-Ok 'zapora: dodano regułę dla portów demo' }
                else { Write-Warn 'Reguła nie została dodana - telefon może nie dotrzeć do laptopa.' }
            }
            else { Write-Warn 'Bez reguły zapory telefon może nie dotrzeć do laptopa.' }
        }
    }
}

function Ensure-Container([string]$Name, [string]$RunArgs, [bool]$Required) {
    $state = (& docker inspect -f '{{.State.Running}}' $Name 2>$null)
    if ($LASTEXITCODE -eq 0) {
        if ($state -eq 'true') { Write-Ok "$Name już działa"; return $true }
        & docker start $Name 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) { Write-Ok "$Name uruchomiony"; return $true }
        if ($Required) { Fail "Nie udało się uruchomić kontenera $Name (docker start $Name)." }
        Write-Warn "Nie udało się uruchomić $Name."; return $false
    }
    Write-Warn "Kontenera $Name nie ma - tworzę."
    $log = Join-Path $LogDir "docker-$Name.log"
    $r = Invoke-Logged "docker run -d --name $Name $RunArgs" $Root "docker-$Name"
    if ($r.ExitCode -ne 0) {
        if ($Required) { Show-Tail $r.Log 8; Fail "Nie udało się utworzyć kontenera $Name." }
        Write-Warn "Nie udało się utworzyć $Name (brak internetu do pobrania obrazu?)."; return $false
    }
    Write-Ok "$Name utworzony"
    return $true
}

function Step-Docker {
    Write-Section '4/9 Docker: baza, Redis, poczta'
    Ensure-Container 'planflow-postgres' '-e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=planflow -p 55432:5432 -v planflow-pgdata:/var/lib/postgresql/data postgres:16-alpine' $true | Out-Null
    Ensure-Container 'planflow-redis' '-p 6379:6379 redis:7-alpine' $true | Out-Null

    $pgReady = Wait-Until { & docker exec planflow-postgres pg_isready -U postgres 2>$null | Out-Null; $LASTEXITCODE -eq 0 } 60
    if (-not $pgReady) { Fail 'Postgres nie odpowiada po 60 s (docker logs planflow-postgres).' }
    Write-Ok 'Postgres gotowy (localhost:55432)'

    $redisReady = Wait-Until { (& docker exec planflow-redis redis-cli ping 2>$null) -match 'PONG' } 30
    if (-not $redisReady) { Fail 'Redis nie odpowiada po 30 s (docker logs planflow-redis).' }
    Write-Ok 'Redis gotowy (localhost:6379)'

    if ($NoMailpit) { return }
    if (Get-ListeningProcess 1025) { Write-Ok 'serwer SMTP na :1025 już działa'; $script:Summary += 'Mailpit (podgląd maili): http://localhost:8025'; return }
    $ok = Ensure-Container 'planflow-mailpit' '-p 1025:1025 -p 8025:8025 axllent/mailpit' $false
    if ($ok) { $script:Summary += 'Mailpit (podgląd maili): http://localhost:8025' }
    else { Write-Warn 'Bez Mailpita maile (kody weryfikacyjne) nie dojdą - reszta aplikacji działa normalnie.' }
}

function Step-Backend {
    Write-Section '5/9 Backend: build, testy, migracje'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ConnectionStrings__PlanFlowDb = $DbConn

    $fp = Get-Fingerprint @("$BackendDir\src", "$BackendDir\tests") @('.cs', '.csproj', '.json', '.props', '.targets')
    $changed = ($fp -ne (Get-State).backend)

    Stop-StaleBuildProcesses

    # -nodeReuse:false and UseSharedCompilation=false: no MSBuild node / compiler server stays alive after the build,
    # so nothing is left holding the output pipes or the next run's files.
    $buildFlags = '--nologo -v minimal -nodeReuse:false -p:UseSharedCompilation=false'

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $r = Invoke-Logged "dotnet build src\PlanFlow.Api -c Debug $buildFlags" $BackendDir 'backend-build' 600 'build backendu'
    if ($r.ExitCode -ne 0) {
        Show-Tail $r.Log 15 'error'
        Fail 'Build backendu NIE przeszedł - popraw błędy powyżej. Bez builda backend nie wystartuje.'
    }
    Write-Ok ("build backendu ({0:n0} s)" -f $sw.Elapsed.TotalSeconds)

    if ($SkipTests) { Write-Warn 'testy backendu pominięte (-SkipTests)' }
    elseif (-not $changed -and -not $ForceTests) { Write-Ok 'testy backendu: brak zmian od ostatniego zielonego przebiegu - pomijam' }
    else {
        Write-Info 'zmiany w kodzie backendu - uruchamiam testy (Testcontainers, potrwa chwilę)...'
        $sw.Restart()
        # --blame-hang-timeout: a single test that hangs is aborted (no dump file) instead of freezing the whole run.
        $t = Invoke-Logged "dotnet test tests\PlanFlow.Tests $buildFlags --blame-hang-timeout 10m --blame-hang-dump-type none" $BackendDir 'backend-tests' 1500 'testy backendu'
        $summary = (Get-Content $t.Log -ErrorAction SilentlyContinue | Where-Object { $_ -match '(Passed|Failed)!' } | Select-Object -Last 1)
        if ($t.ExitCode -eq 0) {
            Write-Ok ("testy backendu zielone ({0:n0} s) {1}" -f $sw.Elapsed.TotalSeconds, $summary)
            Save-State 'backend' $fp
        }
        else {
            Show-Tail $t.Log 12 'error|Failed|FAIL'
            Stop-StaleBuildProcesses   # a failed/aborted run may leave testhost behind
            if (-not (Ask-YesNo 'Testy backendu NIE przeszły. Kontynuować mimo to?' $false)) { Fail 'Przerwano: testy backendu nie przeszły.' }
            Write-Warn 'kontynuuję mimo czerwonych testów backendu'
        }
    }

    # Pending migrations -> apply them (the app does not migrate on its own)
    $efCommon = '--project src\PlanFlow.Infrastructure --startup-project src\PlanFlow.Api --no-build'
    $m = Invoke-Logged "dotnet ef migrations list $efCommon" $BackendDir 'backend-migrations'
    if ($m.ExitCode -ne 0) {
        Show-Tail $m.Log 12
        Fail 'Nie udało się sprawdzić migracji bazy (dotnet ef migrations list). Sprawdź połączenie z Postgresem (localhost:55432).'
    }
    $pending = @(Get-Content $m.Log | Where-Object { $_ -match '\(Pending\)' })
    if ($pending.Count -eq 0) { Write-Ok 'baza aktualna - brak oczekujących migracji' }
    else {
        Write-Info "oczekujące migracje: $($pending.Count) - aktualizuję bazę"
        $pending | ForEach-Object { Write-Info ("  + " + ($_ -replace '\s*\(Pending\)', '')) }
        $u = Invoke-Logged "dotnet ef database update $efCommon" $BackendDir 'backend-db-update'
        if ($u.ExitCode -ne 0) { Show-Tail $u.Log 15; Fail 'Aktualizacja bazy (dotnet ef database update) nie powiodła się.' }
        Write-Ok 'baza zaktualizowana'
    }
}

function Step-Mobile {
    Write-Section '6/9 Mobile: zależności, typy, testy'
    $nm = Join-Path $MobileDir 'node_modules\.package-lock.json'
    $lock = Join-Path $MobileDir 'package-lock.json'
    $needInstall = (-not (Test-Path $nm)) -or ((Get-Item $lock).LastWriteTimeUtc -gt (Get-Item $nm).LastWriteTimeUtc)
    if ($needInstall) {
        Write-Info 'brak/nieaktualne node_modules - npm install...'
        $i = Invoke-Logged 'npm install --no-audit --no-fund' $MobileDir 'mobile-npm'
        if ($i.ExitCode -ne 0) { Show-Tail $i.Log 12; Fail 'npm install nie powiódł się.' }
        Write-Ok 'zależności zainstalowane'
    }
    else { Write-Ok 'node_modules aktualne' }

    $fp = Get-Fingerprint @("$MobileDir\src", "$MobileDir\App.tsx", "$MobileDir\index.ts", "$MobileDir\package.json", "$MobileDir\package-lock.json", "$MobileDir\app.json", "$MobileDir\tsconfig.json") @('.ts', '.tsx', '.js', '.json')
    $changed = ($fp -ne (Get-State).mobile)

    if ($SkipTests) { Write-Warn 'sprawdzenie typów i testy mobile pominięte (-SkipTests)'; return }
    if (-not $changed -and -not $ForceTests) { Write-Ok 'mobile: brak zmian od ostatniego zielonego przebiegu - pomijam tsc/testy'; return }

    $ok = $true
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $tsc = Invoke-Logged 'npx tsc --noEmit' $MobileDir 'mobile-tsc' 600 'tsc'
    if ($tsc.ExitCode -eq 0) { Write-Ok ("TypeScript bez błędów ({0:n0} s)" -f $sw.Elapsed.TotalSeconds) }
    else { $ok = $false; Show-Tail $tsc.Log 12 'error'; Write-Warn 'TypeScript zgłasza błędy' }

    $sw.Restart()
    $jest = Invoke-Logged 'npx jest --silent' $MobileDir 'mobile-jest' 600 'testy mobile'
    $line = (Get-Content $jest.Log -ErrorAction SilentlyContinue | Where-Object { $_ -match '^Tests:' } | Select-Object -Last 1)
    if ($jest.ExitCode -eq 0) { Write-Ok ("testy mobile zielone ({0:n0} s) {1}" -f $sw.Elapsed.TotalSeconds, $line) }
    else { $ok = $false; Show-Tail $jest.Log 14 'FAIL|●|Tests:'; Write-Warn 'testy mobile nie przeszły' }

    if ($ok) { Save-State 'mobile' $fp }
    elseif (-not (Ask-YesNo 'Kontrola mobile NIE przeszła. Kontynuować mimo to?' $false)) { Fail 'Przerwano: kontrola mobile nie przeszła.' }
}

function Step-StartBackend {
    Write-Section '7/9 Start backendu'
    $backendLog = Join-Path $LogDir 'backend-run.log'
    if (Test-Path $backendLog) { Remove-Item $backendLog -Force -ErrorAction SilentlyContinue }

    $cmd = @"
`$Host.UI.RawUI.WindowTitle = 'PlanFlow backend :$ApiPort'
Set-Location '$ApiDir'
dotnet run --no-build --no-launch-profile --urls 'http://0.0.0.0:$ApiPort' 2>&1 | Tee-Object -FilePath '$backendLog'
"@
    $enc = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($cmd))
    $proc = Start-Process powershell -ArgumentList '-NoProfile', '-NoExit', '-EncodedCommand', $enc -PassThru
    $script:Started += $proc
    Write-Info 'backend działa w osobnym oknie "PlanFlow backend" (tam widać jego logi)'

    $up = Wait-Until { $null -ne (Get-HttpStatus "http://localhost:$ApiPort/swagger/index.html" 'GET' $null 3) } 90 { $proc.HasExited }
    if (-not $up) {
        Show-Tail $backendLog 20 'fail|error|exception|crit'
        Fail 'Backend nie wystartował (szczegóły wyżej / w oknie backendu).'
    }
    Write-Ok "backend odpowiada na http://localhost:$ApiPort"

    # Smoke test: a login with a non-existent user must be answered with 4xx - a 5xx means the DB/migrations are broken.
    $status = Get-HttpStatus "http://localhost:$ApiPort/api/auth/login" 'POST' '{"email":"smoke@planflow.invalid","password":"x"}' 10
    if ($null -eq $status -or $status -ge 500) {
        Show-Tail $backendLog 20 'fail|error|exception|crit'
        Fail "Backend żyje, ale zapytanie do bazy zwróciło $status - sprawdź połączenie z bazą/migracje (log wyżej)."
    }
    Write-Ok "backend + baza działają (test zapytania: HTTP $status)"
}

function Start-Ngrok([bool]$Required) {
    $log = Join-Path $LogDir 'ngrok.log'
    $err = Join-Path $LogDir 'ngrok.err.log'
    $proc = Start-Process $script:NgrokExe -ArgumentList "http --url=$($script:NgrokDomain) $ApiPort --log=stdout --log-format=logfmt" `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError $err
    $script:Started += $proc

    $ready = Wait-Until {
        try {
            $t = Invoke-RestMethod 'http://127.0.0.1:4040/api/tunnels' -TimeoutSec 2
            [bool]($t.tunnels | Where-Object { $_.public_url -like "*$($script:NgrokDomain)*" })
        } catch { $false }
    } 30 { $proc.HasExited }

    if (-not $ready) {
        Show-Tail $log 8 'ERR|err|lvl=eror'
        Show-Tail $err 5
        if ($Required) { Fail 'ngrok nie wystartował (najczęściej: błędny authtoken albo domena zajęta przez inną sesję).' }
        Write-Warn 'ngrok nie wystartował - callback Google Calendar nie zadziała, reszta tak.'
        return $false
    }
    $code = Get-HttpStatus "https://$($script:NgrokDomain)/swagger/index.html" 'GET' $null 10
    if ($null -eq $code -or $code -ge 500) {
        if ($Required) { Fail "Tunel ngrok jest, ale backend przez niego nie odpowiada (HTTP $code)." }
        Write-Warn "Tunel ngrok jest, ale backend przez niego nie odpowiada (HTTP $code)."; return $false
    }
    Write-Ok "ngrok: https://$($script:NgrokDomain) -> localhost:$ApiPort (HTTP $code)"
    return $true
}

function Start-Cloudflare {
    $log = Join-Path $LogDir 'cloudflared.log'
    $err = Join-Path $LogDir 'cloudflared.err.log'
    foreach ($f in $log, $err) { if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue } }
    $proc = Start-Process $script:CloudflaredExe -ArgumentList "tunnel --url http://localhost:$MetroPort --no-autoupdate" `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError $err
    $script:Started += $proc

    $pattern = 'https://[a-z0-9-]+\.trycloudflare\.com'
    $script:CfUrl = $null
    $found = Wait-Until {
        foreach ($f in $log, $err) {
            if (Test-Path $f) {
                $m = Select-String -Path $f -Pattern $pattern -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($m) { $script:CfUrl = $m.Matches[0].Value; return $true }
            }
        }
        return $false
    } 45 { $proc.HasExited }

    if (-not $found) {
        Show-Tail $err 10
        Fail 'Cloudflare nie podał adresu tunelu (brak internetu na hotspocie? limit trycloudflare?).'
    }
    $host_ = ([Uri]$script:CfUrl).Host
    # The phone resolves the name with its own DNS, so ask a public resolver (the laptop's hotspot DNS often lags on new names).
    $dns = Wait-Until { [bool](Resolve-DnsName $host_ -Server 1.1.1.1 -DnsOnly -ErrorAction SilentlyContinue) } 30
    if ($dns) { Write-Ok "cloudflare: $($script:CfUrl) -> localhost:$MetroPort" }
    else { Write-Warn "cloudflare: $($script:CfUrl) - adres jeszcze się nie propaguje; poczekaj chwilę przed skanowaniem QR." }
}

function Step-Tunnels {
    Write-Section '8/9 Tunele'
    if ($script:Path -eq 'hotspot') {
        Start-Cloudflare
        Start-Ngrok $true | Out-Null
        $env:EXPO_PACKAGER_PROXY_URL = $script:CfUrl
        $env:EXPO_PUBLIC_API_URL = "https://$($script:NgrokDomain)"
        Remove-Item Env:REACT_NATIVE_PACKAGER_HOSTNAME -ErrorAction SilentlyContinue
        $script:Summary += "Metro (Expo):  $($script:CfUrl)"
        $script:Summary += "API aplikacji: $($env:EXPO_PUBLIC_API_URL)"
    }
    else {
        if ($script:UseNgrok) { Start-Ngrok $false | Out-Null } else { Write-Info 'ngrok pominięty' }
        Remove-Item Env:EXPO_PACKAGER_PROXY_URL -ErrorAction SilentlyContinue
        $env:REACT_NATIVE_PACKAGER_HOSTNAME = $script:LanIp
        $env:EXPO_PUBLIC_API_URL = "http://$($script:LanIp):$ApiPort"
        $code = Get-HttpStatus "$($env:EXPO_PUBLIC_API_URL)/swagger/index.html" 'GET' $null 6
        if ($null -eq $code) { Write-Warn "API pod $($env:EXPO_PUBLIC_API_URL) nie odpowiada z tego komputera - sprawdź adres IP." }
        else { Write-Ok "API w sieci lokalnej: $($env:EXPO_PUBLIC_API_URL) (HTTP $code)" }
        $script:Summary += "Metro (Expo):  exp://$($script:LanIp):$MetroPort (LAN)"
        $script:Summary += "API aplikacji: $($env:EXPO_PUBLIC_API_URL)"
        $script:Summary += "Test z telefonu: otwórz w przeglądarce telefonu $($env:EXPO_PUBLIC_API_URL)/swagger - jeśli się ładuje, aplikacja też się połączy."
    }
}

function Step-Expo {
    Write-Section '9/9 Expo'
    Remove-Item Env:CI -ErrorAction SilentlyContinue   # CI=1 would turn off Metro's interactive mode
    Write-Host ''
    Write-Host "  GOTOWE po $([int]$script:Clock.Elapsed.TotalSeconds) s. Podsumowanie:" -ForegroundColor Green
    Write-Host "    ścieżka:       $($script:Path.ToUpper())" -ForegroundColor White
    foreach ($l in $script:Summary) { Write-Host "    $l" -ForegroundColor White }
    Write-Host "    logi skryptu:  $LogDir" -ForegroundColor Gray
    Write-Host '    Ctrl+C w tym oknie zatrzymuje Expo, backend i tunele (Docker zostaje włączony).' -ForegroundColor Gray
    Write-Host ''
    Push-Location $MobileDir
    try { & npx.cmd expo start -c } finally { Pop-Location }
}

# ----------------------------------------------------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------------------------------------------------
$exitCode = 0
try {
    Write-Host ''
    Write-Host '  PlanFlow - start dema' -ForegroundColor Cyan
    Step-Tools
    Step-Network
    Step-Cleanup
    Step-Docker
    Step-Backend
    Step-Mobile
    Step-StartBackend
    Step-Tunnels
    Step-Expo
}
catch {
    $exitCode = 1
    Write-Host ''
    Write-Host "  [XX] $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "       Logi: $LogDir" -ForegroundColor DarkGray
}
finally {
    foreach ($p in $script:Started) { if ($p -and -not $p.HasExited) { Stop-Tree $p.Id } }
    if ($script:Started.Count -gt 0) { Write-Host '  Zatrzymano backend i tunele. Kontenery Docker zostają włączone.' -ForegroundColor Gray }
}

if ($exitCode -ne 0) {
    Write-Host '  Naciśnij dowolny klawisz, aby zamknąć...' -ForegroundColor Gray
    try { [void][Console]::ReadKey($true) } catch { }
}
exit $exitCode
