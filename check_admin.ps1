$ErrorActionPreference = "Continue"
$login = Invoke-WebRequest -Uri "http://localhost:5055/Admin/Login" -TimeoutSec 20 -UseBasicParsing -SessionVariable sess
$token = $null
if ($login.Content -match 'name="__RequestVerificationToken"[^>]*value="([^"]+)"') { $token = $matches[1] }
if ($token) {
    if (-not $env:Admin__Password) { throw 'Set Admin__Password locally before running this helper.' }
    Invoke-WebRequest -Uri "http://localhost:5055/Admin/Login" -Method POST -TimeoutSec 20 -UseBasicParsing -WebSession $sess -Body @{password=$env:Admin__Password; "__RequestVerificationToken"=$token} | Out-Null
    $create = Invoke-WebRequest -Uri "http://localhost:5055/Admin/MenuItemCreate" -TimeoutSec 20 -UseBasicParsing -WebSession $sess
    $c = $create.Content
    Write-Host "Status: $($create.StatusCode)"
    Write-Host "Has 'Название' label: $($c.Contains('<label asp-for=" + [char]34 + "Name" + [char]34 + ">Название</label>'))"
    Write-Host "Has bare '>Name<': $($c.Contains('>Name<'))"
} else {
    Write-Host "CSRF token not found"
}
