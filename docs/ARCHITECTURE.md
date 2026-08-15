# StarMon — how it is built

About 43,000 lines of C# targeting .NET Framework 4.8, in one non-SDK-style project, with no package references at all. Everything it needs it either carries or binds by hand.

This document is for whoever changes it next. It explains the layers, the rules each one lives by, and the handful of decisions that are load-bearing — the ones where doing the obvious thing produces an application that appears to work and quietly drives the wrong hardware.

If you are looking for what StarMon *does*, that is the [README](../README.md).

> **What this rests on.** Everything below the interface — `Driver`, `Hardware`, `App`, `Library`, `External` — is described from having read it. The `Ui` layer, about eleven thousand lines across views, view models and theme, is described from its seams: `WindowController` is read in the places that matter, the rest is summarised from structure. Where this document states a number, a rule or a failure, it is from the code.

---

## 1. The one thing to understand first

**This code is developed on a machine it is correct on.**

The development board is an HP Victus 15, board 8DCF. It has two fans. Its WMI answers. Every firmware call it is asked returns data. Its colour table comes back full. Its drive answers the health-log query every time. Nothing here has ever been wrong on it.

A read-through of this codebase in August 2026 found eighteen defects. **Thirteen of them are invisible on that board**, and every one of them is silent — no exception the user sees, no message, no wrong number. What they produce is:

- an application that does not start at all (a machine whose WMI is locked down)
- fans that never move (a board with one fan, running a fan program)
- a crash on a command (`-Bios Color` on a single-zone keyboard)
- a feature that switches itself off for good (one busy moment on the drive)
- a window that will not open (any machine with Start with Windows on)

None of them could be found by using the application. All of them were found by reading it and asking *what does this do on a machine unlike this one*.

So the rule that governs everything below: **a difference between boards is not an edge case here — it is the ordinary case, and the development machine is the exception.** When you write something that indexes the second fan, reads the fourth colour zone, or believes a firmware answered, ask what happens on the board that has one fan, one zone, and does not answer.

---

## 2. The four things it talks to

Everything in this application is one of four conversations, and they have almost nothing in common.

| | What it is | Reached through | Costs |
|---|---|---|---|
| **Embedded Controller** | A microcontroller on the board owning the fans, the temperature probes and the keyboard backlight | Two I/O ports, `0x62` and `0x66`, behind a kernel driver and a machine-wide mutex | A handful of port exchanges per register, contended with the firmware itself |
| **WMI BIOS interface** | HP's own firmware calls: performance profiles, the fan table, graphics power, backlight colour | `ACPI\PNP0C14`, through `Microsoft.Management.Infrastructure` | A WMI round trip — an order of magnitude slower than a register |
| **The graphics card** | Temperature, load, clocks, power, video memory | NVAPI and NVML, bound by hand through function ids | A call into the display driver's user-mode half |
| **Windows** | Battery, memory, disk, network, power plan, brightness, thermal zones | Ordinary Win32 and WMI | Varies; the slow ones are cached |

The first two are why this application exists and why it is dangerous. The register map in `Hardware/EcData.cs` was transcribed from **one board's ACPI tables**. On a different board the same addresses mean something else, and the firmware accepts the write without reporting an error. That is not hypothetical — see §7.

---

## 3. Layers

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

The struct layouts are hand-written and have to match the native ones exactly, including padding. `WLAN_ASSOCIATION_ATTRIBUTES` carries a six-byte MAC address followed by a field needing four-byte alignment, so there are two bytes of padding in the middle of it that no line of C# mentions — sequential layout produces them, and a `Pack = 1` added in passing would silently break every reading from it.

### `Driver/` — ring 0

Two drivers, one facade.

- **`Ring0.cs`** — WinRing0 1.2.0.5, extracted from an embedded resource and installed as a kernel service. Raw port, MSR, PCI and memory access. On Microsoft's vulnerable-driver list, and does not load where that list is enforced — the default on Windows 11.
- **`PawnIo.cs`** — the signed alternative. Loads verified programs into the kernel rather than handing out raw access; the one used for the controller permits ports `0x62` and `0x66` and refuses everything else. The modules are carried as embedded resources and the driver verifies each one, so a wrong byte disables PawnIO on every machine rather than misbehaving — which is why their digests are asserted in the tests.
- **`LowLevel.cs`** — the only thing above this layer that knows which of the two answered.

`LowLevel` keeps apart three questions that used to be one:

```csharp
LowLevel.IsOpen    // a driver loaded
LowLevel.HasMsr    // the processor registers are readable
LowLevel.HasSmn    // AMD's System Management Network is readable
```

PawnIO can be open with no processor module — the vendor modules decide for themselves whether this is their processor — so a machine can have a working controller and no MSRs. Every caller was already guarding on the first and meaning one of the others.

### `Hardware/` — what this machine is

The largest layer and the one with the most rules.

**`Ec.cs`** implements the controller's wait-and-retry protocol. Two things in it are not decoration:

- The wait budget is a **span of time**, not a count of iterations. `Thread.Sleep(1)` sleeps until the next scheduler tick — about 15 ms — so counting iterations and sleeping between them produces a real limit fifteen times longer than it reads as. The loop escalates from spinning to yielding and never sleeps.
- Failed waits are counted **per register**, not per controller. A shared counter let one register's failures vouch for another's: six absent probes fail three reads each in one pass, eighteen in a row, and from that point a fail-open bypass is open for every other register. A fan tachometer then reports whatever byte happens to be in the port.

**`Bios.cs` / `BiosCtl.cs`** are the firmware calls. `Bios.Send` allocates the buffer the caller asked for and **guarantees it** — see §5.

**`PlatformComponent.cs`** is the abstraction everything rests on, and the reason a sensor can be read without knowing where it comes from:

```
  IPlatformComponent          access type, data size, link type, name
    IPlatformReadComponent    GetValue, GetValueTrend, Update, a constraint
    IPlatformWriteComponent   SetValue
      PlatformComponentAbstract  holds last/previous, enforces access
```

Four concrete links: `EcComponent` (a register), `WmiBiosTemperatureComponent`, `MsrCpuTemperatureComponent` (the processor's own sensor) and `NvapiGpuTemperatureComponent` (the card's own).

Two things in the base class are load-bearing:

- **`TryRead` exists apart from `Read`** because a failed controller read hands back zero, which is indistinguishable from a register that genuinely holds zero. Links that cannot tell the two apart keep reporting success; the ones that can, do not.
- **A reading above the component's `Constraint` is discarded, not clamped.** The previous value stands. This is what stops an implausible number reaching the fan curve, and it is why a *frozen* sensor is a separate problem from an absent one.

**`Platform.cs`** is the assembled machine — sensors, fans, the system component — built from what the board reports rather than from constants.

- **It substitutes better sources for two sensors.** Where the processor's own thermal sensor is readable, `CPUT` becomes an MSR component; where an NVIDIA card is present, `GPTM` becomes an NVAPI one. The names are kept, so nothing above has to know. The second is not only about accuracy: the board's GPU register cannot answer while an Optimus card is asleep, so polling it fills the log with failures for a redundant reading.
- **The hottest reading spans three sources**: the component array, the firmware's own published sensors, and the ACPI thermal zones. Both of the latter had a maximum of their own since they were written and neither had a caller — so on a board publishing its hottest point through one of them, the thermal guard was protecting the machine with a figure that could not see it.
- **Sensors go dormant and come back.** A register the board does not carry fails every read forever; after 30 fruitless updates it is stood down to one retry in 60, and returns the moment it answers. Dormancy is a reduced rate, not a verdict.

**Sticky settings.** `SetFanModeSticky` and `SetGpuPowerSticky` record what the user asked for and re-assert it on a keep-alive, because this firmware resets both on its own schedule. The graphics power is written blind rather than compared first — the board that most needs it refuses the *read* while accepting the write.

**`DeviceProfile.cs`** is what was worked out about this board at startup: fan count, the fan-level ceiling and where it came from, which fan modes exist, keyboard zones, panel refresh rates. Every value carries its provenance as text, because "56" and "56, because the firmware's fan table said so" are different claims and only one can be argued with. `Observe()` widens the ceiling when a fan is seen running past it — some boards describe a conservative curve and then exceed it.

**`Identity.cs`** is the gate (§7). **`Absent.cs`** holds the stand-ins (§5). **`CodeIntegrity.cs`** examines *why* a driver would not load and says so in a sentence; it examines silently, and whoever knows there is a problem does the telling.

### `App/Service/` — policy

Deliberately given no reference back to the application, so it cannot start doing anything but decide.

- **`Poller.cs`** gathers one `Reading` from everything, on a background thread, one at a time. Slow-moving values refresh one tick in five.
- **`Maintainer.cs`** carries out the periodic hardware *work* — fan program, guard, sticky settings — on a worker of its own.
- **`Ticker.cs`** is one periodic slot. `Rewind()` and `Due()` are two passes on purpose; folding them together loses the property that a slot nobody asked stays ready.
- **`ThermalGuard.cs`** decides engage / release / panic with hysteresis. It decides; carrying it out belongs to the caller.
- **`FanControl.cs`** turns a request (Automatic, Constant, Maximum, Off, Program) into an ordered sequence of hardware operations. Order matters as much as content. **`0xFF` is not a fan level** — it is the sentinel that clears a custom level and hands the speeds back to the firmware, which is what Automatic does.
- **`Backlight.cs`** holds `BacklightColor` (temperature to colour, the hue circle, brightness scaling) and `IdleWatch`, the state machine that switches the backlight off after a period without input.
- **`HistoryBuffer.cs`** is a rolling window of named series behind one wrap-around buffer.
- **`Reading.cs`** is the snapshot, and it carries **when it was taken** (§6).

### `Library/`

`Config`/`ConfigData` hold the values and the reason each exists; the XML node prefix is rooted at the **brand**, which is a constant, not the assembly name, which is not — the test host is built under a different name and could otherwise read no configuration at all.

`Logger` distinguishes **commands from values**: a BIOS call or an EC write is an event and is never collapsed, because "did the application send it" is the question logs are opened for; a register reading is a value and collapses while it holds steady. Clearing the log clears the deduplication state with it, or a steady reading is suppressed against a value from before the list was emptied and never appears again.

`Locale` rebuilds a language's dictionary from what the build ships and swaps the reference in, rather than editing it in place — the poller reads it from another thread every tick, and a `Dictionary` written while read can throw or spin.

`Os` owns the scheduled tasks, and **repairs them at startup** where the file they name has gone: the path is written in when the task is registered and nothing revalidates it, so moving the folder breaks the Omen key silently.

### `Ui/`

`Ui/Windows/WindowController.cs` is the seam: it applies a `Reading` to the view models and turns property changes back into hardware requests. `IsApplyingReading` is what stops those being the same event — without it, writing a reading into the view model raises the same notification a click does, and the window answers the hardware by asking it for the state it just reported, once a second, forever.

---

## 4. The life of a reading, and of a command

```
  Ticker says the monitoring slot is due
      → Poller.Request()            drops if one is already in flight
      → Poller.Gather()             on a thread-pool thread
            stamps Reading.TakenAt  before asking anything
            Platform.UpdateTemperature(), UpdateFans()
            Hw.EcExec(...)          takes the machine-wide EC mutex
            LowLevel.ReadIoPort()   → PawnIO or WinRing0
      → Poller.Read event           still on the background thread
      → WindowController.Apply      marshalled to the dispatcher
```

```
  User clicks Maximum
      → WindowController.OnDashboardChanged   ignored if IsApplyingReading
            Requested()                        stamps the moment
      → FanControl.Apply()
            terminate any program
            release the off switch and the max flag, in that order
            SetLevels(ceiling, ceiling)
      → FanArray.SetLevels
            Fan.NoteLevelRequest(levels)       so the next reading can check
      → Hw.BiosSet / Hw.EcSet
```

Two things are dropped rather than queued: a reading requested while one is in flight, and a maintenance beat arriving while the previous is still working. The readings are a live view of a live machine, so a backlog has nothing in it anybody wants, and queueing turns a slow machine into one that never catches up.

---

## 5. Contracts

Four rules that hold across the whole codebase. Each was arrived at by something breaking when it did not hold.

### A call never hands back less than was asked for

`Bios.Send` allocates a buffer of the requested size and **guarantees it comes back that size** (`Bios.Fit`). The firmware's answer is cast with `as byte[]`, which is null when it answered without a payload — which is what an unsupported call does on a board that does not implement it. Callers index byte zero without looking, and the ones that do are precisely the ones documented as reaching such a board.

Zero is what every caller already reads as *this board does not do that*. Padding says what they would have concluded, instead of faulting before they can conclude it.

### A stand-in fails the same way as the thing it stands in for

`Absent.cs` replaces the controller and the firmware interface when they cannot be reached, so the application runs reduced rather than not at all. The stand-in's job is to be **indistinguishable in shape**: reads report the exchange did not happen (the same answer an absent register gives), writes are dropped, the lock is granted, and `Send` returns a buffer of the size asked for because the real one does.

A stand-in that faults differently from the real thing is worse than no stand-in, because every caller is written against the real one.

### Absence is decided by a run, not by one failure

`HpSensors`, `AcpiThermal` and `DiskTemperature` all stop asking after **three** empty runs, not one. A momentarily busy source is not an absent one — and the moment a drive is busy is the moment somebody is watching its temperature.

`Platform`'s sensor dormancy is the same rule at a different scale: 30 fruitless updates, then one retry in 60, and back the instant it answers.

### A guard that fires on healthy hardware is worse than no guard

The log then says every machine is broken. This was written down in the fan-write check and then not obeyed: the check shipped, and on real hardware it accused a board of ignoring a write when the fans were merely still spinning up, and again when the "level" it compared against was `0xFF` — the release sentinel, not a level at all.

Three things have to hold before a board is accused now: the reading disagrees **and** was not clamped to the ceiling, the fans have **stopped moving**, and there is a previous reading to compare against.

---

## 6. A reading is not an instant

The subtlest thing here, and it caused a bug that took three attempts to state correctly.

Gathering a reading is dozens of round trips. On a contended machine it takes **seconds**:

```
  t=0.0  poller begins gathering, reads "the maximum flag is off"
  t=0.3  user clicks Maximum; the write goes out
  t=4.5  the reading arrives — carrying an answer from before the click
```

A settle window ("was the request recent?") is the obvious guard and it is the wrong one. What matters is whether the *answer* was fetched before or after the request, and those come apart precisely when the machine is slow — which is when somebody is watching.

So `Reading.TakenAt` is stamped before anything is asked, and `WindowController.ShouldFollowReading` refuses any reading older than the user's last request, however long ago that request was. The settle window remains as the second line, for an answer that is current but arrived before the firmware acted.

`Reading.GpuPowerReadAt` exists for the same reason at a different rate: that value is refreshed one tick in five, so the reading carrying it can be four seconds newer than the answer inside it.

The same shape appears in `GuiFilter`: a suppression meant for the moments after an automatic start had no bound on it, so on any machine with Start with Windows enabled the window would not open on the first request. **Any state that means "recently" needs a clock.**

---

## 7. Safety invariants

The properties that must not be broken. Each exists because it was broken once.

**The hardware gate.** `Identity.cs` refuses to start on anything positively identified as not an HP portable. It refuses on evidence and allows on the absence of it — a machine whose WMI will not answer is not thereby a desktop. Somebody installed the upstream project onto an Omen desktop and was left with a permanently wrong fan curve, through a BIOS reset and a Windows reinstall. On the command line the gate applies to **writes only**: reading a register on a board nobody understands is how it comes to be understood, and `-Probe` exists for exactly those machines.

**The fans are handed back.** Every exit path calls `FanControl.ReleaseToFirmware`. Quitting with the fans off used to leave them off. `Maintainer.Drain()` is part of this: a beat still running during shutdown re-asserts exactly what the handback is clearing.

**Thermal protection outranks everything.** At the high threshold the fans go to maximum; if the temperature keeps climbing, every manual override is dropped so the controller's own management takes over. The off switch is cleared *before* maximum is asserted — asking for maximum while the fans are held off changes a number nothing is reading.

**A manual fan speed needs a plausible reading.** On any doubt the failsafe countdown is allowed to lapse and the firmware takes over.

**Readings have believability bounds.** A controller reading above `MaxBelievableTemperature` is discarded rather than clamped. A sensor that stops answering goes dormant and stops counting towards the machine's hottest point.

**A false zero is not a battery.** A momentarily desynced controller can poison the ACPI fuel gauge, and Windows acts on a critical charge by shutting the machine down. `Battery.Sanitise` holds back an impossible charge; `Battery.IsFalseCritical` cannot prevent the shutdown — Windows reads the gauge itself — but it records that the flag was a lie.

**Auxiliary probes do not drive the fans.** `TNT2`–`TNT5` are spare thermistor channels whose meaning differs per board. They are read and shown; they are kept out of the hottest-of decision unless a user opts one back in.

---

## 8. Adapting to the board

Nothing per-board is a constant if it can be asked for.

| Fact | Where it comes from | Fallback |
|---|---|---|
| Fan count | `DeviceProfile.FanCount`, from the firmware | 1 — the one it can be sure of |
| Fan ceiling | The firmware's fan table, when it is credible | The configured value, with the reason recorded |
| Fan modes | The firmware's support flags | None offered |
| Keyboard zones | The BIOS colour table, resolved down | 1 |
| Keyboard body | HP BIOS setup's own description of the deck | The type enumeration |
| Register presence | Probed; an absent one goes dormant | Not read again for a while |

Two of these are subtler than they look.

**The fan ceiling** is only lowered on a table that is *credible*: a board answered this call with twelve rows of padding carrying one stray level of 24, and twelve rows sailed past a check for six. The answer was refused by luck — 24 fell below the plausibility floor — and a stray 30 would have capped the user's fan curve at 30 on fans that reach 56. A curve is rows that ask for a fan speed; padding is not.

**The keyboard zone count** cannot be read. The colour table is a fixed four-entry structure and single-zone decks report four in its zone byte just as a genuine four-zone deck does, so a four proves nothing. The two mistakes are not equal — claiming one zone on a four-zone deck costs a feature, claiming four on a one-zone deck ships three dead controls — so an unproven four resolves to one and the owner turns it on from Settings.

---

## 9. Threading

| Thread | Owns |
|---|---|
| Dispatcher | Every window, view model and the tray icon. Decides what is due. Never talks to hardware. |
| Poller worker | One `Reading` at a time |
| Maintainer worker | One maintenance beat at a time |
| Background one-shots | Task repair, auto-configuration, capability probe, settings probes |

The two workers are separate on purpose: they run at different cadences and a slow reading must not delay a fan program. They are serialised against each other only by the Embedded Controller's own mutex — which is also what serialises this application against every *other* monitoring application on the machine.

Anything touching a window or the tray icon from a worker goes through `GuiTray.OnUiThread`. `SetNotifyText`, `ShowBalloonTip` and the backlight state notification marshal themselves.

The capability probe deserves its own note: it asks the firmware about two dozen features in turn and is not instant, so it runs off the dispatcher with the panel saying it is working. Anything that walks `FeatureSupport.GetAll()` for the first time is doing that work wherever it stands.

---

## 10. Tests

A hand-rolled runner, because there are no package references to bring one in.

```
  StarMon.exe -SelfTest            everything
  StarMon.exe -SelfTest service    suites whose name contains "service"
```

Suites are discovered by reflection over `[TestSuite(Order, TouchesHardware)]`. Each runs behind its own try/catch, so one suite cannot take the run with it.

Three things this runner has that a stock one does not:

- **`SelfTest.Gap`** records an expectation the code does not meet yet. Not a pass and not a failure while it holds false — and it *fails* the moment it starts holding true, so the work that closes a gap retires its own marker.
- **`SelfTest.Skip`** is counted apart from passes, so a run that quietly checked less than usual says so.
- **The device matrix** (`Test/Devices/`) runs the shipping code against boards nobody here owns: a single-fan board, a three-fan board, a stuck probe, a frozen sensor, a board that ignores fan writes, one that refuses them, one whose fan table is padding.

The build also gates on things a test cannot see: duplicate locale keys, and test methods that are declared and never called.

### What the tests cannot see

This matters more than the list above, and every item is something that actually got through.

- **The fakes answer instantly and exactly.** No fan spins up, so a check comparing a commanded level against a reading passed here and cried wolf on hardware.
- **The fakes implement the interface, not the code behind it.** A test asserting "a level was written" passed while the real `BiosCtl.SetFanLevel` threw on the same input, because the fake never reaches that line.
- **A guard that is read as correct is not a guard that was run.** `ColorTable` clamped its zone count and looked safe; the loop ran `i <= ZoneCount` and read one zone anyway. Reading it found nothing. Writing seven lengths through it found it immediately.
- **Anything about timing** has to be verified on hardware. So does anything about a *second* board.

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
| `App/Cli/` | Command line, including `-Probe` and `-EcMon` |
| `App/Gui/` | Tray context, the heartbeat, the Omen key handler, the instance filter |
| `App/Service/` | Poller, Maintainer, Ticker, ThermalGuard, FanControl, Backlight, HistoryBuffer, Reading |
| `Driver/` | WinRing0, PawnIO, and the facade over both |
| `External/` | P/Invoke declarations |
| `Hardware/` | Controller, firmware, sensors, fans, profile, identity, capabilities, stand-ins |
| `Library/` | Config, Logger, Locale, Os, Hw, WMI helpers |
| `Resources/` | Driver and PawnIO module binaries, icons, the typeface |
| `Test/` | The self-test runner, the suites and the device matrix |
| `Ui/` | Views, view models, theme, tray shell, the main window |

---

## 13. Conventions

- **Comments say why.** What the code does is readable from the code. The comment is for the reason it is not the obvious thing, and most of them name the failure that made it so.
- **A comment that is no longer true is a defect.** Three were found in one read-through: an overflow guard that did not prevent an overflow, a parameter described as removed that was still in the signature, a "hidden on this device" list that included things hidden on all devices. Each was harmless and each would have misled the next reader.
- **Nothing silently invents a number.** Where a value cannot be read, it is absent, and absence is reported as absence — not as zero, which reads as a probe at absolute cold.
- **Anything hard to reproduce gets a pure function.** Each of these produces a *believable* wrong answer rather than an obvious one, and each is tested directly: `LowLevel.Translate`, `CpuTemperature.DecodeAmdTctl`, `WindowController.ShouldFollowReading`, `GuiFilter.ShouldRaiseWindow`, `Os.ShouldRepairTask`, `Fan.DidNotTake`, `Bios.Fit`, `Battery.IsFalseCritical`, `AcpiThermal.ToCelsius`, `Poller.PickGraphicsName`, `HpBiosSettings.ClassifyBody`, `Identity.Decide`, `FanProgram.StepLevel`, `MainWindow.FitTo`.
- **Anything that means "recently" takes its clock as a parameter.** Both sides of the window can then be checked without waiting for one, and the tick counter wrapping every twenty-five days is tested rather than hoped about.
- **Write the scenario before the fix.** See §11.
