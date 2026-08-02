# Third-party notices

Device Widget for Android includes or links the components listed below. The
project's own code is licensed under Apache-2.0; see `LICENSE`.

## Bundled scrcpy 4.0 for Windows

The Windows build embeds the unmodified official
`scrcpy-win64-v4.0.zip` archive. Its SHA-256 is:

`75DBEB5B00E6F64292F26F70900AE55CA397786BDFB0B9BBEB481A0549047457`

| Component | Version | License | Notice/source |
|---|---:|---|---|
| scrcpy client and server | 4.0 | Apache-2.0 | https://github.com/Genymobile/scrcpy/tree/v4.0 |
| Android Debug Bridge (ADB) | platform-tools 37.0.0 | Apache-2.0 and notices | `licenses/Apache-2.0.txt`, `licenses/ADB-NOTICE.txt` |
| FFmpeg libraries | 8.1.1 (`libavcodec` 62.28.101, `libavformat` 62.12.101, `libavutil` 60.26.101) | LGPL-2.1-or-later | `licenses/LGPL-2.1.txt`, `SOURCE_OFFER.md` |
| libusb | 1.0.29 | LGPL-2.1-or-later | `licenses/LGPL-2.1.txt`, `SOURCE_OFFER.md` |
| SDL | 3.4.8 | zlib | `licenses/SDL-zlib.txt` |
| dav1d | 1.5.3 | BSD-2-Clause | `licenses/dav1d-BSD-2-Clause.txt` |

FFmpeg and libusb are dynamically linked. The FFmpeg build used by the
official scrcpy archive does not enable GPL-only options. Its complete configure
arguments, dependency versions and Windows cross-build commands are preserved
in the scrcpy 4.0 corresponding source archive under `app/deps/ffmpeg.sh` and
`release/build_windows.sh`.

## Desktop .NET dependencies

| Component | Version | License |
|---|---:|---|
| QRCoder | 1.8.0 | MIT |
| Microsoft.Win32.SystemEvents | 6.0.0 | MIT |
| System.Drawing.Common | 6.0.0 | MIT |
| Avalonia, Avalonia.Desktop, Themes.Fluent, Skia, HarfBuzz, Native, Win32, X11, FreeDesktop and Remote.Protocol | 12.1.0 | MIT |
| Avalonia.BuildServices | 11.3.2 | MIT |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | BSD-3-Clause |
| HarfBuzzSharp and native assets | 8.3.1.3 | MIT |
| SkiaSharp and native assets | 3.119.4 | MIT |
| MicroCom.Runtime | 0.11.6 | MIT |
| Tmds.DBus.Protocol | 0.94.1 | MIT |
| System.IO.Pipelines | 8.0.0 | MIT |
| .NET runtime redistributed by self-contained builds | 8.x | MIT and third-party notices |

Relevant license texts and retained copyright notices are in `licenses/`:
`Avalonia-MIT.txt`, `QRCoder-MIT.txt`, `MIT-components.txt`,
`ANGLE-BSD-3-Clause.txt`, `dotnet-runtime-MIT.txt`, and
`dotnet-runtime-THIRD-PARTY-NOTICES.txt`.

## Android companion dependencies

| Component | Version | License |
|---|---:|---|
| Kotlin standard library | 2.2.10 | Apache-2.0 |
| JetBrains annotations | 13.0 | Apache-2.0 |
| OkHttp | 4.12.0 | Apache-2.0 |
| Okio / Okio JVM | 3.6.0 | Apache-2.0 |

The complete Apache License 2.0 text is in `licenses/Apache-2.0.txt` and is
embedded in the APK together with this notice.

## Trademarks

Android is a trademark of Google LLC. This project is independent and is not
affiliated with or endorsed by Google LLC, Genymobile, Microsoft, Apple, or the
other third-party projects named above.
