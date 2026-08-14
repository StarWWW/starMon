# StarMon — how it is built

About 43,000 lines of C# targeting .NET Framework 4.8, in one non-SDK-style project, with no package references at all. Everything it needs it either carries or binds by hand.

This document is for whoever changes it next. It explains the layers, the rules each one lives by, and the handful of decisions that are load-bearing — the ones where doing the obvious thing produces an application that appears to work and quietly drives the wrong hardware.

If you are looking for what StarMon *does*, that is the [README](../README.md).

---

## 1. The four things it talks to

Everything in this application is one of four conversations, and they have almost nothing in common with each other.

| | What it is | Reached through | Costs |
|---|---|---|---|
| **Embedded Controller** | A microcontroller on the board that owns the fans, the temperature probes and the keyboard backlight | Two I/O ports, `0x62` and `0x66`, behind a kernel driver and a machine-wide mutex | A handful of port exchanges per register, contended with the firmware itself |
| **WMI BIOS interface** | HP's own firmware calls: performance profiles, the fan table, graphics power, backlight colour | `ACPI\PNP0C14`, through `Microsoft.Management.Infrastructure` | A WMI round trip — an order of magnitude slower than a register |
| **The graphics card** | Temperature, load, clocks, power, video memory | NVAPI and NVML, bound by hand through function ids | A call into the display driver's user-mode half |
| **Windows** | Battery, memory, disk, network, power plan, brightness, thermal zones | Ordinary Win32 and WMI | Varies; the slow ones are cached |

The first two are the reason this application exists and the reason it is dangerous. The register map in `Hardware/EcData.cs` was transcribed from **one board's ACPI tables**. On a different board the same addresses mean something else, and the firmware accepts the write without reporting an error. That is not hypothetical — see §6.

---

## 2. Layers

Bottom to top. Each layer may call downward and must not call upward.

```
  Ui/            WPF: views, view models, the tray icon and menu
  App/Gui/       The tray context — owns the heartbeat and the window
  App/Service/   Policy: what to read, when, and what to do about it
  App/Cli/       The command line, sharing everything below
  Hardware/      What this machine is and what it can do
  Driver/        Getting into ring 0 at all
  External/      P/Invoke declarations, and nothing else
  Library/       Configuration, logging, locale, the OS, the hardware facade
```

### `External/` — declarations only

One file per DLL. No logic, no policy, no error handling beyond marshalling. If something here does more than describe a native signature, it is in the wrong place.

### `Driver/` — ring 0

Two drivers, one facade.

- **`Ring0.cs`** — WinRing0 1.2.0.5, extracted from an embedded resource and installed as a kernel service. Raw port, MSR, PCI and memory access. On Microsoft's vulnerable-driver list, and does not load where that list is enforced — which is the default on Windows 11.
- **`PawnIo.cs`** — the signed alternative. Loads verified programs into the kernel rather than handing out raw access; the one used for the controller permits ports `0x62` and `0x66` and refuses everything else. The modules are carried as embedded resources and the driver verifies each one.
- **`LowLevel.cs`** — the only thing above this layer that knows which of the two answered.

`LowLevel` keeps two questions apart that used to be one:

```csharp
LowLevel.IsOpen    // a driver loaded
LowLevel.HasMsr    // the processor registers are readable
LowLevel.HasSmn    // AMD's System Management Network is readable
```

They are not the same question. PawnIO can be open with no processor module — the vendor modules decide for themselves whether this is their processor — so a machine can have a working controller and no MSRs. Every caller was already guarding on the first and meaning the second.

### `Hardware/` — what this machine is

The largest layer, and the one with the most rules.

**`Ec.cs`** implements the controller's wait-and-retry protocol. Two things in it are not decoration:

- The wait budget is a **span of time**, not a count of iterations. `Thread.Sleep(1)` sleeps until the next scheduler tick — about 15 ms — so counting iterations and sleeping between them produces a real limit fifteen times longer than it reads as. The loop escalates from spinning to yielding and never sleeps.
- Failed waits are counted **per register**, not per controller. A shared counter let one register's failures vouch for another's: six absent probes fail three reads each in a single pass, eighteen in a row, and from that point a fail-open bypass is open for every other register in the application. A fan tachometer then reports whatever byte happens to be in the port.

**`Platform.cs`** is the assembled machine — sensors, fans, the system component — built from what the board actually reports rather than from constants.

**`DeviceProfile.cs`** is what was worked out about this board at startup: how many fans, the fan-level ceiling and where it came from, which fan modes exist, how many keyboard zones. Every value carries its provenance as text, because "56" and "56, because the firmware's fan table said so" are different claims and only one of them can be argued with.

**`Identity.cs`** is the gate (§6).

**`Absent.cs`** holds the stand-ins. When the controller or the firmware interface cannot be reached, a stand-in is installed rather than the application exiting: reads report failure, writes are dropped, the lock is granted. Nothing above has to test for it, because a refusal from a stand-in is the same refusal a partially-implemented firmware produces.

**`CodeIntegrity.cs`** examines *why* a driver would not load — elevation, memory integrity, the blocklist, secure boot, whether PawnIO is installed — and says so in a sentence. It examines silently; whoever knows there is a problem does the telling.

### `App/Service/` — policy

Deliberately given no reference back to the application, so it cannot start doing anything but decide.

- **`Poller.cs`** gathers one `Reading` from everything, on a background thread, one at a time. Slow-moving values are refreshed one tick in five.
- **`Maintainer.cs`** carries out the periodic hardware *work* — the fan program, the guard, the sticky settings — on a worker of its own.
- **`Ticker.cs`** is one periodic slot. `Rewind()` and `Due()` are two passes on purpose; folding them together loses the property that a slot nobody asked stays ready.
- **`ThermalGuard.cs`** decides engage / release / panic with hysteresis. It decides; carrying it out belongs to the caller.
- **`FanControl.cs`** turns a request (Automatic, Constant, Maximum, Off, Program) into an ordered sequence of hardware operations. Order matters as much as content.
- **`Reading.cs`** is the snapshot, and it carries **when it was taken** (§5).

### `Ui/` — WPF

`Ui/Windows/WindowController.cs` is the seam: it applies a `Reading` to the view models and turns property changes back into hardware requests. `IsApplyingReading` is what stops those two being the same event — without it, writing a reading into the view model raises the same notification a user's click does, and the window answers the hardware by asking it for the state it just reported, once a second, forever.

---

## 3. The life of a reading

```
  Ticker says the monitoring slot is due
      → Poller.Request()            drops if one is already in flight
      → Poller.Gather()             on a thread-pool thread
            stamps Reading.TakenAt  before asking anything
            Platform.UpdateTemperature(), UpdateFans()
            Hw.EcExec(...)          takes the machine-wide EC mutex
            LowLevel.ReadIoPort()   → PawnIO or WinRing0
            firmware, NVAPI, Windows
      → Poller.Read event           still on the background thread
      → WindowController.Apply      marshalled to the dispatcher
            IsApplyingReading = true
            view models updated
```

Two things are dropped rather than queued: a reading requested while one is in flight, and a maintenance beat arriving while the previous one is still working. The readings are a live view of a live machine, so a backlog of them has nothing in it anybody wants, and queueing turns a slow machine into one that never catches up.

---

## 4. The life of a command

```
  User clicks Maximum
      → DashboardViewModel.Mode changes
      → WindowController.OnDashboardChanged   ignored if IsApplyingReading
            Requested()                        stamps the moment
            ApplyFans()
      → FanControl.Apply()
            terminate any program
            release the off switch and the max flag, in that order
            SetLevels(ceiling, ceiling)
      → FanArray.SetLevels
            Fan.InvalidateLevels()
            Fan.NoteLevelRequest(levels)       so the next reading can check
      → Hw.BiosSet / Hw.EcSet
```

Nothing writes to the hardware from the dispatcher. Everything above is decision; the writing happens on the maintenance worker or in direct response to a click, and either way through `Hw`, which takes the lock.

---

## 5. A reading is not an instant

This is the subtlest thing in the application and it caused a bug that took three attempts to state correctly.

Gathering a reading is dozens of round trips. On a contended machine it takes **seconds**. So:

```
  t=0.0  poller begins gathering, reads "the maximum flag is off"
  t=0.3  user clicks Maximum; the write goes out
  t=4.5  the reading arrives — carrying an answer from before the click
```

A settle window ("was the request recent?") is the obvious guard and it is the wrong one. What matters is whether the *answer* was fetched before or after the request, and those two come apart precisely when the machine is slow — which is when somebody is watching.

So `Reading.TakenAt` is stamped before anything is asked, and:

```csharp
WindowController.ShouldFollowReading(hasRequested, requestedAt,
                                     readingTakenAt, now, settleMs)
```

refuses any reading older than the user's last request, however long ago that request was. The settle window remains as the second line, for an answer that is current but arrived before the firmware acted.

`Reading.GpuPowerReadAt` exists for the same reason at a different rate: that value is refreshed one tick in five, so the reading carrying it can be four seconds newer than the answer inside it.

---

## 6. Safety invariants

These are the properties that must not be broken. Each of them exists because it was broken once.

**The hardware gate.** `Identity.cs` refuses to start on anything it can positively identify as not an HP portable. It refuses on evidence and allows on the absence of it — a machine whose WMI will not answer is not thereby a desktop. Somebody installed the upstream project onto an Omen desktop and was left with a permanently wrong fan curve, through a BIOS reset and a Windows reinstall.

**The fans are handed back.** Every exit path calls `FanControl.ReleaseToFirmware`. Quitting with the fans off used to leave them off; quitting while thermal protection had them pinned left them pinned. `Maintainer.Drain()` is part of this: a beat still running during shutdown re-asserts exactly what the handback is clearing.

**Thermal protection outranks everything.** At the high threshold the fans go to maximum; if the temperature keeps climbing, every manual override is dropped so the controller's own automatic control takes over. The off switch is cleared *before* maximum is asserted — asking for maximum while the fans are held off changes a number nothing is reading.

**A manual fan speed needs a plausible reading.** `SafeToKeepManualFans()` requires a recent, believable temperature below the protection threshold. On any doubt the failsafe countdown is allowed to lapse and the firmware takes over.

**Readings have believability bounds.** A controller reading above `MaxBelievableTemperature` is discarded rather than clamped. A sensor that stops answering goes dormant and stops counting towards the machine's hottest point — one that froze while holding a high figure used to hold the fans at maximum indefinitely.

**A false zero is not a battery.** `Battery.Sanitise` holds back an impossible charge. A momentarily desynced controller can poison the ACPI fuel gauge, and Windows acts on a critical charge by shutting the machine down. `Battery.IsFalseCritical` cannot prevent that — Windows reads the gauge itself — but it records that the flag was a lie.

**Auxiliary probes do not drive the fans.** `TNT2`–`TNT5` are spare thermistor channels whose meaning differs per board. They are read and shown; they are kept out of the hottest-of decision unless a user opts one back in.

---

## 7. Adapting to the board

Nothing per-board is a constant if it can be asked for.

| Fact | Where it comes from | Fallback |
|---|---|---|
| Fan count | `DeviceProfile.FanCount`, from the firmware | 1 — the one it can be sure of |
| Fan ceiling | The firmware's own fan table, when it is credible | The configured value, with the reason recorded |
| Fan modes | The firmware's support flags | None offered |
| Keyboard zones | The BIOS colour table | 1 |
| Register presence | Probed; an absent one goes dormant | Not read again for a while |

`DeviceProfile.Observe()` widens the ceiling when the firmware is seen running the fans higher than it claimed they go — some boards describe a conservative curve and then exceed it.

---

## 8. Threading

| Thread | Owns |
|---|---|
| Dispatcher | Every window, view model and the tray icon. Decides what is due. Never talks to hardware. |
| Poller worker | One `Reading` at a time |
| Maintainer worker | One maintenance beat at a time |
| Background one-shots | Task repair, auto-configuration, settings probes |

The two workers are separate on purpose: they run at different cadences and a slow reading must not delay a fan program. They are serialised against each other only by the Embedded Controller's own mutex, which is what serialises this application against every *other* monitoring application on the machine as well.

Anything touching a window or the tray icon from a worker goes through `GuiTray.OnUiThread`. `SetNotifyText`, `ShowBalloonTip` and the backlight state notification marshal themselves.

---

## 9. Configuration

`Library/ConfigData.cs` holds the values and the reason each one exists. `Library/Config.cs` reads and writes `StarMon.xml`. The XML node prefix is rooted at the **brand**, which is a constant, not at the assembly name, which is not — the test host is built under a different name and could otherwise read no configuration at all.

A setting the file does not mention keeps the built-in value. `DefaultUseFor()` consults a snapshot of what shipped, so a configuration file that says nothing about a sensor does not override what this build decided about it.

---

## 10. Tests

A hand-rolled runner, because there are no package references to bring one in.

```
  StarMon.exe -SelfTest            everything
  StarMon.exe -SelfTest service    suites whose name contains "service"
```

Suites are discovered by reflection over `[TestSuite(Order, TouchesHardware)]`. Each runs behind its own try/catch, so one suite cannot take the run with it.

Three things the runner has that a stock one does not:

- **`SelfTest.Gap`** records an expectation the code does not meet yet. It is not a pass and not a failure while it holds false — and it *fails* the moment it starts holding true, so the work that closes a gap also retires its own marker.
- **`SelfTest.Skip`** is counted apart from passes, so a run that quietly checked less than usual says so.
- **The device matrix** (`Test/Devices/`) runs the shipping code against boards nobody here owns: a single-fan board, a three-fan board, a stuck probe, a frozen sensor, a board that ignores fan writes, one that refuses them, one whose fan table is padding. Each is a real report, reduced to one stated difference.

The build also gates on things a test cannot see: duplicate locale keys, and test methods that are declared and never called.

**What the matrix cannot do** is watch a fan spin up. The fake boards answer instantly and exactly, which is why a check that fires on hardware doing as it is told passed here and failed on a real machine. Anything about *timing* has to be verified on hardware.

---

## 11. Adding support for a board that misbehaves

1. Get a report: `StarMon.exe -Probe report.md`. Read-only, works unelevated, and carries the identity, the profile, all 256 registers, every named register, the firmware's fan table and the fan array as built.
2. Find the one stated difference from the reference board.
3. Add it to `Test/Devices/DeviceCatalogue.cs` as a scenario.
4. Make the scenario pass without breaking the reference one.

The order matters. A fix written before the scenario is a fix for a board nobody can reproduce.

---

## 12. File map

| Path | |
|---|---|
| `All/` | Assembly metadata and the version stamped at build time |
| `App/App.cs` | Entry point: GUI, `-Run`, `-SelfTest`, `-RenderUi`, or the console |
| `App/Cli/` | Command line, including `-Probe` |
| `App/Gui/` | Tray context, the heartbeat, the Omen key handler |
| `App/Service/` | Poller, Maintainer, Ticker, ThermalGuard, FanControl, HistoryBuffer, Reading |
| `Driver/` | WinRing0, PawnIO, and the facade over both |
| `External/` | P/Invoke declarations |
| `Hardware/` | Controller, firmware, sensors, fans, profile, identity, capabilities |
| `Library/` | Config, Logger, Locale, Os, Hw, WMI helpers |
| `Resources/` | Driver and PawnIO module binaries, icons, the typeface |
| `Test/` | The self-test runner, the suites and the device matrix |
| `Ui/` | Views, view models, theme, tray shell, the main window |

---

## 13. Conventions

- **Comments say why.** What the code does is readable from the code. The comment is for the reason it is not the obvious thing, and most of them name the failure that made it so.
- **Nothing silently invents a number.** Where a value cannot be read, it is absent, and absence is reported as absence — not as zero, which reads as a probe at absolute cold.
- **Anything hard to reproduce gets a pure function.** The tick-wrap arithmetic, the AMD temperature decoding, the stale-reading decision, the fan-write check: each is separated out and tested, because each produces a *believable* wrong answer rather than an obvious one.
- **A guard that fires on healthy hardware is worse than no guard.** The log then says every machine is broken. This was written down and then not obeyed once; see the fan-write check's history.
