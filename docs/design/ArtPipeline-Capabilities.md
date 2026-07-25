# Art production pipeline — available capabilities

Written by the coordinator for the art redesign. This records what asset-production routes are
actually available and configured, so implementers pick a real pipeline instead of guessing.

## Unity editor

One Unity Editor serves all agents: `Battle-Plan@bb5dc81e0ad60806`, Unity 6000.3.1f1, URP 17.3.

**The editor lease is held by one named agent at a time.** If you have not been explicitly told in
your task that you hold the Unity lease, do not call any Unity MCP tool. Write files to disk and
report what needs wiring; the coordinator batches editor work.

## AI asset generation (via the Unity MCP bridge)

All three generators import their result directly into the project as a usable Unity asset and
return an `assetPath`. All are asynchronous: `generate` returns a `job_id`, then poll
`status` until `state` is terminal. Provider keys live in the editor's secure store.

> **STATUS: the fal.ai routes are unavailable.** The account balance is exhausted. Both
> `generate_image` and `generate_audio` return
> `403: User is locked. Reason: Exhausted balance` on every submission. `list_providers` still
> reports `configured: true`, because that only checks a key exists in the secure store — it does
> not check the balance. Do not plan around either tool until the balance is topped up at
> `fal.ai/dashboard/billing`. `generate_model` runs on Tripo, a separate provider with its own key,
> and is **unverified** rather than known-good.

| Tool | Provider | Configured | Working | Produces |
| --- | --- | --- | --- | --- |
| `generate_model` | `tripo` | yes | unverified | 3D models, imported as `.glb`/`.fbx`/`.obj`/`.usdz` |
| `generate_model` | `meshy` | no | — | — |
| `generate_image` | `fal` | key present | **no — exhausted balance** | Textures/sprites, supports transparency |
| `generate_image` | `openrouter` | no | — | — |
| `generate_audio` | `fal` | key present | **no — exhausted balance** | AudioClips — both music and SFX |

## What replaced the generated assets

Each fallback turned out to be the better route on its merits, not merely an acceptable substitute.

| Deliverable | Replacement | Why it is better |
| --- | --- | --- |
| SFX and music | Procedural synthesis toolkit (`tools/audio/`) | Every cue is tuned to one key, so the interface cannot fight the score. The whole 82-clip set rebuilds in ~15 s, which absorbed a late tone change for free. |
| Particle sprite masks | Procedural texture generator, editor script | Only the luminance channel was ever going to be used. Exact, parameterised, deterministic, versionable as source, tiny. |
| Deck decals, hazard bands, lane arrows | Procedural texture generator, editor script | Flat two-value graphics are geometry, not pictures. Already in-palette rather than needing a recolour. |
| Backdrop silhouettes | ProBuilder geometry | Flat, distant, single-material. Stays on-palette by construction. |
| Board preview image | Render the real board in-editor | It is then correct rather than approximate, and it stays correct when re-run. |
| Unit portraits | Render the real unit models in-editor | Guarantees the portraits match the units on the field, and a material change propagates instead of silently desyncing. |

**The lesson worth carrying:** procedural generation is not automatically cheap-looking, but it is
cheap-looking *by default*. The audio pass shipped clips that measured as pure sine stacks —
technically correct, audibly thin — until the synthesis was rebuilt on physical modelling. The
texture equivalent is a mask that is too clean. Anything meant to read as smoke, scorch, dust or
wear needs noise, asymmetry and edge break-up; only genuinely geometric shapes should be perfect.

### `generate_model` (Tripo)

Supports `mode: "text"` (prompt only) and `mode: "image"` (`image_path` or `image_url`).
Image-to-3D is the higher-fidelity route: generate a clean reference image with `generate_image`
first, then feed it to Tripo. Useful params: `format`, `target_size`, `texture`, `tier`, `name`,
`output_folder`.

Suitable for: map/cover props, objective markers, projectile and grenade bodies, board-edge set
dressing. Not for unit characters — those meshes are locked.

### `generate_image` (fal.ai)

`mode: "text"` or `mode: "image"` (image-to-image, for restyling an existing asset). Params:
`prompt`, `model`, `transparent`, `width`, `height`, `name`, `output_folder`.

`transparent: true` matters for UI icons and sprite work. Note `remove_background` is **not**
supported in this version and returns an error, so request transparency at generation time.

Suitable for: UI icons, panel and frame art, unit portraits, board preview art, particle and
flipbook textures, trim sheets, noise and gradient ramps, skybox/backdrop art.

### `generate_audio` (fal.ai)

Params: `prompt`, `model`, `duration` (seconds), `name`, `output_folder`. Model choices:

| Model | Use | Limit |
| --- | --- | --- |
| `cassetteai/sound-effects-generator` | SFX | ≤ 30 s |
| `fal-ai/stable-audio-25/text-to-audio` | music and SFX | ≤ 190 s |
| `cassetteai/music-generator` | music | — |
| `fal-ai/lyria2` | music | — |

Omitting `model` uses whatever is selected in the editor's MCP for Unity → Asset Generation tab,
so pass it explicitly for reproducibility.

## Other production routes

- **Procedural geometry in-editor.** `manage_probuilder` is available for blocking out modular
  cover, wall profiles, and board framing parametrically. Preferable to generated meshes wherever
  the shape is simple and must align exactly to the grid.
- **Hand-authored shaders.** `Assets/Shaders/` already holds custom URP shaders
  (`BP_EnergyBeam`, `BP_GroundGlow`, `BP_VisionCone`, `BP_FogOverlay`). There is **no VFX Graph** in
  this project — particles are Shuriken.
- **Materials as text.** `.mat` files are YAML and can be written directly to disk without the
  editor, which keeps material work parallelizable. `manage_material` exists for editor-side edits
  when the lease is held.
- **UI is Unity UI Toolkit** (UXML + USS), not uGUI and not TextMeshPro. Any older doc claiming
  otherwise is stale. USS and UXML are plain text and fully parallelizable.
- **Free third-party assets.** Sourcing from the web is allowed. Anything brought in must be
  license-compatible and the license recorded alongside the asset.

## Verification

`manage_editor`, `refresh_unity`, `read_console`, `run_tests`, and Play mode are lease-only. Play
mode is expensive and is batched into as few sessions as possible; see
`.cursor/rules/minimize-play-mode.mdc`.
