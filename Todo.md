[unverified] Press the END key to drop the item onto the floor or other objects. - Writen by copilot, verify its good.

[] Add Support for 2d gameplay (Sprites, platforms, sprite sheet animations, sprite lighting and normals, polygon colliders, etc)

[] Road generator - There is a city pack with road pieces, it would be nice to be able to drag out a road and have it auto-generate the correct nodes under it dynamically.

[unverified] Similar to our grid map floating tool, lets move all the voxel arrows and stuff into a tool panel that shows up when selecting a voxel node.

[x] Wave function collapse to populate a dungeon from the kenney dungeon assets, but have it be configurable with some JSON assets and a Window Editor for it.

[] Add support for UI from JSON or xaml-ish.  And an editor for it. (Like UMG in unreal)

[] Glow component (next up): levels and prefabs don't save material settings, so emission set in the Inspector is lost on save. Add a "Glow" script (colour, strength, optional light range) that sets emission on the meshes beneath it and adds a small point light, so a "Glowing Coin" is a Coin variant with a Glow added. Strength above 1 blooms.


[x] Review Changes for prefab variants: the review window works on placed copies; a variant open in the prefab stage could use it too.

[] ManaVoxel\Code\GameModes\VoxelEditorGameMode.cs has A LOT of stuff it shouldn't have, planet stuff mostly, that all needs to be split out into game mode components, or some other class.
