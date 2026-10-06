# Helpers to drive the running game: real key presses, mouse clicks, desktop screenshots.
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Drive {
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public struct RECT { public int L, T, R, B; }
    public struct POINT { public int X, Y; }
    public static void Down(byte vk) { keybd_event(vk, (byte)MapVirtualKey(vk, 0), 0, UIntPtr.Zero); }
    public static void Up(byte vk) { keybd_event(vk, (byte)MapVirtualKey(vk, 0), 2, UIntPtr.Zero); }
    public static void Click() { mouse_event(2, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
}
'@
[void][Drive]::SetProcessDPIAware()

# BepInEx's console owns MainWindowHandle, so look the game window up by its Unity class.
function Get-Game { $h = [Drive]::FindWindow('UnityWndClass', 'The Loopler'); if ($h -ne [IntPtr]::Zero) { [pscustomobject]@{ MainWindowHandle = $h } } }
function Is-Focused { $p = Get-Game; $p -and ([Drive]::GetForegroundWindow() -eq $p.MainWindowHandle) }
# Windows refuses SetForegroundWindow from a background process unless it just received input; a tap on Alt satisfies that.
function Focus-Game { $p = Get-Game; if (-not $p) { return $false }; if (-not (Is-Focused)) { [Drive]::Down(0x12); [Drive]::Up(0x12); [void][Drive]::SetForegroundWindow($p.MainWindowHandle); Start-Sleep -Milliseconds 500 }; Is-Focused }
# Never send input unless the game owns the foreground, it would land in whatever window does.
function Assert-Focus { if (-not (Focus-Game)) { throw 'Game window is not in the foreground, not sending input.' } }
function Press([byte]$vk, [int]$holdMs = 80) { Assert-Focus; [Drive]::Down($vk); Start-Sleep -Milliseconds $holdMs; [Drive]::Up($vk); Start-Sleep -Milliseconds 150 }
function Client-Origin { $p = Get-Game; $pt = New-Object Drive+POINT; [void][Drive]::ClientToScreen($p.MainWindowHandle, [ref]$pt); $r = New-Object Drive+RECT; [void][Drive]::GetClientRect($p.MainWindowHandle, [ref]$r); [pscustomobject]@{ X = $pt.X; Y = $pt.Y; W = $r.R; H = $r.B } }
function Click-Client([int]$x, [int]$y) { Assert-Focus; $o = Client-Origin; [void][Drive]::SetCursorPos($o.X + $x, $o.Y + $y); Start-Sleep -Milliseconds 250; [Drive]::Click(); Start-Sleep -Milliseconds 250 }
function Shot([string]$path) { $o = Client-Origin; $bmp = New-Object System.Drawing.Bitmap $o.W, $o.H; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($o.X, $o.Y, 0, 0, $bmp.Size); $bmp.Save($path); $g.Dispose(); $bmp.Dispose(); "$path $($o.W)x$($o.H)" }
function Status([int]$n = 40) { Get-Content 'G:\Steam\steamapps\common\The Loopler\BepInEx\LogOutput.log' | Select-String 'Car Loader' | Select-Object -Last $n }

$VK = @{ F1 = 0x70; F2 = 0x71; F9 = 0x78; F10 = 0x79; W = 0x57; S = 0x53; SPACE = 0x20; CTRL = 0x11; SHIFT = 0x10; E = 0x45; Q = 0x51; C = 0x43; ESC = 0x1B }
