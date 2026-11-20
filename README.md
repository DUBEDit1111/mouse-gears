# Mouse Gears

Speed profiles for any mouse, in the Windows system tray. Free and open source (MIT).

- Switch between pointer-speed profiles (Slow / Normal / Fast, or up to 9 of your own).
- Each profile sets pointer speed, acceleration and scroll lines.
- Left-click the tray gear to step to the next profile. The gear shows the profile number.
- Set your mouse's Back, Forward or Middle button to: next profile, previous profile, a chosen profile, or "hold for slow" (slowest profile only while you hold it, good for precise work).
- Works with any mouse, because it changes Windows' own mouse settings, not the mouse hardware.
- One 50 KB file. No installer, no admin rights, no internet, no tracking.

## Use
1. Unzip anywhere and run `MouseGears.exe`. A gold gear appears in the tray (it may sit under the ^ arrow; drag it onto the taskbar to keep it visible).
2. Right-click the gear for Settings, Pause, Start with Windows and Quit. Running `MouseGears.exe` again also opens Settings.
3. The first run copies your current Windows mouse settings into "Normal".

Your saved Windows mouse settings are never overwritten. Pause or Quit puts them straight back, and so does a restart.

Mouse DPI buttons on gaming mice keep working as before: they are handled inside the mouse.

Settings live in `%APPDATA%\Mouse Gears\settings.json`.

## Windows warnings
The exe is not code-signed, so Windows SmartScreen may say "Windows protected your PC" the first time: click More info, then Run anyway. Some antivirus tools are wary of any app that watches mouse buttons; the full source is here to read.

## Build it yourself
Needs nothing but Windows 10 or 11 (it uses the C# compiler built into Windows):

    execution\build.cmd      builds dist\MouseGears.exe
    execution\selftest.cmd   runs the self-test (your live settings are put back after)

The whole app is `src/MouseGears.cs`.
