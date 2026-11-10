# Mizon state probe for WinUI memory measurements

Small external console client using the official ModelContextProtocol.Core SDK.
It talks to the installed Mizon companion over MCP stdio and allows only:

| Command | Existing tool | Required in-app grant |
| --- | --- | --- |
| getconnection | mizon_get_connection | Own connection status; no private app data |
| getauth | mizon_get_auth_state | Authentication read |
| openlogin | mizon_open_auth_page, login | Authentication control |
| openwelcome | mizon_open_auth_page, welcome | Authentication control |

The configured profile GUID is **required**. No machine identity, credential,
pairing proof, stored grant or session file is included in this project. Supply a
profile from your own configured Mizon client launcher; the companion loads its
protected identity internally. The public profile identifier itself grants nothing.
Initial consent, new scopes and revocation are controlled only by the user inside
Mizon. The probe cannot approve itself or read a credential/grant store.

Build:

```powershell
dotnet build Tools/RuntimeUpgradeBenchmarks/WinUiMemory/McpProbe -c Release
```

Resolve the companion using your Mizon ClientSetup resolver or the installed
package's supported MCP executable. For example, when ClientSetup is installed:

```powershell
$mizonMcpRoot = Join-Path $env:LOCALAPPDATA 'Mizon\Mcp'
$mizonHost = & powershell.exe -NoLogo -NoProfile -NonInteractive -File (Join-Path $mizonMcpRoot 'Resolve-MizonMcp.ps1')
$mizonProfile = '<your configured client profile GUID>'
dotnet Tools/RuntimeUpgradeBenchmarks/WinUiMemory/McpProbe/bin/Release/net11.0/McpProbe.dll $mizonHost --client-profile $mizonProfile getconnection getauth
```

Run page-changing commands only when that measurement/navigation is explicitly
authorized. They may launch Mizon if it is closed through the existing authentication
tool behavior; reading connection/auth state does not launch it. Existing app
guards still decide whether a signed-in session can open an authentication page.
The probe never signs in/out, fills/submits forms, starts playback or changes settings.

Calls are sequential. Nonzero exit codes distinguish tool/business failure (1),
invalid command/profile (2), transport failure (3), cancellation/timeout (130).
Any structured `success:false` stops the sequence even if the protocol's `IsError`
flag is false. Missing/malformed structured success also fails closed. Page changes
are never automatically retried. The MCP client and companion transport are disposed
before the process exits; the cancellation handler is removed.

For measurements, wait for this process to exit and then let the target page settle.
The probe's memory is not part of Mizon's process-private working set. Use the same
page/state/window/backend/cache conditions for both runtimes. MCP state alone is not
a visual-layout, animation or frame-rate test.

Tool output can contain account display fields or connection identifiers. Keep raw
outputs local; publish only sanitized measurement summaries. The project adds no
public endpoint and does not bypass the app's scoped permission boundary.
