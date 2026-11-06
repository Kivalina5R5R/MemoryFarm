using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Deepspace
{
    // Bridges the existing 2D UI-rect gameplay (hitbox/formation/wander math, all untouched)
    // to the 3D Synty models บาส placed as scene templates (Enemy_Basic/fast/Tank/Shooter,
    // Boss_ship, Playership 1-4). A follower is a clone of a template parented under the SAME
    // RectTransform (playArea/cutsceneArea) as the RectTransform it tracks — sharing a parent
    // means whatever Unity does internally to keep a Screen Space Overlay Canvas screen-aligned
    // applies identically to both, so plain localPosition/localScale math lines up with the UI
    // rect without this code needing to know real display pixel size or camera framing tricks.
    public sealed class Visual3DFollower : MonoBehaviour
    {
        // Two separate layers, one per display, instead of one shared layer — Screen Space
        // Overlay canvases can end up at coincidentally similar/overlapping world transforms
        // (this is an internal Unity detail this code deliberately doesn't rely on), so the
        // wall and floor cameras' frustums aren't guaranteed to stay geometrically separate.
        // A single shared culling layer meant either camera could pick up objects that were
        // still conceptually on the OTHER canvas — this is what showed up as an enemy still in
        // wall formation appearing to poke into the floor display. Giving each canvas its own
        // layer, and switching a clone's layer the instant it reparents, makes "which display
        // this renders to" exact and independent of any spatial coincidence.
        public const int WallLayer = 8;
        public const int FloorLayer = 9;
        // Decorative background (the astroid field) needs to always render BEHIND every
        // WallLayer/FloorLayer object — ships, bullets, explosions — permanently, regardless of
        // where any of them happen to sit (บาส 2026-08-21: chased this through several failed
        // Z/Y position attempts before landing here). Position/sortingOrder can't guarantee that
        // against opaque Mesh-mode particles (real Z-testing, not sortingOrder-driven — see
        // ConfigureBackgroundGameplayEffects), but CAMERA STACK ORDER can: giving the astroid
        // field its own layer that's NOT in the "3D Visuals" Overlay camera's cullingMask means
        // it only ever renders through the BASE camera pass, which draws strictly before every
        // Overlay camera in the stack (3D Visuals, UI Priority) — so it's behind ships/bullets/
        // explosions unconditionally, by construction, no position math involved. It still reads
        // as clearly in front of BG (the Skybox, infinite depth, always loses to any real
        // geometry) since the base camera already draws it after clearing to Skybox.
        public const int BackgroundLayer = 16;
        // Unity's built-in "UI" layer (fixed index, every project ships with this one already
        // named/reserved — not something this codebase defines). Referenced here just so the
        // UI-priority overlay camera setup (see SetupVisual3D) has one named constant instead of
        // a bare magic number at its call site.
        public const int UILayer = 5;

        // One dedicated layer per floor player zone (10-15, room for up to 6 players) — a
        // one-shot burst (explosion) or a fast in-flight trail (missile) can visually spread
        // past its own zone's width, and layer alone can't stop that (a layer only decides
        // WHICH camera can see an object, not WHERE on screen it's allowed to draw). Pairing
        // this layer with a per-zone camera whose Viewport Rect is clipped to that zone's
        // screen slice (see Visual3DCameraRig.Create's viewportRect param) is what actually
        // enforces the "stays in your own lane" request (บาส 2026-08-21) — same split-screen technique any
        // local-multiplayer game uses to keep one player's view out of another's.
        public const int ZoneVfxLayerBase = 10;
        public const int MaxZoneVfxLayers = 6;

        // บาส 2026-08-26: Boss needs to render BEHIND UI (the "Stage X-X/X" banner, HP bar)
        // no matter what, same as the astroid field needs to render behind ships — and the
        // astroid field's own trick (BackgroundLayer above) is the proven way to do that in
        // this project: keep an object OFF the "Wall 3D Visuals" Overlay camera's cullingMask
        // (that camera only culls exactly `1 << WallLayer`, so any other layer is automatically
        // excluded) so it only ever renders through the WALL BASE camera's pass instead —
        // the same pass "Canvas == Cutscene" (and therefore all its UI) is assigned to render
        // through. Giving Boss its OWN layer, excluded from that Overlay entirely, means Boss
        // only ever renders in the SAME single pass as the UI Canvas — where normal
        // opaque-then-transparent draw order (Boss's material is Opaque queue, UI is
        // Transparent queue) puts UI on top by construction, no Z-position trick needed at all.
        //
        // At the time this was written, regular ships on WallLayer were ALSO being rendered a
        // second time by the Overlay camera on top of the base pass — wallBaseCamera's own
        // cullingMask was every layer except FloorLayer, so it drew WallLayer content itself
        // before the Overlay repainted it, showing as two overlapping copies of the same ship
        // (e.g. the ship-select preview models). Fixed 2026-08-28 in SetupVisual3D by also
        // excluding each base camera's own WallLayer/FloorLayer, so the dedicated Overlay is the
        // only one drawing it — this paragraph's Boss-specific reasoning is unaffected since
        // Boss was never on WallLayer to begin with.
        public const int BossLayer = 17;

        public static int ZoneFloorLayer(int zoneIndex) =>
            ZoneVfxLayerBase + Mathf.Clamp(zoneIndex, 0, MaxZoneVfxLayers - 1);

        RectTransform source;
        RectTransform wallArea;
        float wallLocalScale;
        Vector3 templateLocalScale;
        Quaternion templateLocalRotation;
        bool preserveTemplateScale;
        int floorZoneIndex = -1;
        // บาส 2026-08-26: dragged Boss_ship(Clone)'s own Z gizmo in the Scene view during Play
        // until UI stopped getting covered, landing on local Z=267 — set only via
        // DeepspaceGameManager for Boss (now on its own BossLayer, sharing the wall base
        // camera's single pass with the UI Canvas), left at 0 for every other model.
        public float extraLocalZ;
        // Same idea as extraLocalZ, on Y instead — added for
        // MemoryFarmGameManager's farmer model, which needed a fixed upward
        // nudge off its tracked RectTransform's own Y (see AttachFarmerModel).
        // 0 for every follower that never sets it — no behavior change for
        // anything else.
        public float extraLocalY;
        // บาส 2026-08-26: bank/tilt angle (degrees), set every frame by EnemyShip.UpdateTilt
        // off its own horizontal velocity — composed on top of templateLocalRotation (the
        // model's own final "facing" orientation) rather than before it, so it rolls around
        // whatever axis reads as nose-forward AFTER that per-kind correction, same reasoning
        // PlayerShip.UpdateTilt's own comment gives for composing tilt in that order. 0 for
        // every follower that never calls SetExtraTilt (projectiles, VFX, etc.) — no behavior
        // change for anything but enemy ships.
        public float extraTiltDegrees;
        // Composed on Y (the coin's own spin axis, see coinGenerator.js in the
        // Fortal3DS repo) AFTER templateLocalRotation, same ordering reasoning
        // extraTiltDegrees already gives for its own Z-axis composition — added
        // for BountyHunterGameManager's collect-flip celebration (a one-shot
        // 360° spin driven by a coroutine each frame it plays). 0 for every
        // follower that never sets it — no behavior change for ships/gems/VFX.
        public float extraSpinDegrees;
        public Quaternion extraViewRotation = Quaternion.identity;
        // Multiplies the follower's own scale (wallLocalScale, or
        // templateLocalScale under preserveTemplateScale) — same
        // collect-flip use as extraSpinDegrees, needed because LateUpdate
        // below already fully owns transform.localScale every frame; setting
        // it directly from outside would just get overwritten the very next
        // frame. Defaults to 1 (no-op) for every other caller.
        public float extraScaleMultiplier = 1f;

        // sizeMultiplier defaults to 1 (untouched) for ship/enemy models, which are already
        // sized correctly off the template mesh — VFX call sites pass DeepspaceGameManager's
        // vfxSizeMultiplier instead, since particle effects read as smaller than a solid mesh
        // at the same transform scale and บาส wanted them punchier without re-scaling every
        // individual VFX prefab by hand.
        //
        // floorZoneIndex (default -1) routes the clone through its owning player's dedicated
        // ZoneFloorLayer instead of the shared FloorLayer whenever it's on the floor — only
        // meaningful for effects that can visually spread beyond their spawn point (missile/
        // explosive trails); ship/enemy models pass -1 (unchanged, shared FloorLayer) since a
        // model tracks one rect and never spreads past its own zone regardless.
        public static Visual3DFollower Attach(Transform template, RectTransform parent, RectTransform source, RectTransform wallArea, float sizeMultiplier = 1f, Color? tint = null, bool preserveTemplateScale = false, int floorZoneIndex = -1)
        {
            if (template == null || parent == null || source == null)
            {
                return null;
            }

            var clone = Instantiate(template, parent);
            clone.gameObject.SetActive(true);
            clone.localRotation = template.localRotation;
            clone.localScale = template.localScale * sizeMultiplier;

            clone.localPosition = new Vector3(source.anchoredPosition.x, source.anchoredPosition.y, 0f);

            if (tint.HasValue)
            {
                // Recolors every particle system in the clone (recursive — same reasoning as
                // SetLayerRecursive below: VFX prefabs commonly carry child sub-emitters) instead
                // of baking one fixed color into the shared template, so the same prefab reads
                // correctly whichever caller's actual runtime color it's standing in for.
                foreach (var ps in clone.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.startColor = tint.Value;
                }
                // Same idea for a plain (non-vertex-colored) mesh — added for
                // MemoryFarmGameManager's farmer shirt, which needs a runtime
                // color per farmerTypes on a PLAIN material rather than
                // baking 5 separate exports the way the vertex-colored
                // models do (see humanGenerator.js's generateFarmerRig for
                // why the shirt is its own mesh/material specifically so
                // this works — Fortal/VertexColorLit's own Properties block
                // is empty by design, so setting .color on a material using
                // that shader is a harmless no-op, not a conflict). Every
                // other Attach caller passes tint: null, so this is a purely
                // additive, opt-in path.
                foreach (var renderer in clone.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderer.material.color = tint.Value;
                }
            }

            var collider = clone.GetComponent<Collider>();
            if (collider != null)
            {
                // The Box Collider was only ever there so บาส could read mesh bounds in the
                // Inspector while sizing these — no physics/raycasts happen in this UI-driven
                // game, so leaving it enabled on live clones would just be dead weight.
                collider.enabled = false;
            }

            var follower = clone.gameObject.AddComponent<Visual3DFollower>();
            follower.source = source;
            follower.wallArea = wallArea;
            follower.wallLocalScale = template.localScale.x * sizeMultiplier;
            follower.templateLocalScale = template.localScale;
            follower.templateLocalRotation = template.localRotation;
            follower.preserveTemplateScale = preserveTemplateScale;
            follower.floorZoneIndex = floorZoneIndex;
            follower.ApplyLayer(parent == wallArea);
            return follower;
        }

        void ApplyLayer(bool onWall)
        {
            int layer = onWall ? WallLayer : (floorZoneIndex >= 0 ? ZoneFloorLayer(floorZoneIndex) : FloorLayer);
            SetLayerRecursive(gameObject, layer);
        }

        // Recursive because VFX prefabs (unlike the single-mesh enemy/ship models this was
        // originally written for) commonly carry child sub-emitters — setting the layer on
        // just the root would leave those children on the prefab's original layer, invisible
        // to both dedicated cameras (wrong layer) and picked up by Main Camera instead (wrong
        // display) despite the parent being correctly assigned.
        internal static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        public void SyncPose() => LateUpdate();

        void LateUpdate()
        {
            if (source == null)
            {
                Destroy(gameObject);
                return;
            }

            // Enemies/projectiles reparent their tracking rect from cutsceneArea to playArea
            // when they cross from the wall to the floor (see EnemyShip/PrototypeProjectile's
            // CrossToFloor) — follow that reparent (and flip layer to match) so the clone always
            // renders through the camera for whichever display it's actually on.
            if (transform.parent != source.parent)
            {
                transform.SetParent(source.parent, false);
                ApplyLayer(wallArea != null && source.parent == wallArea);
            }

            transform.localPosition = new Vector3(source.anchoredPosition.x, source.anchoredPosition.y + extraLocalY, extraLocalZ);
            // Was set once at spawn (Attach) and never touched again — fine for every other
            // follower (ship/enemy models, straight-flying bullets) since their tracked rect's
            // rotation never changes after spawn, but PrototypeProjectile.UpdateExplosiveHoming
            // (บาส 2026-08-14, the Explosive homing shot) turns `source`'s rotation every frame
            // as it steers onto its target — without this, the 3D clone (and its trail/smoke
            // particle system, which points wherever the mesh is currently facing) stayed frozen
            // at its spawn heading while only its position kept up, so the trail streamed off in
            // a fixed straight line completely disconnected from the flat 2D sprite's actual
            // turning heading (บาส 2026-08-21 screenshot). templateLocalRotation is composed
            // OUTSIDE source's rotation so a template mesh whose default facing doesn't already
            // point the same way rect-rotation-zero does still keeps that fixed correction.
            Quaternion rotation = source.localRotation * templateLocalRotation;
            if (extraTiltDegrees != 0f) rotation *= Quaternion.Euler(0f, 0f, extraTiltDegrees);
            if (extraSpinDegrees != 0f) rotation *= Quaternion.Euler(0f, extraSpinDegrees, 0f);
            transform.localRotation = extraViewRotation * rotation;

            if (preserveTemplateScale)
            {
                transform.localScale = templateLocalScale * extraScaleMultiplier;
                return;
            }
            // บาส 2026-08-26: no wall/floor correction at all, on either display — use
            // exactly the localScale authored on the prefab (wallLocalScale), full stop. Any
            // auto-conversion (height-ratio or lossyScale-based) kept fighting whatever value
            // บาส actually set on the prefab; he wants that number respected as-is everywhere.
            float scale = wallLocalScale * extraScaleMultiplier;
            transform.localScale = new Vector3(scale, scale, scale);
        }
    }

    // Creates the orthographic camera that renders the 3D model layer for one display,
    // parented under (and framed to exactly match) the same RectTransform the UI on that
    // display already uses — see Visual3DFollower's class comment for why that sidesteps
    // needing to know real device pixel sizes.
    public static class Visual3DCameraRig
    {
        // viewportRect (default null = full 0,0,1,1) restricts this camera's own drawing to a
        // normalized sub-rectangle of the display — an Overlay camera in a stack still respects
        // its own Camera.rect, so this is what turns a per-zone culling layer into an actual
        // hard clip (nothing on this layer ever draws outside that rect) instead of just
        // deciding which camera CAN see it. See Visual3DFollower.ZoneFloorLayer.
        // clearFlags (default Depth, matching every caller before this one) — Depth clears the
        // depth buffer before this camera draws, which is correct for a camera that's the ONLY
        // (or first) Overlay adding real 3D content on top of the base pass. It's the wrong
        // choice for a SECOND overlay stacked after one that already wrote real geometry (บาส
        // 2026-08-21: Wall UI Priority, stacked after Wall 3D Visuals, was making a hand-placed
        // ship model on WallLayer disappear — confirmed by disabling UI Priority and watching it
        // reappear). Nothing means this camera doesn't touch the depth buffer at all, so it
        // can't interfere with whatever an earlier Overlay in the same stack already rendered.
        public static Camera Create(RectTransform parent, Camera baseCamera, int targetDisplay, string name, int cullingLayer, Rect? viewportRect = null, CameraClearFlags clearFlags = CameraClearFlags.Depth)
        {
            if (parent == null)
            {
                return null;
            }

            var rect = viewportRect ?? new Rect(0f, 0f, 1f, 1f);

            var existing = parent.Find(name);
            if (existing != null)
            {
                var existingCam = existing.GetComponent<Camera>();
                if (existingCam != null)
                {
                    existingCam.orthographic = true;
                    existingCam.orthographicSize = OrthographicHalfHeight(parent);
                    // บาส 2026-08-28: was 1f — confirmed in-Editor (Camera == Wall 3D Visuals,
                    // parented -1500 back from cutsceneArea) that this near plane sat close
                    // enough to clip real ship geometry (a wing tip poking closer to the camera
                    // than the rest of the hull), not just some empty margin in front of it.
                    // 0.05f keeps the same camera position/framing, just stops clipping anything
                    // that's actually part of the model.
                    existingCam.nearClipPlane = 0.05f;
                    existingCam.farClipPlane = 4000f;
                    existingCam.targetDisplay = targetDisplay;
                    existingCam.clearFlags = clearFlags;
                    existingCam.cullingMask = 1 << cullingLayer;
                    existingCam.rect = rect;
                    existingCam.depth = 5f;
                    existingCam.backgroundColor = Color.clear;
                    ConfigureCameraStack(baseCamera, existingCam);
                    return existingCam;
                }
            }

            var go = new GameObject(name, typeof(Camera));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, -1500f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var cam = go.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = OrthographicHalfHeight(parent);
            // See the matching comment on existingCam.nearClipPlane above — same fix, same reason.
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 4000f;
            cam.targetDisplay = targetDisplay;
            cam.rect = rect;
            // Do not clear color here. Backgrounds are authored as RawImages on each display,
            // so this camera should only add the 3D ship/VFX layer over whatever the UI cameras
            // have already drawn.
            cam.clearFlags = clearFlags;
            cam.cullingMask = 1 << cullingLayer;
            cam.depth = 5f;
            cam.backgroundColor = Color.clear;
            // Was allowHDR = false with no UniversalAdditionalCameraData post-processing setup
            // at all — บาส 2026-08-14 added a Global Volume with Bloom to the scene, but it had
            // nothing to show through: URP's Post Processing only runs on a camera whose own
            // UniversalAdditionalCameraData.renderPostProcessing is true (defaults false on a
            // camera added via script, unlike one placed by hand in the Editor), and Bloom's
            // over-bright detection needs HDR rendering active to have anything above 1.0 to
            // detect in the first place. Both needed flipping for either camera's VFX layer
            // (Wall or Floor — this rig builds both) to ever bloom at all.
            cam.allowHDR = true;
            cam.allowMSAA = false;
            var cameraData = go.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            ConfigureCameraStack(baseCamera, cam);
            return cam;
        }

        static void ConfigureCameraStack(Camera baseCamera, Camera overlayCamera)
        {
            if (overlayCamera == null)
            {
                return;
            }

            var overlayData = overlayCamera.GetUniversalAdditionalCameraData();
            overlayData.renderType = CameraRenderType.Overlay;
            overlayData.renderPostProcessing = true;

            if (baseCamera == null)
            {
                return;
            }

            var baseData = baseCamera.GetUniversalAdditionalCameraData();
            baseData.renderType = CameraRenderType.Base;
            if (!baseData.cameraStack.Contains(overlayCamera))
            {
                baseData.cameraStack.Add(overlayCamera);
            }
        }

        static float OrthographicHalfHeight(RectTransform parent)
        {
            float halfHeight = parent.rect.height * 0.5f;
            var canvas = parent.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
            {
                halfHeight *= Mathf.Max(0.0001f, Mathf.Abs(parent.lossyScale.y));
            }
            return Mathf.Max(0.01f, halfHeight);
        }
    }

    // Spawns a VFX prefab once at a fixed point and lets it clean itself up — for effects that
    // don't need to keep following anything (an explosion at a death position, a fire-instant
    // flash), unlike Visual3DFollower which tracks a moving source indefinitely.
    public static class Visual3DVfx
    {
        // depthYOffset (default 0, บาส 2026-08-21) — lets a one-shot VFX (explosion) win the
        // depth test against another opaque WallLayer/FloorLayer object sharing the same
        // Overlay camera pass (the astroid field). Confirmed by บาส dragging each axis handle
        // directly in the Scene view: for this Canvas (rotated 90° around X, wall camera NOT
        // rotated to match) it's local Y that actually maps to the wall camera's real viewing
        // depth (world Z) — NOT local Z as originally assumed (that guess cost a couple of
        // failed round-trips; see DeepspaceGameManager's weaponEffectDepthOffset comment).
        // Added to anchoredPosition.y rather than replacing it, since Y is also the object's
        // real on-screen vertical position — this nudges depth without relocating it visibly
        // as long as the offset stays modest.
        public static void SpawnOneShot(Transform template, RectTransform parent, Vector2 anchoredPosition, int layer, float sizeMultiplier = 1f, float fallbackLifetime = 2f, float depthYOffset = 0f)
        {
            if (template == null || parent == null)
            {
                return;
            }

            var clone = Object.Instantiate(template, parent);
            clone.gameObject.SetActive(true);
            Visual3DFollower.SetLayerRecursive(clone.gameObject, layer);
            clone.localRotation = template.localRotation;
            clone.localScale = template.localScale * sizeMultiplier;
            clone.localPosition = new Vector3(anchoredPosition.x, anchoredPosition.y + depthYOffset, 0f);

            // Prefer the particle system's own duration if it has one — a fixed fallback would
            // either cut a longer effect off mid-play or leave a short one lingering. Most VFX
            // prefabs are already configured to self-destroy via their own Stop Action; this is
            // just a safety net for ones that aren't (Destroy on an already-destroyed object is
            // a harmless no-op in Unity).
            Object.Destroy(clone.gameObject, EstimateLifetime(clone.gameObject, fallbackLifetime));
        }

        static float EstimateLifetime(GameObject go, float fallback)
        {
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            float longest = 0f;
            foreach (var ps in systems)
            {
                var main = ps.main;
                float duration = main.duration + main.startLifetime.constantMax;
                if (duration > longest)
                {
                    longest = duration;
                }
            }
            return longest > 0f ? longest : fallback;
        }
    }
}
