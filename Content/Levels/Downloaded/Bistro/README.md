# Amazon Lumberyard Bistro

These level files preserve the imported scene nodes, authored lighting, camera poses and overrides. Large assets are
not committed. Open a level normally: its `downloads` block fetches `Downloaded/Bistro_v5_2.zip`, verifies SHA256, and
imports it into `Library/Imported/Bistro_v5_2/`. The first import also fetches FBX2glTF and takes a few minutes.

Each download declaration has a ZIP `file` name, an HTTPS `url`, and a required `sha256`. Existing local ZIPs are verified
too. A failed download leaves no installed partial ZIP; reopening retries it. Reopening an unchanged pack reuses the
import. Saving or Save As retains the download declarations along with the authored nodes.

Web builds use the converted glTF buffers, textures and sky, downloaded individually through the existing hashed asset
catalog. Run `dotnet run --project ManaVoxel -- --prepare-level-assets` before publishing locally; the daily workflow
does this automatically. The browser never downloads the FBX source ZIP or runs FBX2glTF. The five scenes appear in the
web selector. These are large benchmark scenes; the selector describes the initial asset download.

Source: [Amazon Lumberyard Bistro, Open Research Content Archive (ORCA)](https://developer.nvidia.com/orca/amazon-lumberyard-bistro),
Amazon Lumberyard, July 2017. Licensed under [Creative Commons Attribution 4.0](https://creativecommons.org/licenses/by/4.0/).
Mana converts FBX to glTF, reconstructs materials, recentres mesh pivots and supplies engine lighting/probes.

The web texture copy starts at the existing 512 px mip; desktop retains the original full-resolution DDS chain.
