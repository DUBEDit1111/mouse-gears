# SOP: Mouse Gears

## Goal
Switchable mouse speed profiles for any mouse, from a tray icon and from mouse buttons.

## Inputs
- settings.json (%APPDATA%\Mouse Gears\, or the folder given with --config).
- Mouse button events from a low-level mouse hook (Back = XBUTTON1, Forward = XBUTTON2, Middle).
- The user's own saved Windows mouse settings, read from the registry:
  HKCU\Control Panel\Mouse: MouseSensitivity, MouseThreshold1, MouseThreshold2, MouseSpeed;
  HKCU\Control Panel\Desktop: WheelScrollLines.

## Tool logic
1. Apply = SystemParametersInfo SET for pointer speed, acceleration (SPI_SETMOUSE 6/10/1 on, 0/0/0 off) and scroll lines.
   Never pass SPIF_UPDATEINIFILE: the registry keeps the user's real settings, so a reboot, Quit or Pause always returns them.
   Scroll lines is sent with SPIF_SENDCHANGE only when it actually changes.
2. Applying runs on a worker thread with "latest wins" (pending slot + write lock), so the hook never waits on a settings broadcast.
3. The hook only decides and posts; the action runs on the UI thread via BeginInvoke. A mapped button is swallowed (return 1), an unmapped one passes on.
4. Button actions: pass | next | prev | profile:N (1-based) | holdSlow (first profile while held, previous on release).
5. Pause = saved Windows settings + all buttons pass. Picking a profile un-pauses. Quit = Pause, then exit.
6. Load: missing file -> defaults seeded from the user's saved Windows settings (Normal = theirs, Slow = speed-4, Fast = speed+5, clamped), written. Unreadable file -> copy to settings.json.bak, defaults, tray warning. Out-of-range values clamped (speed 1-20, scroll 1-100, max 9 profiles, unknown action -> pass).
7. Re-apply the active profile after resume from sleep and after unlock.

## Edge cases
- Mapped Back/Forward lose their browser job: default is pass for every button.
- A lost button-up during holdSlow: the next profile change clears the hold.
- Second instance signals the first (named event MouseGears.ShowSettings) to open Settings, then exits.
- Started from inside a packaged (MSIX) app, %APPDATA% may be redirected into that package; launch it normally (Explorer, shortcut) for real use.

## Verify
`execution\selftest.cmd` runs `MouseGears.exe --selftest`: apply/read-back per profile, pause, round trip, broken file, clamping, button routing, a real hook with injected Back/Forward presses, and finally puts the user's live settings back.
