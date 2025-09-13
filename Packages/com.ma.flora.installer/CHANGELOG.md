# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [2.0.12] - 2024-07-03
### Fixed
- Fixed prefab not being modified and not getting an `InstancedPrototype` when converted to an instance from the scene context menu.
- Fixed Flora not running when custom pass is disabled on HDRP, Flora will now automatically enable custom passes.
### Changed
- Added an editor only spatial grid for placement so that placed instances are consistent with the grid.
- Added vertex transfer to bypass loading from the indirect buffer and visibility buffer in the fragment shader.
- Completely rewrote the `InstancedTerrainFoliage` load system to be more efficient, faster, and more reliable.

## [2.0.11] - 2024-06-29
### Fixed
- Fixed incorrect define in 2022.3 with URP; passes now use `RenderingData`'s command buffer directly.
- Fixed renderer data being updated outside the render loop.
- Fixed renderer update job not being completed every frame.
- Fixed culling data build job missing the [BurstCompile] attribute.
- Fixed culling data in `InstancedTerrainFoliage` being recreated in `OnEnable`.
- Fixed culling data in `InstancedTerrainFoliage` sometimes resetting when no changes have been made.
- Fixed undo functionality in the `InstancedTerrainFoliage` inspector.
- Fixed layers being null on `InstancedTerrainFoliage` upon creation.
- Fixed shader SH coefficients attempting to set l-value array when `UNITY_INSTANCED_SH` and `UNITY_USE_SHCOEFFS_ARRAYS` are enabled.
- Fixed ShaderGraph patcher inserting duplicate pragmas into each pass.
- Fixed ShaderGraph patcher including procedural instancing in the Meta pass.
- Fixed submesh index accessing invalid materials on renderers when missing.
- Fixed `Convert Instances To GameObjects` not converting all linked objects.
- Fixed `Remove Linked Objects` not destroying child linked objects.
- Fixed `InstancedTerrainFoliage` inspector displaying child LOD instead of the LODGroup.
- Fixed sample scene materials and directional light in HDRP.
- Fixed linked object removal attempting to destroy or remove the instance twice.
- Fixed fill and placement instances not adhering to the spatial hash, causing failure to find a designated container or cell.
### Changed
- Disabled `InstancedTerrainFoliage` detail data serialization; requires a more compact solution.
- Reduced the required disk size of containers' culling data to ~25% of the previous size.
    - Removed unnecessary data from culling data nodes.
    - Serialized culling data as bytes instead of structs to reduce editor size for scenes and prefabs.
    - Serialized transforms with compressed rotation and scale to reduce instanced container size.
- Made detail density a slider to match Unity's UI in the `InstancedTerrainFoliage` inspector.

## [2.0.10] - 2024-06-27
### Fixed
- Fixed null reference error in the fill tool when an object has been destroyed.
- Fixed linked object check methods throwing exceptions instead of just returning null.
- Fixed excluded occluders not being added after filling a mesh.
- Fixed `InstancedMeshContainer` not adding itself back to the global spatial has after being empty.
- Fixed tool undo when an `InstancedMeshContainer` contains linked objects.
- Fixed culling data being reset every time `InstancedMeshContainer`'s transform changes.
- Fixed shadows disappearing when using minimum shadow LOD with cross-fade due to batches combining incorrectly.
- Fixed shader patcher failing when a shader has custom pass includes, now falls back to common names or the last found include.
- Fixed endless loop in picking due to typo.
### Changed
- Added instance count and linked object utilities to instanced mesh container GUI.
- Added linked object instantiate and clear methods to `InstancedMeshContainer`.

## [2.0.9] - 2024-06-26
### Fixed
- Fixed empty containers being added to the global spatial hash.
- Fixed wizard import sample being displayed and enabled before Flora is actually installed.
- Fixed jobs not implementing the legacy batch compatibility layer with `unity.collections` 1.4.0.
- Fixed scheduled job writes exception due to the job not completed before the scene is closed.
- Fixed `unity.collections` package being down-graded on 2022.3. Flora will now only install the package if it's not already installed.

## [2.0.8] - 2024-06-25
### Fixed
- Fixed fill tool filling beyond the terrain bounds.
- Fixed LOD world size being doubled for built culling data.
- Fixed flora shaders losing connection to materials when the importer version changes.
- Fixed linked objects not being excluded from painting when their collider is a child of the linked object.
- Fixed being able to paint on freshly created object links causing stacking.
- Fixed shader patcher missing certain legacy includes and fails.
- Fixed tool render previews rendering as a solid color.
- Fixed containers adding themselves to the runtime spatial hash at their transform position instead of their world bounds center.
### Changed
- Serialize `InstancedTerrainFoliage` culling data to reduce load times.
- Multi-thread renderer updates, joins with uploads and culling.
- Delay rebuilding terrain culling data rebuilds until after the patches been loaded.

## [2.0.7] - 2024-06-24
### Fixed
- Fixed errors with immutable prefabs on instanced terrain foliage: they are now are automatically converted to prefabs.
- Fixed Flora shader patching with HDRP and Amplify.
- Fixed builtin crashing due to indirect args buffer containing non-zero values at initialization.
- Fixed Unity stalling due compiling picking and scene outline passes.
- Fixed undo not working correctly with convert to instances.
- Fixed camera data being rebuilt multiple times per render.
- Fixed selection/picking passes missing required uniforms.
- Fixed allocation error when instance count reaches zero.
### Changed
- Optimized state changes for `Graphics.RenderMeshIndirect` calls.
- Improved shader patching reliability, performance, and caching.

## [2.0.6] - 2024-06-21
### Fixed
- Fixed crash when submitting a large number of materials in a single frame.
- Fixed race condition in the `InstancedTerrainFoliage` load job.
- Fixed hitching in `InstancedTerrainFoliage` when the previous frame's load job is still running.
- Fixed `InstanceBuffer` attempting to allocate a zero-sized buffer.
- Fixed terrain normals being null during load on instanced terrain foliage.
- Fixed container/renderers registering without a valid prototype.
- Fixed debug display keyword not being enabled in cross-fade variants.
- Fixed missing `GetFloraDebugColor` when debug display is enabled on non-procedural materials.
- Fixed LOD transition calculation.
- Fixed GPU cross-fade incorrectly calculating from instance origin instead of AABB center.
- Fixed GPU occlusion incorrectly calculating from instance origin instead of AABB center.
- Fixed possible exceptions in instanced terrain foliage if trees or details haven't been initialized.
- Fixed possible null reference exception in uploader when a renderer has been destroyed mid-frame.
- Fixed missing upload flag in instanced scene data.
### Changed
- Improved LOD selection to match Unity's LODGroup.
- Improved terrain patch loading and overall CPU performance.
- Renamed `GlobalInstancedID` to `InstancedGlobalID`.
- Converted `flora_IndirectInstanceVisibility` buffer to raw format.
- Display the entire inspector for selected prototypes in the prototype overlay.
- Convert immutable prefabs to mutable prefabs when added to a instanced terrain foliage.

## [2.0.5] - 2024-06-20
### Fixed
- Fixed index out of range exceptions caused by cameras with `DisableInstanceRendering` and improved overall camera handling.
- Fixed shader variable collisions with DOTS instancing.
- Fixed `InstancedMeshContainer` not marking the scene as dirty when a culling tree is rebuilt.
- Fixed Flora project settings being added to the preferences' pane.
- Fixed issue where instance renderers were sometimes not unloaded from the system.
- Fixed height display and masking issues in the fill preview.
- Fixed brush rendering issues in HDRP.
- Fixed terrain gather leak caused by unbuilt instance bounds.
- Fixed null reference exception in the material manager when a material is destroyed.
### Changed
- Improved search providers used by the prototype overlay and instanced terrain editors.
- Improved draw call performance by minimizing draw data changes.
- Improved tooltips for `InstancedPrototype`.
- Renamed `InstancedMeshProxy` to `InstancedObjectLink` for better clarity, and made class and container features more intuitive.
- Renamed `InstancePrototype` to `InstancedPrototype`.
- Changed default spatial grid size to 256; TODO: Make this setting adjustable per prototype.
### Added
- Added menu option to convert instance containers into prefab instances.
- Added undo warning for terrain fill operations involving more than 100k instances.
- Added a global density modifier, applied to any prototype instance with `AffectedByGlobalInstanceDensity` and controlled via `GlobalInstanceDensity` on the `InstancingSceneSettings` component.

## [2.0.4] - 2024-06-19
### Fixed
- Fixed incorrect `ResizeArray` implementation that caused memory corruption with terrain foliage.
- Fixed unsupported `Texture2DMSArray` in build occluder depths for Switch builds.
- Fixed debug display shaders in HDRP by correcting the include order.
- Fixed USS warnings related to stylesheets.
### Changed
- Improved main-light selection process for culling.
- Improved property naming and GUI for `InstancedTerrainFoliage`.
- Improved property naming and GUI for `InstancePrototype`.

## [2.0.3] - 2024-06-18
### Fixed
- Fixed issue with the prototype overlay sometimes not accepting valid prefabs.
- Fixed TLS_ALLOC errors caused by Allocator.Temp allocations in the render loop.
- Fixed dangerous `Span<T>` return in `InstancePlacementHash`.
- Fixed overlay collapsed icon styling when overlays are collapsed in a toolbar.
- Fixed `InstancedMeshProxy` source prefab reference being null during play mode.
- Fixed placement tool not spawning instances during mouse drag.
- Fixed previews not loading in the prototype overlay by removing custom `ObjectPreview` for `InstancePrototype`.
- Fixed `IndexOutOfRangeException` when multiple cameras are rendering by adding array resize.
### Changed
- Updated `InstanceSelectionGroup` to target prefab instances and draw their inspectors if available.
- Improved main light selection for culling by generating a hash based on the light's position, direction, intensity, and shadows mode.
### Added
- Added `InstancingSceneSettings` component for scene-wide settings, allowing override of how the render backend selects the main light for culling.

## [2.0.2] - 2024-06-17
### Fixed
- Fixed compile error in 2021.3 due to missing `CommandBuffer.BeginSample(ProfilerMarker)`.
- Fixed errors on Apple Silicon caused by ShaderGraph patching.
- Fixed leak in `InstancePrototype`.
- Fixed shader errors when DOTS is enabled.
- Fixed `LocalKeyword` errors when the keyword doesn't exist.
- Fixed selection groups persisting in play mode.
- Fixed view tool not working with paint tools on Mac.
- Fixed selection errors when the shader hasn't finished compiling.
- Fixed index out of range error when picking an instance.
- Fixed pressure application in the brush tool.
- Fixed UI sliders not changing to horizontal when in horizontal or panel layout.
- Fixed issue where instance toolbar/tools were not appearing in 2021.3.
- Fixed adding prefab scene instances to the prototype overlay view to add the corresponding prefab asset.
- Fixed material manager error with a large number of materials.
- Fixed null error in `InstancedMeshProxy` after deserialization.
- Fixed placement tool not randomizing the rotation.
### Changed
- Renamed `InstancingCameraData` to `InstancingCameraSettings`.
- Added option to disable `InstancedMeshProxy` gizmo icons in the scene view.
### Added
- Added LOD cross-fade duration option in `InstancingCameraSettings`.
- Enabled converting hierarchies of prefabs to instanced containers.

## [2.0.1] - 2024-06-14
### Fixed
- Fixed "com.ma.core" inclusion in the Asset Store package.
- Fixed "com.ma.core" reference version.
- Fixed missing debug display keyword if it doesn't exist.
- Fixed instanced terrain loading loop.
- Fixed leaks in the brush tool.
- Fixed `InstancedMeshProxy` errors during construction and destruction.
### Changed
- Added `InstancedMeshContainer` creation menu items to the GameObject's right-click context in the scene menu (Create/Split/Combine).
- Added `Convert to Instanced Shader` material context menu item.
- Added batch AutoShader patching, which checks for existing patches and only creates new ones.

## [2.0.0] - 2024-06-13
### Changed
- Deprecated `FloraPrototype`, `FloraContainer`, `FloraInstanceData`, `FloraCamera`. Use the upgrade button to convert existing data to the new system.
### Added
- Implemented performance optimizations resulting in up to 5x faster performance.
- Added GPU Accelerated Features: Indirect Culling, Instanced Properties, Instance Streaming, Occlusion Culling (Hi-Z), Dynamic Density.
- Added CPU Occlusion Culling support for Unity's baked occlusion culling system.
- Added LOD Cross-fading with animated and transition-based cross-fading.
- Added `InstancedTerrainFoliage` component for instancing foliage on terrain with streaming support.
- Added support for instanced properties similar to Unity's DOTS system.
- Added automatic shader patching for Shader Graph materials.
- Achieved full compatibility with Unity 6.0.
- Added new components: `InstancePrototype`, `InstancedMeshContainer`, `InstancedMeshProxy`, `InstancedTerrainFoliage`, `InstancingAPI`, `InstancingCameraData`, `GlobalInstanceID`.
- Enhanced prefab support for streamlined asset workflows and scene composition.
- Added global spatial hash for improved instance querying and management.
- Added a new tab-based global tool UI.
- Added new brush tools: Instance Scale Brush, Instance Placement Brush, Instance Property Brush.
- Enhanced support for built-in transform and selection tools.
- Added `InstanceToolContext` for improved workflow and tool interaction within Unity.

## [1.1.9] - 2024-05-09
### Fixed
- Fix `FloraInstanceRenderData` initialization and errors on deserialization.
- Fix `FloraModelInfo` LOD size calculation.

## [1.1.8] - 2024-05-07
### Fixed
- Fix `FloraInstanceController` serialization crash during play mode.

## [1.1.7] - 2024-05-06
### Fixed
- Fix AxisAlignedBox transformation; renamed method 'Transform' to 'TransformBy'.
- Fix node culling with large BVHs.
- Fix submeshes sometimes being omitted from the batcher.
### Changed
- Optimized batch sorting.

## [1.1.6] - 2024-05-03
### Fixed
- Fix multiple issues with the install wizard, and make it more robust.
- Fix `FloraInstanceRenderer` not always correctly removing prefab instances.
- Fix 'FloraFillTool' throwing physics main thread errors when 'Check Collisions' is enabled in the prototype.

## [1.1.5] - 2024-03-20
### Fixed
- Fix rendering issues with Builtin renderer.
- Fix ShaderGraph as required package.
- Fix `FloraContainer` `Static GameObjects Only` option not updating correctly.

## [1.1.3] - 2024-01-26
### Added
- Added additional culling tree debug mode (Depth Visualization).
### Fixed
- Fix Debug Window error on 2022.3.
- Scene never saves temporary colliders in edit mode.

## [1.1.2] - 2024-01-24
### Fixed
- Fix HDRP compile error.
- Fix UnsafeUtility.MallocTracked compile error on early versions of 2021.3.
- Fix dynamic colliders sometimes not being re-enabled in a scene when the scene is closed in edit mode.

## [1.1.1] - 2024-01-04
### Fixed
- Fix "Collision With World" prototype placement option.
### Changed
- Added "Overhang" checkbox to the "Collision With World" painting option. Ensures instances do not extend beyond the painted surface.

## [1.1.0] - 2023-12-26
### Added
- Add collision layer mask for "Collision With World" painting option.
- Add "Nothing" option to layer mask painting option.
### Fixed
- Fix null reference errors when painting with a Single Instance mode.
- Fix terrain layer mask not being applied correctly during painting.
### Changed
- Remove "Add Occluders" option (always enabled)
- Remove "FloraMainLight" component (never used)
- Update Core library + prep for new renderer

## [1.0.10] - 2023-09-15
### Added
- Add install wizard for Flora: `com.ma.flora.installer` package.
### Fixed
- Fix crash during GPU uploads.
- Fix undo/redo edge cases.
- Fix scene always being set dirty after a tree rebuild.
### Changed
- Move Flora and Core to the packages folder.
- Demo scene is now in the `com.ma.flora` `Samples~` folder. Can be installed via the Package Manager.
- Redesigned render data uploads to be more efficient and stable.
- Redesigned adding/removing/updating instances - up to 1000% faster.
- Separated `FloraInstanceData` and `FloraInstanceCollection` assets.
- Improved handling of Flora instance data assets. Deferred saves with the scene + automatically clean up old or invalid assets.
- Improved terrain fill and heightmap change performance.

## [1.0.9] - 2023-08-28
### Fixed
- Fix `FloraCullingTree` throwing allocator exception on a synchronous build.
- Fix `FloraCullingTree` possible leak.
- Fix `FloraPrototype` asset preview warnings and backgrounds.
- Fix `FloraPrototype` search warnings.
- Fix `FloraContainerEditor` UI actions.
- Fix missing `unity_SpecCube0_HDR` in HDRP.
### Changed
- Improved instance data uploading performance and memory usage.
### Added
- Add indirect instancing support.
- Add billboard helper nodes for ShaderGraph.

## [1.0.8] - 2023-08-24
### Fixed
- Fix crash when using undo/redo.
- Fix multiple instance is destroyed errors when using undo/redo.
- Fix `FloraInstanceRenderer` `RemoveInstances` not correctly sorting the removal indices.
- Fix `FloraCullingTree` async building with an out of date transform list.
- Fix missing `unity_SpecCube0_HDR` in HDRP.
### Changed
- Change `FloraInstanceData` transform type from `float4x4` to `MA.Core.LocalTransform`. This leads to a slight decrease in memory and disk usage.

## [1.0.7] - 2023-08-22
### Fixed
- Fix `FloraShaderPatcher` ShaderGraphImporter on 2022.3.
- Fix `TryGetCellAtLocalPosition` in `FloraContainer` returning the wrong cell.
- Fix `FloraContainerEditor` replace prototype.
### Changed
- Major UI overhaul. The UI is now built using the new UI Toolkit.
- Rename namespace `MAEditor.Core` to `Ma.Core.Editor`.
- Improve `FloraCellContainer`enumeration methods.
- Improve `FloraInstanceController` serialization.

## [1.0.6] - 2023-08-11
### Added
- Add new auto shader patching asset system. Create an auto shader patch by right clicking in the project window and selecting `Create > Flora > Flora Auto Shader`.
- Add `Replace` to the `FloraContainer` prototype list context menu. This will replace the selected prototype with another while maintaining the instance data.
### Fixed
- Fix player build regression, missing UNITY_EDITOR ifdef in `FloraInstanceController` and `IFloraInstanceRenderer`.
- Fix brush preview casting shadows.

## [1.0.5] - 2023-08-09
### Fixed
- Fix `FloraInstanceController` not assigning the prefab model when instances are added to a new `FloraContainer`.
- Fix null reference errors in `FloraPrototypeEditor`.
### Added
- Add `FLORA_VERSION` define to `Flora.hlsl`
- Add job manager for `FloraCullingTree` async job build.

## [1.0.4] - 2023-08-07
- Fix `FloraTool` not capturing mouse events when no prototypes are active.
- Fix `FloraToolAttributes` throwing divide by zero errors when no prototypes are active.
- Fix `FloraParentCache` containing null references when a parent GameObject is destroyed while a container is disabled.
- Fix `FloraInstanceController` throwing invalid instance id errors if not initialized in order.
- Fix `FloraInstanceRenderer` bounds calculation in `UpdateInstanceTransform`.
- Fix draw call batch size calculation in `FloraRenderSystem`.
### Added
- Add automatic maximum constant buffer size calculation to 2022.3+.
### Changed
- Remove required shader defines for Flora attributes and Light Probe support. Values are now calculated at runtime.
- Make the input of the `FloraInstaningSetup` ShaderGraph node optional. Will default to the Vertex Position if not connected.
- Only show debug frame timing while playing (inaccurate in edit mode).

## [1.0.3] - 2023-08-03
### Fixed
- Fix `FloraBrushTool` null reference error with the brush preview.
- Fix `FloraInstanceController` throwing null reference errors when entering edit mode.
- Fix `FloraTool` incorrectly setting the pen pressure.
- Fix compile warnings.
### Added
- Add `IFloraInstanceRenderer` interface for custom instance renderers.

## [1.0.2] - 2023-08-01
### Fixed
- Fix compile errors on Mac with Unity 2022.3
- Fix constant buffer size on switch and certain mobile platforms.
- Fix `FloraCell` world bounds property calculation and gizmos when `Show Cells` is enabled.
### Added
- Add event pressure for tablet support with the brush tools in 2022.1+. Currently only works with density (more options to come).
- Add Amplify Shader Editor support with custom Flora nodes.
- Add a preview when using single instance mode with the paint tool.

## [1.0.1] - 2023-07-23
### Fixed
- Fix `FloraInstanceFillTool` tool throwing Burst errors when filling meshes.
- Fix `FloraInstanceData` names not matching their file names on disk.
- Fix `FloraCullingTree` async build not running consistently in the editor.
- Fix `ToolbarSearchCancelButton` style sometimes not being found depending on the Unity version.
- Fix compiler errors when `Unity.Collections` version is >`2.0.0`.

## [1.0.0] - 2023-07-21
- Initial release
