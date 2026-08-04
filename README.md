<div align="center">

# StarMon

**Hardware monitoring and control for HP Omen and Victus laptops**<br>
_HP Omen ve Victus dizüstüleri için donanım izleme ve denetimi_

[![License](https://img.shields.io/badge/license-GPL--3.0-8B5CF6?style=flat-square)](https://www.gnu.org/licenses/gpl-3.0.html)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-8B5CF6?style=flat-square)](#requirements)
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.8-8B5CF6?style=flat-square)](#building-from-source)
[![UI](https://img.shields.io/badge/UI-WPF-8B5CF6?style=flat-square)](#the-interface)

**[English](#english) · [Türkçe](#türkçe)**

</div>

---

# English

> Read temperatures, drive the fans, colour the keyboard and put the Omen key to work — without the manufacturer's software. StarMon never touches the network: no ads, no store, no telemetry.

### Contents

1. [What it is](#what-it-is)
2. [Requirements](#requirements)
3. [Getting started](#getting-started)
4. [The interface](#the-interface)
5. [Cooling in depth](#cooling-in-depth)
6. [Keyboard backlight](#keyboard-backlight)
7. [Settings reference](#settings-reference)
8. [Tray menu and the Omen key](#tray-menu-and-the-omen-key)
9. [Command line](#command-line)
10. [Configuration file](#configuration-file)
11. [One binary, every Omen and Victus](#one-binary-every-omen-and-victus)
12. [Building from source](#building-from-source)
13. [Project layout](#project-layout)
14. [Troubleshooting](#troubleshooting)
15. [License](#license)

---

## What it is

StarMon talks directly to two things the manufacturer's software also uses, and nothing else:

* the **Embedded Controller** (EC), the small microcontroller that owns the fans, the keyboard backlight and most of the temperature probes;
* the **WMI BIOS interface** that HP exposes through the `ACPI\PNP0C14` driver, for the calls the EC does not cover — performance profiles, graphics power, the fan table.

Around those it adds NVIDIA readings through NVAPI and NVML, processor power and throttle status through Intel RAPL MSRs or the AMD SMU register, drive temperature from the NVMe health log, and the ordinary Windows facilities for battery, memory, disk, network and power plan.

It runs in the notification area, uses a few megabytes, and does not phone home.

## Requirements

| | |
|---|---|
| **Operating system** | Windows 10 or Windows 11, 64-bit |
| **Runtime** | .NET Framework 4.8 (present on every supported Windows) |
| **Rights** | Administrator — the EC and the BIOS calls are not reachable otherwise |
| **Hardware** | An HP Omen or Victus laptop. Most features need the HP BIOS interface; what your particular board offers is listed on the System page |

> **Note** — Administrator rights are not optional and not a convenience. Reading an Embedded Controller port from user mode is refused by Windows, so an unelevated StarMon can show you almost nothing.

## Getting started

1. Build `Bin\StarMon.exe` (see [Building from source](#building-from-source)) or copy an existing build anywhere you like.
2. Run it. It starts **into the notification area**, not into a window — click the icon to open one.
3. On first run it writes `StarMon.xml` beside the executable and probes the firmware once to learn what this machine can do.
4. To have it start with Windows, turn on **Start with Windows** in Settings. That registers a scheduled task, because a normal startup entry cannot ask for elevation.

Everything is stored beside the executable. There is no installer, no registry footprint beyond the scheduled tasks you ask for, and uninstalling is deleting the folder.

## The interface

Eight sections, reached from the tabs in the title bar. A **live summary strip** sits under the tabs on every page: CPU and GPU temperature with a one-minute sparkline, both fans, the battery, and status chips for thermal protection, throttling and a running fan program.

### Dashboard

Four blocks — **CPU**, **GPU**, **Fans**, **System** — each with a headline temperature, a sparkline and a table of readings underneath. Below them a **history plot** of six series with a 2 / 5 / 10 minute window, hover crosshair, and CSV export. Below that the fan and performance controls.

The CPU block carries two bars per logical core: temperature, in health bands, and clock, in one flat colour — a slow core is not a core in trouble.

### Sensors

Everything the machine publishes, grouped and updated live: the board's own probes, the sensors HP publishes through its firmware interface (including fan speeds where the EC tachometer is unreliable), ACPI thermal zones, per-core temperature and clock, memory, storage, network, battery. The dashboard shows the headline figures; this is the full set.

### Cooling

The **fan curve editor** — drag the points, set hysteresis — plus the **fan program manager**: run, stop, save and delete the programs kept in the configuration file. Alongside it, *what this machine allows*: the fan ceiling and how it was worked out, whether the firmware admits to software fan control, whether levels go through the BIOS or straight to the EC, the failsafe countdown and the thermal protection state.

### Keyboard

The keyboard is drawn as your machine's own — with or without the numeric pad, in the ISO or ANSI body, with the legends of the layout being typed on. Click anywhere on it to set a colour. Backlight switch, up to four colour zones, four modes and an idle switch-off.

### System

What this machine is (model, board, BIOS, Windows), the eleven findings the startup probe worked out, all of the firmware's published BIOS settings in a searchable list, the Windows power mode, and the **full hardware report** — the thing to attach when reporting a problem.

### Log

What the application has done and what the hardware answered, filtered by what you are looking for: problems, hardware exchanges, interface actions, individual BIOS calls, individual EC accesses. Searchable, pausable, exportable.

### Settings

Every preference, in two columns. See [Settings reference](#settings-reference).

### About

Version, licence, credits.

> Every control and every reading in the interface carries a tooltip explaining not just what it is but what it costs or what it is for.

## Cooling in depth

### The four fan modes

| Mode | What it does |
|---|---|
| **Automatic** | Hands the fans back to the firmware, in the performance profile chosen beside it. The default and the safe state. |
| **Constant** | Holds both fans at the levels you set on the sliders. Both at zero switches them off; both at the ceiling is the same as Maximum. |
| **Maximum** | Both fans at the hardware ceiling — *and* the performance profile raised with them. Asking for the fans alone buys cooling for headroom that was never released: the noise without the speed. |
| **Program** | Runs a saved curve that follows temperature. The button names the program that is running. |

### Performance profiles

`Default`, `Performance`, `Cool`, `Quiet`, and `Extreme` on the boards that have it. These are the firmware's own power and thermal envelopes, not fan speeds. On this class of hardware **Performance is what lifts the graphics power past its base draw** — which is why Maximum fans sets it too.

### Graphics power

`Base` · `Custom TGP` · `Boost`. A request rather than a setting: what the firmware does with it depends on the chassis, the power source and its own thermal headroom.

> The write is attempted even on boards that refuse to *report* their graphics power. Whether a board will tell you its TGP and whether it will accept a new one are two separate questions, and assuming the second from the first costs real wattage.

### The fan ceiling

The level a 100 % point on the curve maps to, and the top of the manual sliders. It is read from the board's own fan table rather than assumed, and **revised upwards** whenever the firmware is seen running a fan higher than the recorded figure. It never goes down on its own. A board that understates its table still ends up with its full range.

### The failsafe countdown

The EC runs a timer. When it reaches zero the firmware takes the fans back, whatever level was set by hand — which is the entire explanation for a manual fan speed reverting on its own. **Keep a manual fan speed from reverting** extends it while a level is held.

### Thermal protection

When the hottest sensor reaches the threshold, StarMon forces the fans to maximum; a few degrees above that it hands cooling back to the firmware entirely, on the principle that a machine in trouble is better off with its own emergency behaviour than with ours. The summary strip says so while it is active, so you can tell it apart from something you did.

## Keyboard backlight

| Mode | Behaviour |
|---|---|
| **Static colour** | One colour, held. The zone swatches are what it uses. |
| **Follow temperature** | Cool through to hot as the machine warms up. |
| **Colour cycle** | Moves through the colour wheel continuously. |
| **Breathing** | Fades down and back up in the colours set below. |

Animated effects have a speed of 1–5. **Switch off when idle** blanks the backlight after 0–30 minutes without a keypress; zero never switches it off.

Zones are however many the firmware addresses — one or four. Per-key RGB decks, which cannot take a four-zone colour table, keep the switch and the effects and simply lose the swatches.

> StarMon holds the backlight state itself rather than asking the firmware. On some boards the read is simply wrong, and a switch that disagrees with the light is worse than no switch.

## Settings reference

### Hardware controls

| Setting | Notes |
|---|---|
| **Graphics mode** | `Optimus` routes the display through the integrated graphics; `Discrete` wires it to the NVIDIA chip. More performance, less battery, needs a restart. |
| **CPU Turbo Boost** | `Off` holds the base clock; `On` is normal; `Aggressive` boosts harder and longer. |
| **Display brightness** | The same control as the function keys. |
| **Thermal protection** | On/off and the threshold, 80–99 °C. |

### Fans

| Setting | Notes |
|---|---|
| **Level ceiling / floor** | Scale the sliders and the curve. Set by hand only for a board that misreports its own. |
| **Curve hysteresis** | How far the temperature must fall below a curve step before the level steps back. Zero follows the curve exactly and makes the fans surge on a boundary. |
| **Let the ceiling rise** | Auto-detection of a higher ceiling from observed speeds. |
| **Keep a manual fan speed from reverting** | Extends the EC failsafe countdown. |
| **Suspend the fan program while asleep** | Stops it over sleep and starts it again on resume, so it does not wake acting on a stale temperature. |

### Behaviour, display and logging

| Setting | Notes |
|---|---|
| **Start with Windows** | Registers an elevated scheduled task at logon. |
| **Apply saved settings on start** | Re-applies fan, graphics and keyboard settings instead of accepting whatever the firmware came up with. |
| **Close button exits** | Off means close hides to the tray. |
| **Keep the window above other windows** | — |
| **Notify when the processor is throttling** | At most one notification every five minutes. |
| **Keep reading the graphics card on battery** | Reading the NVIDIA chip wakes it; on battery that costs power for figures nothing is watching. |
| **This keyboard has four colour zones** | Off by default, and correct that way on every deck: the whole keyboard takes one colour. The firmware cannot be asked — its colour table is four entries wide whatever the board is, so a single-zone Victus reports four exactly as a four-zone Omen does. Turn it on only if yours really lights in four separate regions. |
| **Refresh rate follows the power source** | Plus the two rates, typed or taken from what the panel reports. |
| **Switch the display off** | A global hotkey, captured by pressing the combination. A bare key is refused. |
| **Log verbose / to file / BIOS errors** | Plus the size at which the log file starts again. |
| **How often** | The polling cadence with the window open, with it hidden, and for a running fan program. |

## Tray menu and the Omen key

The notification icon is live — it draws the current temperature, with a backdrop that changes with it. Its menu carries the fan modes and profiles the firmware actually offers, the keyboard backlight and effects, the language, and the settings that are worth reaching without opening a window.

The **Omen key** can:

* start and stop the fan program — or cycle through every saved program;
* show the window on the first press and do its usual job from the second;
* stay silent instead of announcing the change;
* run a command instead, with arguments, optionally minimised. This takes precedence over the fan program.

## Command line

```
StarMon -<Arg1> [...] [-<ArgN> [...]]
```

| Argument | Effect |
|---|---|
| `-Bios` | Run every BIOS operation that only retrieves information |
| `-Bios <Op>[=<Data>]…` | Perform one or more BIOS operations, with optional parameters |
| `-Ec` | Print every Embedded Controller register as a table |
| `-Ec <Reg>[=<Byte>]…` | Read or write specific registers |
| `-Ec <Reg>(2)[=<Word>]…` | Read or write consecutive register pairs as words |
| `-EcMon [FileName]` | Watch every register for changes, optionally to a file |
| `-Prog` | List the fan programs in the configuration file |
| `-Prog <Name>` | Run one |
| `-Run <Task> [<Args>]` | Run a scheduled task headlessly |
| `-Task` | Show the status of all scheduled tasks |
| `-Task <Task>[=<Flag>]…` | Enable or disable one |
| `-Probe [FileName]` | Write down everything this machine says about itself, as Markdown. Read-only: it asks the firmware and the Embedded Controller for values and changes neither. This is the file to attach when a board does not behave — the register dump in it is the one part that is not this build's interpretation of your machine |
| `-SelfTest` | Run the built-in tests — touches no hardware |
| `-?` `-H` `-Help` `-Usage` | Usage information |

**BIOS operations:** `Cpu:PL1` `Cpu:PL4` `Cpu:PLGpu` `Gpu` `GpuMode` `Xmp` `FanCount` `FanLevel` `FanMax` `FanMode` `FanTable` `FanType` `Idle` `Temp` `Throttling` `BornDate` `System` `Adapter` `HasOverclock` `HasMemoryOverclock` `HasUndervolt` `KbdType` `HasBacklight` `Backlight` `Color` `Anim`

**Scheduled tasks:** `Gui` (autorun at logon) · `Key` (Omen key interception) · `Mux` (Advanced Optimus bug fix)

Arguments are case-insensitive and each may appear any number of times.

## Configuration file

`StarMon.xml`, written beside the executable, commented in full on first creation. Selected keys:

| Group | Keys |
|---|---|
| **Fans** | `FanLevelMax` `FanLevelMin` `FanLevelAutoDetect` `FanLevelUseEc` `FanLevelNeedManual` `FanProgramDefault` `FanProgramHysteresisC` `FanProgramSuspend` `FanCountdownExtend*` `FanModeKeepAliveMs` |
| **Thermal** | `ThermalProtectionEnabled` `ThermalProtectionHighC` `ThermalProtectionLowC` `ThrottleNotifyEnabled` `TemperatureCacheMs` |
| **Graphics** | `GpuPowerDefault` `GpuPowerSetInterval` `GpuPollOnBattery` |
| **Keyboard** | `KbdZoneCount` `KbdColorByTemp` `KbdColorEffect` `KbdEffectSpeed` `KbdIdleOffMinutes` |
| **Omen key** | `KeyToggleFanProgram` `KeyToggleFanProgramCycleAll` `KeyToggleFanProgramShowGuiFirst` `KeyToggleFanProgramSilent` |
| **Display** | `RefreshRateFollowPower` `RefreshRateAutoDetect` `PresetRefreshRateHigh` `PresetRefreshRateLow` `DisplayOffHotkeyKey` `DisplayOffHotkeyMods` |
| **Interface** | `Language` `GuiCloseWindowExit` `GuiStayOnTop` `GuiDynamicIcon` `GuiDynamicIconHasBackground` `GuiTipDuration` |
| **Timing** | `UpdateMonitorInterval` `UpdateProgramInterval` `UpdateIconInterval` |
| **Embedded Controller** | `EcFailLimit` `EcRetryLimit` `EcWaitLimit` `EcWaitTimeoutMs` `EcMutexTimeout` `EcMonInterval` |
| **Logging** | `LogVerbose` `LogToFile` `LogFileMaxBytes` `BiosErrorReporting` |
| **Startup** | `AutoConfig` `AutoStartup` |

`Language` takes `Auto`, `English` or `Turkish`. Fan programs live in their own section of the same file and are edited from the Cooling page.

## One binary, every Omen and Victus

Nothing about a particular board is compiled in. At startup StarMon asks the firmware what this machine is and adapts to the answer, so the same executable is correct on a Victus 15 and on an Omen 17 without a per-model file:

* **The fan ceiling** comes from the board's own fan table, and rises if the fans are ever seen running higher.
* **How fan levels are written** — the BIOS call, or the EC with the manual toggle raised first — follows whichever this board actually supports.
* **The performance profiles offered** are the ones the firmware reports. Extreme appears where it exists and stays hidden where it does not, rather than being shown to everyone and quietly doing nothing for half of them.
* **The keyboard** is drawn as the machine's own: numeric pad or not, ISO or ANSI, in the legends of the layout being typed on, in one or four colour zones.
* **The refresh-rate presets** come from the rates the panel offers rather than a fixed 60 / 144.
* **Temperature sensors the board does not carry** are noticed and stood down to an occasional retry, instead of costing an EC exchange a second forever.
* **The processor** is read through Intel MSRs or the AMD SMU thermal register, whichever is present.

Everything probed is listed in the hardware report with the source of each finding. Setting `FanLevelAutoDetect` or `RefreshRateAutoDetect` to `false` pins the values by hand on a machine whose firmware answers badly.

## Building from source

```powershell
.\build.ps1                     # build Bin\StarMon.exe
.\build.ps1 -Test               # build, then build a test host and run the self-test
.\build.ps1 -Render window      # draw a piece of the interface to Obj\window.png
.\build.ps1 -Configuration Debug
```

The script works with the plain .NET SDK — no Visual Studio. It handles the two things that otherwise make this project awkward to build:

* the **.NET Framework 4.8 reference assemblies** the SDK does not ship, along with `System.Management.Instrumentation`, which is not in the reference pack at all. Staying on `net48` is not nostalgia: that assembly and `System.Management` would need NuGet packages on modern .NET.
* the **WPF markup compiler**, which turns the `.xaml` files into BAML before the C# compiler runs. It ships inside the WindowsDesktop SDK rather than with the C# targets, and a non-SDK-style project does not import it on its own.

#### What has to be installed

* a **.NET SDK** including the Windows Desktop workload — it carries the markup compiler
* the **[.NET Framework 4.8 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net48)** — it carries the reference assemblies

Both are searched for, so nothing has to be configured. If either is somewhere unusual, `STARMON_REFASM` and `STARMON_MMI` name the directories directly and skip the search. These two paths used to be written into the script as absolute locations on one developer's machine, which meant the project built nowhere else — including on a CI runner, which is why `.github/workflows/build.yml` now builds through this same script rather than its own `msbuild` line.

### The self-test

The shipping executable demands elevation, which makes it unusable as a test host. `-Test` therefore builds the same sources a second time without the manifest into `Bin\Test` and runs `StarMonTest.exe -SelfTest`. **469 tests**, no hardware touched: the Embedded Controller is stood in for by a fake that models its I/O ports, so the wait-and-retry protocol can be exercised including the failure modes a working controller never produces.

#### The device matrix

This application is developed on one laptop out of a family it claims to support, and on that laptop every compiled-in assumption is correct — which is why several of them survived. So the other machines come to the code instead: `Test/Devices` holds a controller backed by a register file, a BIOS interface that can refuse a call *or accept it and do nothing*, and a set of boards each differing from this one in a single stated way. Every scenario is drawn from a report where that difference broke something on a real machine.

Expectations the code does not meet yet are recorded as **known gaps** rather than deleted or left failing. A gap is not a pass and not a failure; it is listed at the end and counted apart. When one starts holding, it fails — so work that lands has to retire its own marker, and the gap list cannot drift into describing code that has moved on.

Suites are found by reflection — a class marked `[TestSuite]` runs because it exists, rather than because someone remembered to add it to a list. An optional argument narrows the run:

```powershell
.\Bin\Test\StarMonTest.exe -SelfTest service    # only TestService
```

A filter that matches nothing is an error, not an empty pass. A suite that throws no longer takes the rest of the run down with it: it is reported, and the other suites still run. The exit code for a failing run is `6`, which used to be `1` — the same code a BIOS initialisation failure returns, so a script could not tell the two apart.

Three things are checked against the source before anything compiles, because none of them can be seen from a built assembly:

* a **duplicate locale key** — the collection-initialiser syntax silently keeps the last one
* a **test method with no caller** — it compiles, never runs, and the count at the end still looks healthy
* a **fan mode or message key missing from either language**

### The design loop

The interface cannot be checked by running the application — it needs elevation. `-Render <surface>` draws any part of it to a PNG instead:

`gallery` `dashboard` `window` `window-en` `window-tr` `window-one` `curve` `keyboard` `keyboard-off` `keyboard-tkl` `log` `system` `sensors` `settings` `picker` `menu` `trayicon`

`window-en` and `window-tr` pin their language, because the plain `window` surface renders in whatever language the machine building it happens to use — which is how an English layout can go unchecked for months on a Turkish machine.

### A constraint worth knowing

No markup in this project may name a type declared in the same assembly — no local converter instance, no `{x:Type local:…}`, no custom control in XAML. Markup that does cannot be compiled in one pass, and this project builds in one. Drawn controls are therefore placed into named `ContentControl` hosts from code-behind, and converters are registered into the application resources by string key. The only exception is the `{loc:Str}` markup extension.

## Project layout

| Path | What lives there |
|---|---|
| `App/Cli` | Command-line parsing and output |
| `App/Gui` | Tray host, poller wiring, notifications |
| `App/Service` | Readings, polling, fan control, history buffer, thermal guard |
| `Hardware` | Embedded Controller, BIOS calls, NVIDIA, battery, device profile |
| `Library` | Configuration, localisation, logging, WMI, conversions |
| `Ui/Theme` | Palette, typography, control styles |
| `Ui/Views` | Pages and drawn controls |
| `Ui/ViewModels` | The models behind them |
| `Ui/Windows` | Window controller — the bridge between readings and the interface |
| `Ui/Design` | The headless render surfaces and their sample data |
| `Test` | The self-test suite |

## Troubleshooting

**The fans go back to automatic on their own.**
The EC failsafe countdown reached zero. Turn on *Keep a manual fan speed from reverting*, or use a fan program, which re-asserts the level on every step.

**The fans never stop, whatever level I ask for.**
Check *Fans always on* on the System page. It is a BIOS setup option, and while it is on nothing StarMon does will silence them. It is changed in the BIOS setup screen.

**The graphics card will not go above its base wattage.**
Select the `Performance` profile. On this class of hardware that profile is what releases the extra graphics power; the fan setting alone does not.

**A control is greyed out.**
The firmware does not offer it on this board. The System page lists what was found and how.

**The window shows dashes everywhere.**
StarMon is not elevated. Nothing below the operating system is reachable from user mode.

**Something is wrong and I want to report it.**
System page → *Full hardware report* → **Copy**. Add the log with *Log verbose* turned on if the problem is reproducible.

## License

**StarMon** © 2026 Star, released under the [GNU General Public License Version 3](https://www.gnu.org/licenses/gpl-3.0.html#license-text): you may redistribute it and/or modify it under the terms of that licence as published by the [Free Software Foundation](https://www.fsf.org/). The full text is in `LICENSE.md`.

StarMon incorporates code from **OmenMon**, © 2023-2024 [Piotr Szczepański](https://piotr.szczepanski.name/), also under GPL-3.0. Portions of the driver code are based on Open Hardware Monitor, © 2009-2017 Michael Möller. The application icons and logo artwork are © 2023 Piotr Szczepański (CC BY-NC-ND 4.0); see `Resources/README.md` for the licensing of all bundled resources.

_This software is not affiliated with or endorsed by HP. Any brand names are used for informational purposes only._

<div align="right"><a href="#starmon">↑ Back to top</a></div>

---

# Türkçe

> Sıcaklıkları okuyun, fanları sürün, klavyeyi renklendirin ve Omen tuşunu işe koşun — üretici yazılımına gerek kalmadan. StarMon ağa hiç dokunmaz: reklam yok, mağaza yok, telemetri yok.

### İçindekiler

1. [Nedir](#nedir)
2. [Gereksinimler](#gereksinimler)
3. [Başlarken](#başlarken)
4. [Arayüz](#arayüz)
5. [Soğutma ayrıntılı](#soğutma-ayrıntılı)
6. [Klavye arka ışığı](#klavye-arka-ışığı)
7. [Ayarlar başvurusu](#ayarlar-başvurusu)
8. [Tepsi menüsü ve Omen tuşu](#tepsi-menüsü-ve-omen-tuşu)
9. [Komut satırı](#komut-satırı)
10. [Yapılandırma dosyası](#yapılandırma-dosyası)
11. [Tek çalıştırılabilir, bütün Omen ve Victus'lar](#tek-çalıştırılabilir-bütün-omen-ve-victuslar)
12. [Kaynaktan derleme](#kaynaktan-derleme)
13. [Proje düzeni](#proje-düzeni)
14. [Sorun giderme](#sorun-giderme)
15. [Lisans](#lisans)

---

## Nedir

StarMon, üretici yazılımının da kullandığı iki şeyle doğrudan konuşur, başka hiçbir şeyle değil:

* **Gömülü Denetleyici** (EC) — fanların, klavye arka ışığının ve sıcaklık problarının çoğunun sahibi olan küçük mikrodenetleyici;
* HP'nin `ACPI\PNP0C14` sürücüsü üzerinden sunduğu **WMI BIOS arayüzü** — EC'nin kapsamadığı çağrılar için: performans profilleri, ekran kartı gücü, fan tablosu.

Bunların çevresine NVAPI ve NVML üzerinden NVIDIA ölçümlerini, Intel RAPL MSR'leri ya da AMD SMU yazmacı üzerinden işlemci gücü ve kısıtlama durumunu, NVMe sağlık günlüğünden sürücü sıcaklığını, pil/bellek/disk/ağ/güç planı için de olağan Windows olanaklarını ekler.

Bildirim alanında çalışır, birkaç megabayt yer kaplar ve dışarıyla konuşmaz.

## Gereksinimler

| | |
|---|---|
| **İşletim sistemi** | Windows 10 veya Windows 11, 64 bit |
| **Çalışma zamanı** | .NET Framework 4.8 (desteklenen her Windows'ta hazır gelir) |
| **Yetki** | Yönetici — EC ve BIOS çağrılarına başka türlü erişilemez |
| **Donanım** | HP Omen ya da Victus dizüstü. Özelliklerin çoğu HP BIOS arayüzünü gerektirir; sizin anakartınızın neyi sunduğu Sistem sayfasında yazar |

> **Not** — Yönetici yetkisi bir tercih ya da kolaylık değil. Windows, kullanıcı kipinden Gömülü Denetleyici portu okunmasını reddeder; yükseltilmemiş bir StarMon size neredeyse hiçbir şey gösteremez.

## Başlarken

1. `Bin\StarMon.exe` dosyasını derleyin ([Kaynaktan derleme](#kaynaktan-derleme)) ya da hazır bir yapıyı istediğiniz yere kopyalayın.
2. Çalıştırın. **Pencereyle değil, bildirim alanında** açılır — pencere için simgeye tıklayın.
3. İlk çalıştırmada yanına `StarMon.xml` yazar ve bu makinenin neler yapabildiğini öğrenmek için ürün yazılımını bir kez yoklar.
4. Windows ile birlikte başlaması için Ayarlar'daki **Windows ile birlikte başlat** seçeneğini açın. Bu bir zamanlanmış görev kaydeder; çünkü olağan bir başlangıç girdisi yükseltme isteyemez.

Her şey çalıştırılabilir dosyanın yanında durur. Kurulum yok, istediğiniz zamanlanmış görevler dışında kayıt defteri izi yok; kaldırmak klasörü silmekten ibaret.

## Arayüz

Başlık çubuğundaki sekmelerden erişilen sekiz bölüm. Sekmelerin altında her sayfada duran bir **canlı özet şeridi** var: bir dakikalık minik grafiğiyle CPU ve GPU sıcaklığı, iki fan, pil, bir de ısıl koruma, kısıtlama ve çalışan fan programı için durum rozetleri.

### Panel

Dört blok — **İşlemci**, **Ekran kartı**, **Fanlar**, **Sistem** — her birinde öne çıkan bir sıcaklık, bir minik grafik ve altında ölçüm tablosu. Altlarında altı serilik **geçmiş grafiği**: 2 / 5 / 10 dakikalık pencere, üzerine gelince artı imleç ve CSV dışa aktarımı. Onun da altında fan ve performans denetimleri.

İşlemci bloğunda her mantıksal çekirdek için iki çubuk var: sağlık bantlarıyla sıcaklık, tek düz renkle frekans — yavaş çalışan bir çekirdek sorunlu bir çekirdek değildir.

### Sensörler

Makinenin yayımladığı her şey, gruplanmış ve canlı: anakartın kendi probları, HP'nin ürün yazılımı arayüzünden yayımladığı algılayıcılar (EC takometresinin güvenilmez olduğu yerde fan hızları dahil), ACPI ısıl bölgeleri, çekirdek başına sıcaklık ve frekans, bellek, depolama, ağ, pil. Panel öne çıkanları gösterir; burası tam listedir.

### Soğutma

**Fan eğrisi düzenleyicisi** — noktaları sürükleyin, histerezisi ayarlayın — ve **fan programı yöneticisi**: yapılandırma dosyasındaki programları çalıştırın, durdurun, kaydedin, silin. Yanında *bu makinenin izin verdikleri*: fan tavanı ve nasıl belirlendiği, ürün yazılımının yazılımla fan denetimini kabul edip etmediği, seviyelerin BIOS'tan mı doğrudan EC'ye mi yazıldığı, emniyet geri sayımı ve ısıl koruma durumu.

### Klavye

Klavye sizin makinenizin kendi klavyesi olarak çizilir — sayısal tuş takımıyla ya da onsuz, ISO ya da ANSI gövdede, yazdığınız düzenin tuş yazılarıyla. Renk vermek için üzerinde herhangi bir yere tıklayın. Arka ışık anahtarı, en çok dört renk bölgesi, dört kip ve boştayken kapanma.

### Sistem

Bu makinenin ne olduğu (model, anakart, BIOS, Windows), açılıştaki yoklamanın saptadığı on bir bulgu, ürün yazılımının yayımladığı bütün BIOS ayarları aranabilir bir listede, Windows güç modu ve **tam donanım raporu** — bir sorun bildirirken eklenecek şey.

### Günlük

Uygulamanın ne yaptığı ve donanımın ne yanıtladığı, aradığınıza göre süzülmüş: sorunlar, donanım alışverişleri, arayüz işlemleri, tek tek BIOS çağrıları, tek tek EC erişimleri. Aranabilir, duraklatılabilir, dışa aktarılabilir.

### Ayarlar

Bütün tercihler, iki sütunda. Bkz. [Ayarlar başvurusu](#ayarlar-başvurusu).

### Hakkında

Sürüm, lisans, katkılar.

> Arayüzdeki her denetimin ve her ölçümün, yalnızca ne olduğunu değil neye mal olduğunu ya da ne işe yaradığını da anlatan bir ipucu vardır.

## Soğutma ayrıntılı

### Dört fan kipi

| Kip | Ne yapar |
|---|---|
| **Otomatik** | Fanları ürün yazılımına, yanında seçilen performans profilinde geri verir. Varsayılan ve güvenli durum. |
| **Sabit** | İki fanı da sürgülerde belirlediğiniz seviyede tutar. İkisi de sıfırsa fanlar kapanır; ikisi de tavandaysa Azami ile aynı şeydir. |
| **Azami** | İki fan da donanım tavanında — *ve* performans profili onlarla birlikte yükseltilir. Yalnızca fanları istemek, hiç serbest bırakılmamış bir pay için soğutma satın almaktır: hız olmadan gürültü. |
| **Program** | Sıcaklığı izleyen kayıtlı bir eğriyi çalıştırır. Düğme, çalışan programın adını gösterir. |

### Performans profilleri

`Varsayılan`, `Performans`, `Serin`, `Sessiz`, bir de sahip olan anakartlarda `Extreme`. Bunlar ürün yazılımının kendi güç ve ısıl zarflarıdır, fan hızları değil. Bu sınıf donanımda **ekran kartı gücünü taban çekişinin üstüne çıkaran şey Performans profilidir** — Azami fan seçildiğinde profilin de ayarlanmasının nedeni budur.

### Ekran kartı gücü

`Temel` · `Özel TGP` · `Boost`. Bir ayardan çok bir istektir: ürün yazılımının bununla ne yapacağı kasaya, güç kaynağına ve kendi ısıl payına bağlıdır.

> Yazma işlemi, ekran kartı gücünü *bildirmeyi* reddeden anakartlarda bile denenir. Bir anakartın TGP'sini söyleyip söylemeyeceği ile yenisini kabul edip etmeyeceği iki ayrı sorudur; ikincisini birincisinden çıkarmak gerçek watt'lara mal olur.

### Fan tavanı

Eğrideki %100 noktasının karşılık geldiği seviye ve elle sürgülerin üst sınırı. Varsayılmaz, anakartın kendi fan tablosundan okunur; ürün yazılımı bir fanı kayıtlı değerin üstünde çalıştırırken görülürse **yukarı çekilir**. Kendiliğinden hiç inmez. Kendi tablosunu eksik bildiren bir anakart bile sonunda tam menziline kavuşur.

### Emniyet geri sayımı

EC bir sayaç işletir. Sıfıra ulaştığında ürün yazılımı, elle ne ayarlanmış olursa olsun fanları geri alır — elle ayarlanan bir fan hızının kendiliğinden geri dönmesinin tüm açıklaması budur. **Elle ayarlanan fan hızı kendiliğinden geri dönmesin** seçeneği, bir seviye tutulduğu sürece sayacı uzatır.

### Isıl koruma

En sıcak algılayıcı eşiğe ulaştığında StarMon fanları azamiye zorlar; birkaç derece üstünde ise soğutmayı tamamen ürün yazılımına bırakır — çünkü zor durumdaki bir makine, bizim davranışımızdansa kendi acil durum davranışıyla daha iyidir. Etkinken özet şeridi bunu söyler; böylece sizin yaptığınız bir şeyden ayırt edebilirsiniz.

## Klavye arka ışığı

| Kip | Davranış |
|---|---|
| **Sabit renk** | Tek renk, sabit tutulur. Bölge renk kutuları bunun içindir. |
| **Sıcaklığı izle** | Makine ısındıkça serinden sıcağa. |
| **Renk döngüsü** | Renk çemberinde sürekli dolaşır. |
| **Nefes efekti** | Aşağıda ayarlanan renklerde sönüp yeniden parlar. |

Hareketli efektlerin hızı 1–5 arasındadır. **Boştayken kapat**, 0–30 dakika tuşa basılmazsa arka ışığı söndürür; sıfırda hiç kapatmaz.

Bölge sayısı, ürün yazılımının kaç bölgeyi adresliyorsa o kadardır — bir ya da dört. Dört bölgelik renk tablosunu kabul edemeyen tuş başına RGB klavyeler anahtarı ve efektleri korur, yalnızca renk kutularını yitirir.

> StarMon arka ışık durumunu ürün yazılımına sormak yerine kendisi tutar. Bazı anahtarlarda okuma düpedüz yanlış geliyor ve ışıkla çelişen bir anahtar, hiç anahtar olmamasından kötüdür.

## Ayarlar başvurusu

### Donanım kontrolleri

| Ayar | Notlar |
|---|---|
| **Ekran kartı modu** | `Optimus` görüntüyü tümleşik ekran biriminden geçirir; `Ayrık` doğrudan NVIDIA çipine bağlar. Daha fazla performans, daha az pil, yeniden başlatma gerekir. |
| **CPU Turbo Boost** | `Kapalı` temel frekansta tutar; `Açık` olağandır; `Agresif` daha sert ve daha uzun yükselir. |
| **Ekran parlaklığı** | İşlev tuşlarındaki denetimin aynısı. |
| **Isıl koruma** | Açık/kapalı ve eşik, 80–99 °C. |

### Fanlar

| Ayar | Notlar |
|---|---|
| **Seviye tavanı / tabanı** | Sürgüleri ve eğriyi ölçekler. Elle ayarlamak yalnızca kendi tavanını yanlış bildiren bir anakart içindir. |
| **Eğri histerezisi** | Seviyenin geri inmesi için sıcaklığın eğri basamağının ne kadar altına düşmesi gerektiği. Sıfır eğriyi birebir izler ve sınırda fanların dalgalanmasına yol açar. |
| **Tavan yükselebilsin** | Gözlenen hızlardan daha yüksek bir tavanın kendiliğinden saptanması. |
| **Elle ayarlanan fan hızı geri dönmesin** | EC emniyet geri sayımını uzatır. |
| **Uykudayken fan programını askıya al** | Uyku boyunca durdurup dönüşte yeniden başlatır; böylece bayat bir sıcaklığa göre davranarak uyanmaz. |

### Davranış, ekran ve günlük

| Ayar | Notlar |
|---|---|
| **Windows ile birlikte başlat** | Oturum açılışında yükseltilmiş bir zamanlanmış görev kaydeder. |
| **Kayıtlı ayarları açılışta uygula** | Ürün yazılımının bıraktığı duruma razı olmak yerine fan, ekran kartı ve klavye ayarlarını yeniden uygular. |
| **Kapat düğmesi uygulamadan çıksın** | Kapalıyken kapat düğmesi tepsiye gizler. |
| **Pencereyi diğer pencerelerin üstünde tut** | — |
| **İşlemci kısıtlandığında bildir** | Beş dakikada en çok bir bildirim. |
| **Pilde ekran kartını okumayı sürdür** | NVIDIA çipini okumak onu uyandırır; pildeyken bu, kimsenin bakmadığı değerler için güç harcamaktır. |
| **Bu klavyenin dört renk bölgesi var** | Varsayılan olarak kapalı ve her klavyede böyle doğru: tüm klavye tek renk alır. Donanım yazılımına sorulamaz — renk tablosu kart ne olursa olsun dört girdiliktir, bu yüzden tek bölgeli bir Victus da dört bölgeli bir Omen gibi dört bildirir. Yalnızca klavyeniz gerçekten dört ayrı bölge hâlinde yanıyorsa açın. |
| **Yenileme hızı güç kaynağını izlesin** | İki hızla birlikte; elle yazılır ya da ekranın bildirdiğinden alınır. |
| **Ekranı kapat** | Genel bir kısayol; kombinasyona basılarak yakalanır. Tek başına bir tuş kabul edilmez. |
| **Ayrıntılı günlük / dosyaya yaz / BIOS hataları** | Günlük dosyasının baştan başlayacağı boyutla birlikte. |
| **Ne sıklıkta** | Pencere açıkken, gizliyken ve çalışan bir fan programı için yoklama sıklığı. |

## Tepsi menüsü ve Omen tuşu

Bildirim simgesi canlıdır — o anki sıcaklığı, onunla birlikte değişen bir zemin üzerinde çizer. Menüsü, ürün yazılımının gerçekten sunduğu fan kiplerini ve profillerini, klavye arka ışığını ve efektlerini, dili ve pencere açmadan erişmeye değer ayarları taşır.

**Omen tuşu** şunları yapabilir:

* fan programını başlatıp durdurmak — ya da kayıtlı bütün programlar arasında dolaşmak;
* ilk basışta pencereyi göstermek, ikinciden itibaren olağan işini yapmak;
* değişikliği duyurmak yerine sessiz kalmak;
* bunun yerine bir komut çalıştırmak — argümanlarıyla ve istenirse simge durumunda. Bu, fan programının önüne geçer.

## Komut satırı

```
StarMon -<Arg1> [...] [-<ArgN> [...]]
```

| Argüman | Etkisi |
|---|---|
| `-Bios` | Yalnızca bilgi getiren bütün BIOS işlemlerini çalıştırır |
| `-Bios <İşlem>[=<Veri>]…` | Bir ya da daha çok BIOS işlemini, isteğe bağlı parametrelerle yapar |
| `-Ec` | Bütün Gömülü Denetleyici yazmaçlarını tablo hâlinde yazar |
| `-Ec <Yzm>[=<Bayt>]…` | Belirli yazmaçları okur ya da yazar |
| `-Ec <Yzm>(2)[=<Sözcük>]…` | Ardışık yazmaç çiftlerini sözcük olarak okur ya da yazar |
| `-EcMon [DosyaAdı]` | Bütün yazmaçları değişiklik için izler, istenirse dosyaya yazar |
| `-Prog` | Yapılandırma dosyasındaki fan programlarını listeler |
| `-Prog <Ad>` | Birini çalıştırır |
| `-Run <Görev> [<Arg>]` | Zamanlanmış bir görevi konsolsuz çalıştırır |
| `-Task` | Bütün zamanlanmış görevlerin durumunu gösterir |
| `-Task <Görev>[=<Bayrak>]…` | Birini etkinleştirir ya da devre dışı bırakır |
| `-Probe [DosyaAdı]` | Bu makinenin kendisi hakkında söylediği her şeyi Markdown olarak yazar. Salt okunur: firmware'e ve Gömülü Denetleyici'ye değer sorar, ikisini de değiştirmez. Bir kart düzgün çalışmadığında eklenecek dosya budur — içindeki yazmaç dökümü, bu derlemenin makinenizi yorumlaması olmayan tek kısımdır |
| `-SelfTest` | Yerleşik testleri çalıştırır — donanıma dokunmaz |
| `-?` `-H` `-Help` `-Usage` | Kullanım bilgisi |

**BIOS işlemleri:** `Cpu:PL1` `Cpu:PL4` `Cpu:PLGpu` `Gpu` `GpuMode` `Xmp` `FanCount` `FanLevel` `FanMax` `FanMode` `FanTable` `FanType` `Idle` `Temp` `Throttling` `BornDate` `System` `Adapter` `HasOverclock` `HasMemoryOverclock` `HasUndervolt` `KbdType` `HasBacklight` `Backlight` `Color` `Anim`

**Zamanlanmış görevler:** `Gui` (oturum açılışında otomatik çalıştırma) · `Key` (Omen tuşunu yakalama) · `Mux` (Advanced Optimus hata düzeltmesi)

Argümanlar büyük/küçük harfe duyarsızdır ve her biri istenildiği kadar tekrarlanabilir.

## Yapılandırma dosyası

Çalıştırılabilir dosyanın yanına yazılan `StarMon.xml`; ilk oluşturulduğunda baştan sona yorumlanmış gelir. Seçilmiş anahtarlar:

| Grup | Anahtarlar |
|---|---|
| **Fanlar** | `FanLevelMax` `FanLevelMin` `FanLevelAutoDetect` `FanLevelUseEc` `FanLevelNeedManual` `FanProgramDefault` `FanProgramHysteresisC` `FanProgramSuspend` `FanCountdownExtend*` `FanModeKeepAliveMs` |
| **Isıl** | `ThermalProtectionEnabled` `ThermalProtectionHighC` `ThermalProtectionLowC` `ThrottleNotifyEnabled` `TemperatureCacheMs` |
| **Ekran kartı** | `GpuPowerDefault` `GpuPowerSetInterval` `GpuPollOnBattery` |
| **Klavye** | `KbdZoneCount` `KbdColorByTemp` `KbdColorEffect` `KbdEffectSpeed` `KbdIdleOffMinutes` |
| **Omen tuşu** | `KeyToggleFanProgram` `KeyToggleFanProgramCycleAll` `KeyToggleFanProgramShowGuiFirst` `KeyToggleFanProgramSilent` |
| **Ekran** | `RefreshRateFollowPower` `RefreshRateAutoDetect` `PresetRefreshRateHigh` `PresetRefreshRateLow` `DisplayOffHotkeyKey` `DisplayOffHotkeyMods` |
| **Arayüz** | `Language` `GuiCloseWindowExit` `GuiStayOnTop` `GuiDynamicIcon` `GuiDynamicIconHasBackground` `GuiTipDuration` |
| **Zamanlama** | `UpdateMonitorInterval` `UpdateProgramInterval` `UpdateIconInterval` |
| **Gömülü Denetleyici** | `EcFailLimit` `EcRetryLimit` `EcWaitLimit` `EcWaitTimeoutMs` `EcMutexTimeout` `EcMonInterval` |
| **Günlük** | `LogVerbose` `LogToFile` `LogFileMaxBytes` `BiosErrorReporting` |
| **Açılış** | `AutoConfig` `AutoStartup` |

`Language` anahtarı `Auto`, `English` ya da `Turkish` alır. Fan programları aynı dosyanın kendi bölümünde durur ve Soğutma sayfasından düzenlenir.

## Tek çalıştırılabilir, bütün Omen ve Victus'lar

Belirli bir anakarta dair hiçbir şey derlemeye gömülü değildir. StarMon açılışta ürün yazılımına bu makinenin ne olduğunu sorar ve yanıta göre uyum sağlar; böylece aynı çalıştırılabilir dosya, modele özel bir dosya olmadan hem Victus 15'te hem Omen 17'de doğrudur:

* **Fan tavanı** anakartın kendi fan tablosundan gelir ve fanlar daha yüksekte çalışırken görülürse yükselir.
* **Fan seviyelerinin nasıl yazıldığı** — BIOS çağrısıyla mı, yoksa önce elle bayrağı kaldırılarak EC'ye mi — bu anakartın gerçekten desteklediğine göre belirlenir.
* **Sunulan performans profilleri** ürün yazılımının bildirdikleridir. Extreme, sahip olan anakartta görünür, olmayanda gizli kalır; herkese gösterilip yarısında sessizce hiçbir şey yapmaz.
* **Klavye** makinenin kendi klavyesi olarak çizilir: sayısal tuş takımlı ya da takımsız, ISO ya da ANSI, yazılan düzenin tuş yazılarıyla, bir ya da dört renk bölgesinde.
* **Yenileme hızı ön ayarları** sabit 60 / 144 yerine ekranın sunduğu hızlardan gelir.
* **Anakartta bulunmayan sıcaklık algılayıcıları** fark edilir ve saniyede bir EC alışverişine mal olmak yerine ara sıra denenmeye çekilir.
* **İşlemci**, hangisi varsa Intel MSR'leri ya da AMD SMU ısı yazmacı üzerinden okunur.

Yoklanan her şey, her bulgunun kaynağıyla birlikte donanım raporunda listelenir. Ürün yazılımı kötü yanıt veren bir makinede `FanLevelAutoDetect` ya da `RefreshRateAutoDetect` anahtarını `false` yapmak değerleri elle sabitler.

## Kaynaktan derleme

```powershell
.\build.ps1                     # Bin\StarMon.exe derler
.\build.ps1 -Test               # derler, sonra test barındırıcısını derleyip öz testi çalıştırır
.\build.ps1 -Render window      # arayüzün bir parçasını Obj\window.png dosyasına çizer
.\build.ps1 -Configuration Debug
```

Betik düz .NET SDK ile çalışır — Visual Studio gerekmez. Bu projeyi başka türlü zahmetli kılan iki şeyi kendisi halleder:

* SDK'nın birlikte getirmediği **.NET Framework 4.8 başvuru derlemeleri** ve başvuru paketinde hiç bulunmayan `System.Management.Instrumentation`. `net48` üzerinde kalmak nostalji değil: o derleme ile `System.Management`, modern .NET'te NuGet paketleri gerektirirdi.
* `.xaml` dosyalarını C# derleyicisi çalışmadan önce BAML'e çeviren **WPF markup derleyicisi**. Bu, C# hedefleriyle değil WindowsDesktop SDK'sının içinde gelir ve SDK biçiminde olmayan bir proje onu kendiliğinden içe aktarmaz.

### Öz test

Sevkiyat çalıştırılabiliri yükseltme ister; bu da onu test barındırıcısı olarak kullanılamaz kılar. Bu yüzden `-Test`, aynı kaynakları manifest olmadan ikinci kez `Bin\Test` altına derler ve `StarMonTest.exe -SelfTest` çalıştırır. **469 test**, donanıma hiç dokunulmadan: Gömülü Denetleyici'nin yerini G/Ç portlarını modelleyen bir taklit alır; böylece bekle-ve-yeniden-dene protokolü, çalışan bir denetleyicinin hiç üretmediği arıza kipleri dahil sınanabilir. Derleme ayrıca yinelenen bir yerel anahtarda ve iki dilden birinde eksik kalan bir fan kipi ya da ileti anahtarında başarısız olur.

### Tasarım döngüsü

Arayüz, uygulamayı çalıştırarak denetlenemez — yükseltme ister. `-Render <yüzey>` bunun yerine herhangi bir parçasını PNG'ye çizer:

`gallery` `dashboard` `window` `window-en` `window-tr` `window-one` `curve` `keyboard` `keyboard-off` `keyboard-tkl` `log` `system` `sensors` `settings` `picker` `menu` `trayicon`

`window-en` ve `window-tr` dillerini sabitler; çünkü düz `window` yüzeyi, kendisini derleyen makine hangi dildeyse onunla render edilir — Türkçe bir makinede İngilizce yerleşimin aylarca denetlenmeden kalması tam da böyle olur.

### Bilinmesi gereken bir kısıt

Bu projede hiçbir markup, aynı derlemede tanımlı bir tipi adlandıramaz — yerel dönüştürücü örneği, `{x:Type local:…}`, XAML'de özel denetim, hiçbiri. Bunu yapan markup tek geçişte derlenemez; bu proje ise tek geçişte derlenir. Bu yüzden çizilen denetimler code-behind'dan adlandırılmış `ContentControl` yuvalarına yerleştirilir, dönüştürücüler ise uygulama kaynaklarına dize anahtarıyla kaydedilir. Tek istisna `{loc:Str}` markup uzantısıdır.

## Proje düzeni

| Yol | Ne bulunur |
|---|---|
| `App/Cli` | Komut satırı ayrıştırma ve çıktı |
| `App/Gui` | Tepsi barındırıcısı, yoklayıcı bağlantıları, bildirimler |
| `App/Service` | Ölçümler, yoklama, fan denetimi, geçmiş tamponu, ısıl koruma |
| `Hardware` | Gömülü Denetleyici, BIOS çağrıları, NVIDIA, pil, cihaz profili |
| `Library` | Yapılandırma, yerelleştirme, günlük, WMI, dönüşümler |
| `Ui/Theme` | Palet, tipografi, denetim stilleri |
| `Ui/Views` | Sayfalar ve çizilen denetimler |
| `Ui/ViewModels` | Arkalarındaki modeller |
| `Ui/Windows` | Pencere denetleyicisi — ölçümlerle arayüz arasındaki köprü |
| `Ui/Design` | Konsolsuz render yüzeyleri ve örnek verileri |
| `Test` | Öz test paketi |

## Sorun giderme

**Fanlar kendiliğinden otomatiğe dönüyor.**
EC emniyet geri sayımı sıfıra ulaştı. *Elle ayarlanan fan hızı kendiliğinden geri dönmesin* seçeneğini açın ya da her adımda seviyeyi yeniden bildiren bir fan programı kullanın.

**Hangi seviyeyi istersem isteyeyim fanlar hiç durmuyor.**
Sistem sayfasındaki *Fanlar hep açık* satırına bakın. Bu bir BIOS kurulum seçeneğidir ve açıkken StarMon'un yaptığı hiçbir şey onları susturmaz. BIOS kurulum ekranından değiştirilir.

**Ekran kartı taban watt'ının üstüne çıkmıyor.**
`Performans` profilini seçin. Bu sınıf donanımda fazladan ekran kartı gücünü serbest bırakan şey o profildir; tek başına fan ayarı bunu yapmaz.

**Bir denetim soluk görünüyor.**
Ürün yazılımı onu bu anakartta sunmuyor. Sistem sayfası neyin bulunduğunu ve nasıl bulunduğunu listeler.

**Pencerede her yer tire dolu.**
StarMon yükseltilmemiş. İşletim sisteminin altındaki hiçbir şeye kullanıcı kipinden erişilemez.

**Bir şey ters gitti, bildirmek istiyorum.**
Sistem sayfası → *Tam donanım raporu* → **Kopyala**. Sorun yinelenebiliyorsa *Ayrıntılı günlük* açıkken alınan günlüğü de ekleyin.

## Lisans

**StarMon** © 2026 Star, [GNU Genel Kamu Lisansı Sürüm 3](https://www.gnu.org/licenses/gpl-3.0.html#license-text) ile yayımlanmıştır: [Free Software Foundation](https://www.fsf.org/) tarafından yayımlanan bu lisansın koşulları altında yeniden dağıtabilir ve/veya değiştirebilirsiniz. Tam metin `LICENSE.md` dosyasındadır.

StarMon, yine GPL-3.0 ile lisanslı **OmenMon** projesinden kod içerir, © 2023-2024 [Piotr Szczepański](https://piotr.szczepanski.name/). Sürücü kodunun bazı bölümleri Open Hardware Monitor'a dayanır, © 2009-2017 Michael Möller. Uygulama simgeleri ve logo çalışmaları © 2023 Piotr Szczepański (CC BY-NC-ND 4.0); paketlenmiş bütün kaynakların lisansları için `Resources/README.md` dosyasına bakın.

_Bu yazılımın HP ile bağlantısı yoktur ve HP tarafından onaylanmamıştır. Marka adları yalnızca bilgilendirme amacıyla kullanılmıştır._

<div align="right"><a href="#starmon">↑ Başa dön</a></div>
