# Compact material packs

In the Inspector, expand a mesh's Material section and choose its **Preset**.
Each ZIP assigns its surface maps together. Individual map slots remain editable.
Material choices and maps are saved with shapes and imported meshes.

The bundled 512px packs total about 1.7 MiB:

| Pack | Source |
| --- | --- |
| Dark wood parquet | https://cc0-textures.com/t/st-dark-wood-parquet-18 |
| Black brick wall | https://cc0-textures.com/t/st-black-brick-wall-15 |
| Concrete | https://cc0-textures.com/t/st-concrete-19 |

These Share Textures assets are public domain / CC0, as stated on each source page.
Each ZIP includes its source and license note. Normal vectors were renormalized after
resizing. Height and specular maps are omitted. Concrete has no roughness map and uses
an adjustable roughness of 0.8.

Add licensed material ZIPs to `Mana/Assets/Materials/` (bundled with builds), or
`Assets/Materials/` (local project packs). Open the picker again to discover new packs.
Use distinct ZIP filenames. Images may be nested in folders; map filenames should
contain `albedo`, `basecolor`, `diffuse`, `normal`, `roughness`, `metallic`,
`metalness`, `ao`, `ambientocclusion`, `emission`, or `opacity`, separated by
underscores or hyphens. Use OpenGL normals. Specular maps are not metalness maps.

To make another compact pack after checking its source license:

```powershell
python -m pip install Pillow numpy
python Tools/Materials/compact.py input.zip Mana/Assets/Materials/new-pack.zip --source https://cc0-textures.com/t/source-page --size 512
```

The compaction script's license note assumes Share Textures / CC0; update it when
using a different source. Keep original downloads under `Artifacts/`, outside Git.
