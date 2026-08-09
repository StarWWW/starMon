# StarMon Resources

This directory holds the non-_GPL3_ resources bundled with **StarMon**. These items come with their own licenses, which are compatible with being distributed alongside the application.

# Driver.sys.gz

**_WinRing0_ driver binary from [OpenLibSys](https://openlibsys.org/manual/WhatIsWinRing0.html)**
* Copyright © 2007-2010 OpenLibSys & Noriyuki Miyazaki
* Licensed under the terms of the [Modified BSD License](https://openlibsys.org/manual/License.html)

# LpcACPIEC.bin, IntelMSR.bin, AMDFamily17.bin

**_PawnIO_ modules from [PawnIO_Modules](https://github.com/namazso/PawnIO_Modules)**
* Copyright © 2023-2025 namazso
* Licensed under the terms of the [LGPL-2.1-or-later](https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html)

Unmodified binaries, taken from release `0.2.10` of that project. They are small verified programs that run inside the signed _PawnIO_ driver, which loads on machines where _WinRing0_ is blocked by Microsoft's vulnerable-driver list. `LpcACPIEC` permits I/O ports `0x62` and `0x66` and nothing else; `IntelMSR` and `AMDFamily17` each permit a fixed list of processor registers and refuse a processor they are not for.

They must be byte-for-byte as published: the driver verifies each module's signature and will not run one that has been altered.

| Module | SHA-256 |
|:--|:--|
| `LpcACPIEC.bin` | `C38FD116E7AFF4D1FDB0A494E296BE0A6708E5A22FC72F14587442FB7F8F7906` |
| `IntelMSR.bin` | `D6ED85D65AB17A22F813EF98207D6D537155EE2DED5976A21CB48413C9B92E5F` |
| `AMDFamily17.bin` | `DAE74615761B78BDF064DFB3E136252DDCC6FC727D88F14738D0E5800D427A91` |

# Icon*.ico, Keyboard*.png, Logo.png

**Logo artwork, icons, and keyboard-layout diagram**

* Copyright © 2023 [Piotr Szczepański](https://piotr.szczepanski.name)
* Licensed under the terms of the [CC BY-NC-ND 4.0](http://creativecommons.org/licenses/by-nc-nd/4.0/)
* Artwork designed in [Inkscape](https://inkscape.org/) and converted to the `.ico` format with [icoutils](https://www.nongnu.org/icoutils/)

# IoMon.ttf

**A variation of the [Iosevka](https://be5invis.github.io/Iosevka) typeface**
  * Copyright © 2015-2023 [Renzhi Li](https://typeof.net/) (aka Belleve Invis)
  * Licensed under the [SIL Open Font License 1.1](https://scripts.sil.org/OFL)

Used to display some of the figures (numbers), including the temperature dynamic notification icon.

Modifications include in particular:
  * Removal of unused glyphs, glyph variants and substitution tables to reduce size from 9,437 kB to 29 kB
  * Reduction of horizontal spacing between glyphs to allow for more information density
  * A customized glyph for **℃** _Degrees Celsius_ `U+2103`
  * Modified with [FontForge](https://fontforge.org/)

The font also includes the following whitespace sizes:
| _En_ | _Em_ | _3-Per-Em_ | _4-Per-Em_ | _6-Per-Em_ |
|:----:|:----:|:----------:|:----------:|:----------:|
| > <  | > <  | > <        | > <        | > <        |
