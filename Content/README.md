# Authored project assets

The editor's **Project** panel browses this folder. Imported source art remains in `Assets`; levels live under `Content/Levels`, and grid map generator rules under `Content/Generators`.

- Right-click an authored node or group in the Outliner and choose **Make Prefab**. That node becomes a linked instance; its name supplies a unique asset filename. Prefabs are saved under `Prefabs` as `.manaprefab` JSON.
- Browse folders and thumbnail tiles in **Project**. Right-click a prefab and choose **Place**, then click the viewport, or drag its tile directly into the viewport. Placement uses the same surface preview, snapping and rotation as Import; Escape cancels. **Delete** removes the source asset while leaving placed instances intact.
- **Edit Prefab** opens the prefab in isolation. Use the Outliner and Inspector to edit its tree. Keep one root and parent new objects beneath it. Select a node to expose transform or serializable component properties, optionally with numeric ranges. The field's source value is its default.
- **Save** (including Ctrl+S) updates loaded instances while preserving placement and overrides. **Save & Close** returns to the level; **Discard** drops edits since the last save. Opening levels and entering Play are disabled during source editing.
- The instance Inspector offers exposed values, individual and whole-instance **Revert**, **Apply to Prefab**, **Refresh**, and **Unpack**. Apply publishes instance changes to the source and updates its other loaded instances. Refresh picks up external source edits. Unpack makes the tree independent.
- Select a grid map and choose **Save Grid Map Preset** to save its palette, tile settings, cell size, collision, and autotile rules. Optionally include current generator rules and settings. Applying a preset copies those settings and preserves existing painted cells and layers, remapping palette indices by asset reference.

Assets and prefab-internal nodes/components have persistent IDs. Moving an asset within `Content` preserves its references. Copying an asset file by hand requires a new asset ID. Keep these files in version control along with the source art they reference.

This first version does not support nested prefabs or variants. Prefab creation, placement, and rebuild operations clear editor undo history because existing inspector commands hold references to replaced nodes. Grid preset application supports undo. The source editor uses the normal node/component inspectors; imported model internals remain owned by their source model.
