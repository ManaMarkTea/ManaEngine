# Editor layout plan

Reference: the supplied Stride/Xenko editor screenshot. Preserve Mana's themes and readable controls.

## First pass

- [x] Align the Project folder/asset boundary with the Outliner/viewport boundary while Project is in the bottom dock. Keep standalone/floating Project usable.
- [x] Give assets consistent tile backgrounds, stable selection borders, and explicit type labels.
- [x] Add compact/comfortable thumbnail sizes without changing asset selection or drag/drop behavior.
- [x] Verify headless checks and capture the editor layout.

## Follow-up passes

- Inspector: property/section search implemented; further label/value and component-heading refinement remains.
- Optional collapsible asset preview beneath the Inspector for models, materials and sprites.
- Move transform, snap, camera and gizmo controls into a viewport-local toolbar; retain global save/play actions.
- Review diagnostics placement and narrow-window behavior.

## Acceptance

The folder boundary follows left-column resizing in the standard bottom-docked layout. Asset names/types and selection remain legible, tile size changes keep selection, and existing asset interactions still work. Preserve user dock layouts and authored content.

## First-pass validation

Completed: all 2,922 headless checks pass, including folder-width tracking, tile-size selection preservation, and existing asset drag/drop checks. In-app capture: `DevPhotos/2026-10-01 project-layout.png`. Follow-up passes above remain planned.

## Viewport toolbar

Implemented Camera, Display and Focus menus above the viewport. Camera offers a draggable/editable logarithmic movement-speed slider, six view directions and orthographic projection. Display shares wireframe, normals, gizmo and room-wall settings with existing controls. Focus Selection calls the same framing operation as F and is disabled without a selection. The corner gizmo moves below the toolbar. Transform and snap controls remain follow-ups.

## Inspector search

Added a fixed search field above the scrolling properties. Matching is case-insensitive by property label or section heading. Matching sections open while filtering; clearing restores the prior expansion state. Editors remain retained. All 2,931 checks pass, including filtering and restoration coverage. Capture: `DevPhotos/2026-10-01 inspector-search.png`.
