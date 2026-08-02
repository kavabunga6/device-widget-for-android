# Corresponding-source manifest

Source archives are deliberately published as release assets instead of being
duplicated in Git history. Before publishing a release, run
`tools/build_release.ps1`; it verifies that the exact archives named in
`SOURCE_OFFER.md` exist and match their pinned SHA-256 values.

These archives correspond to the unmodified official scrcpy 4.0 Windows
bundle stored in `vendor/scrcpy-win64-v4.0.zip`. Build configuration is in the
scrcpy archive under `app/deps/` and `release/`.
