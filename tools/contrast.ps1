param([string]$Repo = (Split-Path -Parent $PSScriptRoot), [switch]$OnlyFailures)
# Reads the colour tokens of both themes from site.css (and the category colours from CategoryStyle.cs) and
# prints WCAG contrast ratios for the pairs the UI actually uses. Text needs 4.5, icons/bars/shapes need 3.
# Run from the repository: powershell -ExecutionPolicy Bypass -File tools/contrast.ps1 [-OnlyFailures]
# Exits with 1 when a pair is below its minimum.

$css = [System.IO.File]::ReadAllText("$Repo\src\BudgetApp\wwwroot\css\site.css")

function Get-Block([string]$pattern) {
    $m = [regex]::Match($css, $pattern, 'Singleline')
    $tokens = @{}
    foreach ($t in [regex]::Matches($m.Groups[1].Value, '--([a-z0-9-]+)\s*:\s*([^;]+);')) { $tokens[$t.Groups[1].Value] = $t.Groups[2].Value.Trim() }
    $tokens
}

$light = Get-Block '/\* ---------- Tokens: light theme.*?:root\s*\{(.*?)\n\}'
$darkOnly = Get-Block '/\* ---------- Tokens: dark theme.*?\[data-bs-theme="dark"\]\s*\{(.*?)\n\}'
$dark = @{}; foreach ($k in $light.Keys) { $dark[$k] = $light[$k] }; foreach ($k in $darkOnly.Keys) { $dark[$k] = $darkOnly[$k] }

function Resolve-Token($tokens, [string]$value) {
    $guard = 0
    while ($value -match '^var\(--([a-z0-9-]+)\)$' -and $guard -lt 10) { $value = $tokens[$Matches[1]]; $guard++ }
    $value
}
function Hex($tokens, [string]$nameOrHex) {
    if ($nameOrHex.StartsWith('#')) { return $nameOrHex }
    Resolve-Token $tokens $tokens[$nameOrHex]
}
function Rgb([string]$hex) { $h = $hex.TrimStart('#'); @([Convert]::ToInt32($h.Substring(0,2),16), [Convert]::ToInt32($h.Substring(2,2),16), [Convert]::ToInt32($h.Substring(4,2),16)) }
function Lum([string]$hex) {
    $c = Rgb $hex | ForEach-Object { $v = $_ / 255.0; if ($v -le 0.03928) { $v / 12.92 } else { [Math]::Pow(($v + 0.055) / 1.055, 2.4) } }
    0.2126 * $c[0] + 0.7152 * $c[1] + 0.0722 * $c[2]
}
function Ratio([string]$a, [string]$b) { $l1 = Lum $a; $l2 = Lum $b; if ($l1 -lt $l2) { $l1, $l2 = $l2, $l1 }; ($l1 + 0.05) / ($l2 + 0.05) }
# color-mix(in srgb, A p%, B)
function Mix([string]$a, [double]$percent, [string]$b) {
    $ra = Rgb $a; $rb = Rgb $b; $p = $percent / 100.0
    '#' + ((0..2 | ForEach-Object { [int][Math]::Round($ra[$_] * $p + $rb[$_] * (1 - $p)) } | ForEach-Object { $_.ToString('X2') }) -join '')
}

$script:failures = 0
function Check($theme, $tokens, [string]$what, [string]$fg, [string]$bg, [double]$min) {
    $f = Hex $tokens $fg; $b = Hex $tokens $bg
    $r = Ratio $f $b
    $ok = $r -ge $min
    if (-not $ok) { $script:failures++ }
    if (-not $OnlyFailures -or -not $ok) {
        '{0,-5} {1,-46} {2} on {3}  {4,5:N2}  (min {5})  {6}' -f $theme, $what, $f, $b, $r, $min, $(if ($ok) { 'ok' } else { 'FAIL' })
    }
}

$pairs = @(
    @('body text on page', 'text', 'bg', 4.5), @('body text on card', 'text', 'surface', 4.5), @('body text on raised area', 'text', 'surface-2', 4.5),
    @('muted text on page', 'muted', 'bg', 4.5), @('muted text on card', 'muted', 'surface', 4.5), @('muted text on raised area', 'muted', 'surface-2', 4.5),
    @('link / income on card', 'brand-ink', 'surface', 4.5), @('link / income on page', 'brand-ink', 'bg', 4.5),
    @('link hover on card', 'brand-ink-strong', 'surface', 4.5),
    @('active menu item, icon on green tint', 'brand-ink', 'brand-soft', 4.5),
    @('expense amount on card', 'expense-ink', 'surface', 4.5), @('expense text on page', 'expense-ink', 'bg', 4.5),
    @('expense icon on red tint', 'expense-ink', 'expense-soft', 3),
    @('gold text on gold tint (badge, tile)', 'gold-ink', 'gold-soft', 4.5), @('gold percent on card', 'gold-ink', 'surface', 4.5),
    @('warning text', 'gold-soft-ink', 'gold-soft', 4.5), @('warning icon', 'gold-icon', 'gold-soft', 3),
    @('success text', 'brand-soft-ink', 'brand-soft', 4.5), @('error text', 'expense-soft-ink', 'expense-soft', 4.5),
    @('carry-over card text', 'text', 'gold-soft', 4.5), @('carry-over card text (gradient end)', 'text', 'gold-soft-2', 4.5),
    @('carry-over card small note', 'muted', 'gold-soft', 4.5),
    @('celebration amount', 'brand-ink-strong', 'gold-soft', 4.5),
    @('white on primary button', '#FFFFFF', 'brand', 4.5), @('white on primary button (hover)', '#FFFFFF', 'brand-hover', 4.5),
    @('white on danger button', '#FFFFFF', 'expense', 4.5),
    @('primary button against card', 'brand', 'surface', 3),
    @('budget bar (ok) on track', 'bar-ok', 'surface-2', 3), @('budget bar (near) on track', 'bar-near', 'surface-2', 3), @('budget bar (over) on track', 'bar-over', 'surface-2', 3),
    @('goal bar (list) on green tint track', 'bar-near', 'brand-soft', 3),
    @('icon on gold tile (carry-over, trophy)', 'on-gold', 'gold', 3),
    @('icon on gold tile (trophy, light end)', 'on-gold', 'gold-light', 3),
    @('allowance card: amount (within share)', 'brand-ink', 'surface', 4.5),
    @('allowance card: amount (over today)', 'gold-ink', 'gold-soft', 4.5),
    @('allowance card: text and small print (over today)', 'gold-soft-ink', 'gold-soft', 4.5),
    @('allowance card: amount, text, small print (deficit)', 'expense-soft-ink', 'expense-soft', 4.5),
    @('allowance card: bar on track', 'bar-ok', 'surface-2', 3),
    @('allowance card: hint icon on card', 'gold-icon', 'surface', 3),
    @('checkbox / button outline on card', 'line-strong', 'surface', 1.2)
)

foreach ($themeName in 'light', 'dark') {
    $tokens = if ($themeName -eq 'light') { $light } else { $dark }
    foreach ($p in $pairs) { Check $themeName $tokens $p[0] $p[1] $p[2] $p[3] }

    # goal panel: white text on every stop of the gradient
    if ($tokens['hero-grad']) { foreach ($stop in [regex]::Matches($tokens['hero-grad'], '#[0-9A-Fa-f]{6}')) { Check $themeName $tokens 'white on goal panel' '#FFFFFF' $stop.Value 4.5 } }

    # categories: raw colour on the card (dots, chart slices) and the badge label on the badge background
    $src = [System.IO.File]::ReadAllText("$Repo\src\BudgetApp\Models\CategoryStyle.cs")
    $surface = Hex $tokens 'surface'
    $bgBase = Hex $tokens 'cat-bg-base'; $inkBase = Hex $tokens 'cat-ink-base'
    $bgMix = [double]($tokens['cat-bg-mix'].TrimEnd('%')); $inkMix = [double]($tokens['cat-ink-mix'].TrimEnd('%'))
    $iconBgMix = [double]($tokens['cat-icon-bg-mix'].TrimEnd('%')); $iconInkMix = [double]($tokens['cat-icon-ink-mix'].TrimEnd('%'))
    foreach ($c in [regex]::Matches($src, '\["([^"]+)"\]\s*=\s*new\("[^"]+",\s*"(#[0-9A-Fa-f]{6})"(?:,\s*"(#[0-9A-Fa-f]{6})")?')) {
        $name = $c.Groups[1].Value
        $colour = if ($themeName -eq 'dark' -and $c.Groups[3].Success) { $c.Groups[3].Value } else { $c.Groups[2].Value }
        Check $themeName $tokens "category colour: $name" $colour $surface 3
        Check $themeName $tokens "category badge: $name" (Mix $colour $inkMix $inkBase) (Mix $colour $bgMix $bgBase) 4.5
        Check $themeName $tokens "category icon: $name" (Mix $colour $iconInkMix $inkBase) (Mix $colour $iconBgMix $bgBase) 3
    }
}

"failures: $script:failures"
if ($script:failures -gt 0) { exit 1 }
