# Corresponding source and written offer

The Windows release of Device Widget for Android redistributes dynamically
linked FFmpeg 8.1.1 and libusb 1.0.29 libraries as part of the unmodified
official scrcpy 4.0 archive.

Every binary release that contains these libraries is accompanied by these
exact corresponding-source files as GitHub Release assets:

| File | SHA-256 |
|---|---|
| `scrcpy-4.0.tar.gz` | `A62BC2639E1D56B3E7EBAA20D8DEB4947DD02954B3362BDEBE2EF9F7EAE41B00` |
| `ffmpeg-8.1.1.tar.xz` | `B6863ADDE98898F42602017462871B5F6333E65AEC803FDD7A6308639C52EDF3` |
| `libusb-1.0.29.tar.gz` | `7C2DD39C0B2589236E48C93247C986AE272E27570942B4163CB00A060FCF1B74` |

The scrcpy source archive contains the exact dependency download hashes,
configuration flags and Windows build scripts used for the redistributed
official package (`app/deps/*.sh` and `release/build_windows.sh`). No local
patches are applied to scrcpy, FFmpeg or libusb.

If the source assets are ever unavailable, any recipient of the binaries may
request an equivalent machine-readable copy, at no charge other than the
reasonable cost of physical transfer, by opening a public issue in this
repository with the title `LGPL source request`. This written offer is valid
for at least three years after the last distribution of the corresponding
binary version.

Upstream locations:

- https://github.com/Genymobile/scrcpy/releases/tag/v4.0
- https://ffmpeg.org/releases/ffmpeg-8.1.1.tar.xz
- https://github.com/libusb/libusb/releases/tag/v1.0.29
