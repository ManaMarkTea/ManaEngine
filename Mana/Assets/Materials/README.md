# Compact material packs

In the Inspector, expand a mesh's Material section and choose its **Preset**.
Each ZIP assigns its surface maps together. Individual map slots remain editable.
Material choices and maps are saved with shapes and imported meshes.

The 21 bundled presets use maps up to 512px and total about 15.7 MiB. Search by category in the picker. The PBR Gallery level shows every preset on a shader ball.

| Category | Packs |
| --- | --- |
| Metals | gold, silver brushed steel, aluminum, copper, rusted corrugated steel |
| Wood | oak, pine |
| Stone | white marble, granite |
| Masonry | plaster, ceramic tile |
| Ground | sand, grass, soil |
| Fabric | denim, brown leather |
| Plastic | black plastic, scratched blue plastic |

Also included: dark wood parquet, black brick wall and concrete. Sources and CC0 notes are included inside each ZIP; the complete download manifest is `Tools/Materials/starter-packs.json`. Silver is silver-colored brushed steel. Aluminum uses constant metalness 1 and roughness 0.28 because its original pack used specular shading. White marble uses roughness 0.2. Normal maps are renormalized after resizing; height and specular maps are omitted.

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

Rebuild the categorized starter set (originals stay in ignored Artifacts):

```powershell
python Tools/Materials/build_starters.py --download
```

Only use assets whose redistribution license you have checked. Pass `--creator` to the compactor for the source author; its license note assumes CC0.
