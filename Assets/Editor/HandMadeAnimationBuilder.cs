using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Wires up the animation Blender bakes for the five hand-modelled units — Soldier, Commander,
/// Sniper, Ramrod and PogoRider — the way <see cref="CharacterAnimationBuilder"/> wires up the
/// eight generated ones.
///
/// <para>
/// These five could not be animated the same way. They are imported models rather than code, and
/// four of the five carry a skeleton somebody drew by hand: ten bones in five unparented chains,
/// with no spine and no hips. So the motions are authored in Blender against that skeleton — see
/// <c>Tools/Anim/motions.py</c> — and baked to an animation-only FBX sitting beside each model.
/// Nothing in that pipeline reopens a source model, which is the point: Sniper and the pogo rider
/// already carry clips that work, and adding to a roster must not be able to lose them.
/// </para>
///
/// <para>
/// What this side does is the part Blender cannot: name the clips, set their loops, build a
/// controller, and hang it on the right node of the prefab. The names are the same six logical
/// states the generated eight answer to, because <c>AnimationHandler</c> asks every unit for a
/// state by one name and a roster that half-answers is worse than one that does not answer at all.
/// </para>
/// </summary>
public static class HandMadeAnimationBuilder
{
    private const string ModelFolder = "Assets/Models and stuff/";
    private const string ControllerFolder = "Assets/Animation/HandMade/";

    /// <summary>
    /// One hand-modelled unit: its prefab, the model whose skeleton the clips drive, and the node
    /// inside the prefab that the Animator belongs on.
    ///
    /// <para>
    /// <c>AnimatorPath</c> is the fussy one and cannot be guessed. Clips bake with their paths
    /// relative to the node above the armature, so the Animator has to sit on exactly that node or
    /// every curve in every clip silently binds to nothing.
    /// </para>
    /// </summary>
    private readonly struct Subject
    {
        public readonly string Unit;
        public readonly string Model;
        public readonly string AnimatorPath;

        /// <summary>
        /// Clips already living in the unit's own model that must end up in the controller too.
        /// The pogo rider's bounce and its dismount were drawn by hand years before any of this
        /// and were never wired to anything; adding states for them is the difference between
        /// "gave it new animations" and "replaced the ones it had".
        /// </summary>
        public readonly string[] Keep;

        /// <summary>
        /// Props hanging off the unit root rather than off a hand — Commander's pistol, Ramrod's
        /// shotgun. They are siblings of the model, so nothing in an animation reaches them and
        /// the body walks off while the weapon hangs in the air. Named here, they get parented to
        /// whichever hand is actually holding them.
        /// </summary>
        public readonly string[] Attach;

        /// <summary>
        /// The model a re-skinned copy was made from, for the two units that needed a torso bone.
        /// Their materials have to be taken from it: a round trip through Blender writes its own
        /// idea of every material, and Commander came back out of it in Soldier's green.
        /// </summary>
        public readonly string Origin;

        public Subject(
            string unit,
            string model,
            string animatorPath,
            string[] keep = null,
            string[] attach = null,
            string origin = null
        )
        {
            Unit = unit;
            Model = model;
            AnimatorPath = animatorPath;
            Keep = keep ?? System.Array.Empty<string>();
            Attach = attach ?? System.Array.Empty<string>();
            Origin = origin;
        }

        public string Original => Origin == null ? null : ModelFolder + Origin + ".fbx";

        public string Source => ModelFolder + Model + ".fbx";

        public string Animation => ModelFolder + Model + "@Anim.fbx";
        public string Manifest => ModelFolder + Model + "@Anim.clips.json";
        public string Prefab => $"Assets/Prefabs/Units/{Unit}.prefab";
        public string Controller => ControllerFolder + Unit + ".controller";
    }

    private static readonly Subject[] Roster =
    {
        // Commander runs off a re-skinned copy of its model, not the original: its chest was
        // skinned to the upper-arm bones, so it needed a torso bone before an arm could move
        // without taking a third of the body with it. See Tools/Anim/rerig.py.
        new("Commander", "CommanderRigged", "CommanderRigged", attach: new[] { "Pistol" }, origin: "Commander"),

        // Sniper and the pogo rider each carry two skeletons under one node -- a person and the
        // thing it is holding -- with the weapon skinned to its own armature rather than parented
        // to a hand. Their existing clips come in pairs for exactly that reason, and their new
        // ones are baked arms-locked so the weapon stays where its own layer puts it.
        new("Sniper", "Sniper", "Sniper (1)"),
        new(
            "PogoRider",
            "PogoRider",
            "PogoRider (1)",
            keep: new[] { "Bounce", "Flip off deadly pogo", "Ready to bounce", "PogoRiderAction" }
        ),
        // The shotgun only. Ramrod's Shield is deliberately not attached: it is a deployable the
        // game positions itself, and GameLoop and the collision tests both find it as a direct
        // child of the unit root by name. Parenting it to a hand hides it from them.
        new("Ramrod", "Shotgunner", "Shotgunner", attach: new[] { "Shotgun" }),

        // Soldier had no skeleton at all, so one was built onto it — see Tools/Anim/rig_soldier.py
        // — and the prefab now points at that rigged copy rather than at the original model.
        new(
            "Soldier",
            "SoldierRigged",
            "SoldierRigged",
            attach: new[] { "Battle Plan character soldier rifle" },
            origin: "Soldier"
        ),
    };

    [MenuItem("Battle Plan/Art/Build Hand-Made Animations", false, 18)]
    public static void BuildAll()
    {
        int done = 0;
        foreach (Subject subject in Roster)
        {
            if (Build(subject))
                done++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Characters] Wired animation for {done} of {Roster.Length} hand-made units.");
    }

    private static bool Build(Subject subject)
    {
        if (!System.IO.File.Exists(subject.Manifest))
        {
            Debug.LogError($"[Characters] No baked clips for {subject.Unit}. Run Tools/Anim/bake.py first.");
            return false;
        }

        Repaint(subject);

        List<Baked> baked = Baked.Read(subject.Manifest);
        if (!Configure(subject, baked))
            return false;

        List<(string State, AnimationClip Clip)> states = Rebase(subject, Collect(subject, baked));
        if (states.Count == 0)
            return false;

        states.AddRange(Legacy(subject));
        AnimatorController controller = WriteController(subject, states, ExistingController(subject));
        return Assign(subject, controller);
    }

    /// <summary>
    /// Names the imported takes and sets their loops.
    ///
    /// <para>
    /// Matched to the manifest by frame count rather than by name, deliberately. Unity renames
    /// whichever take it imports first after the file it came from, so one clip always arrives
    /// called "Commander@Anim" with no way to tell from the name what it was meant to be; the
    /// lengths are what survive the trip intact.
    /// </para>
    /// </summary>
    private static bool Configure(Subject subject, List<Baked> baked)
    {
        ModelImporter importer = AssetImporter.GetAtPath(subject.Animation) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[Characters] {subject.Animation} has not been imported.");
            return false;
        }

        ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
        List<ModelImporterClipAnimation> named = new();

        foreach (ModelImporterClipAnimation take in takes)
        {
            Baked match = baked.FirstOrDefault(clip => Mathf.Approximately(clip.Last - clip.First, take.lastFrame - take.firstFrame));
            if (match == null)
            {
                Debug.LogWarning($"[Characters] {subject.Unit} has a take {take.lastFrame - take.firstFrame} frames long that nothing was baked for.");
                continue;
            }

            take.name = match.Name;
            take.loopTime = match.Loop;
            take.wrapMode = match.Loop ? WrapMode.Loop : WrapMode.Once;
            named.Add(take);
        }

        importer.clipAnimations = named.ToArray();
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();

        // The model has to agree. Ramrod imports as Humanoid, and a Humanoid Animator drives its
        // rig through muscle space rather than by transform path — it ignores generic curves
        // outright, so the unit would animate not at all and say nothing about why. Nothing was
        // using that avatar: its only clips are one-frame poses.
        ModelImporter model = AssetImporter.GetAtPath(subject.Source) as ModelImporter;
        if (model != null && model.animationType == ModelImporterAnimationType.Human)
        {
            model.animationType = ModelImporterAnimationType.Generic;
            EditorUtility.SetDirty(model);
            model.SaveAndReimport();
            Debug.Log($"[Characters] {subject.Unit} was importing as Humanoid; switched to Generic so its clips bind.");
        }

        return named.Count > 0;
    }

    /// <summary>
    /// Points a re-skinned model's materials back at the ones its original came in with.
    ///
    /// <para>
    /// Exporting a model out of Blender writes fresh materials with the same names and none of the
    /// colours, so the re-skinned Commander imported wearing green instead of navy. Remapped on the
    /// importer rather than fixed up on the prefab, so it survives the next reimport.
    /// </para>
    /// </summary>
    private static void Repaint(Subject subject)
    {
        if (subject.Original == null)
            return;

        ModelImporter importer = AssetImporter.GetAtPath(subject.Source) as ModelImporter;
        if (importer == null)
            return;

        Dictionary<string, Material> original = new();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(subject.Original))
        {
            if (asset is Material material)
                original[material.name] = material;
        }

        if (original.Count == 0)
            return;

        bool changed = false;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(subject.Source))
        {
            if (asset is not Material mine || !original.TryGetValue(mine.name, out Material theirs) || theirs == mine)
                continue;

            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), mine.name), theirs);
            changed = true;
        }

        if (!changed)
            return;

        importer.SaveAndReimport();
        Debug.Log($"[Characters] {subject.Unit}: materials remapped back onto {subject.Origin}'s.");
    }

    private static List<(string State, AnimationClip Clip)> Collect(Subject subject, List<Baked> baked)
    {
        Dictionary<string, AnimationClip> found = new();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(subject.Animation))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                found[clip.name] = clip;
        }

        List<(string, AnimationClip)> states = new();
        foreach (Baked entry in baked)
        {
            if (found.TryGetValue(entry.Name, out AnimationClip clip))
                states.Add((entry.State, clip));
            else
                Debug.LogWarning($"[Characters] {subject.Unit} is missing its {entry.Name} clip after import.");
        }
        return states;
    }

    /// <summary>
    /// Refits Blender's clips onto the prefab they have to drive, and writes them out as clips of
    /// our own.
    ///
    /// <para>
    /// Two things need correcting and both are about the armature object rather than the bones.
    /// Unity's FBX importer folds its axis conversion and scale factor into that node, so the
    /// model's Armature sits at 270 degrees and a scale of a hundred where Blender left it square
    /// at one. Play a clip written against Blender's version and the figure lies on its back at a
    /// hundredth of its size — which is exactly what it did the first time. The bones are fine:
    /// they go through the same conversion in both files and arrive agreeing to four decimals.
    /// </para>
    ///
    /// <para>
    /// So the armature's curves are rebased through the <c>Rest</c> take — the same rig exported
    /// doing nothing — which measures the conversion instead of assuming it. Scale curves are
    /// dropped outright, from every path: nothing in this roster animates scale, and a scale curve
    /// that disagrees with the prefab can only ever be damage.
    /// </para>
    /// </summary>
    private static List<(string State, AnimationClip Clip)> Rebase(
        Subject subject,
        List<(string State, AnimationClip Clip)> imported
    )
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(subject.Prefab);
        Transform node = prefab != null ? prefab.transform.Find(subject.AnimatorPath) : null;
        if (node == null)
        {
            Debug.LogError($"[Characters] {subject.Unit} has no {subject.AnimatorPath}.");
            return new List<(string, AnimationClip)>();
        }

        (string _, AnimationClip calibration) = imported.FirstOrDefault(entry => entry.State == "Rest");
        if (calibration == null)
        {
            Debug.LogError($"[Characters] {subject.Unit} has no Rest take to measure the import conversion with.");
            return new List<(string, AnimationClip)>();
        }

        string root = RootPath(calibration);
        Transform rig = node.Find(root);
        if (rig == null)
        {
            Debug.LogError($"[Characters] {subject.Unit} has no {root} under {subject.AnimatorPath}.");
            return new List<(string, AnimationClip)>();
        }

        Vector3 blenderPosition = Sample(calibration, root, "m_LocalPosition", Vector3.zero);
        Quaternion blenderRotation = SampleRotation(calibration, root);
        Quaternion onto = rig.localRotation * Quaternion.Inverse(blenderRotation);

        EnsureFolder(ControllerFolder);
        List<(string, AnimationClip)> fitted = new();

        foreach ((string state, AnimationClip source) in imported)
        {
            if (state == "Rest")
                continue;

            string path = $"{ControllerFolder}{subject.Unit}_{state}.anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool fresh = clip == null;
            if (fresh)
                clip = new AnimationClip();

            clip.ClearCurves();
            clip.frameRate = source.frameRate;

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                // Scale is dropped from every path. Nothing on this roster animates it, and the
                // importer's own scale factor lives on the armature node, so a scale curve can only
                // ever fight the prefab.
                if (binding.propertyName.StartsWith("m_LocalScale") || binding.path == root)
                    continue;

                clip.SetCurve(binding.path, typeof(Transform), binding.propertyName, AnimationUtility.GetEditorCurve(source, binding));
            }

            RefitRoot(source, clip, root, rig, blenderPosition, blenderRotation, onto);

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = AnimationUtility.GetAnimationClipSettings(source).loopTime;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            clip.name = state;
            if (fresh)
                AssetDatabase.CreateAsset(clip, path);
            EditorUtility.SetDirty(clip);
            fitted.Add((state, clip));
        }

        return fitted;
    }

    /// <summary>
    /// The armature node's curves, moved out of Blender's frame and into the prefab's.
    ///
    /// <para>
    /// Whole vectors at a time, because the correction mixes the components: a rotation cannot be
    /// applied to x without also touching y and z. Both channels are rebuilt from the same set of
    /// sample times, taken from whichever of the source's own curves is densest, so nothing is
    /// invented between keys that the bake did not already say.
    /// </para>
    /// </summary>
    private static void RefitRoot(
        AnimationClip source,
        AnimationClip into,
        string root,
        Transform rig,
        Vector3 blenderPosition,
        Quaternion blenderRotation,
        Quaternion onto
    )
    {
        Dictionary<string, AnimationCurve> channels = new();
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
        {
            if (binding.path == root && !binding.propertyName.StartsWith("m_LocalScale"))
                channels[binding.propertyName] = AnimationUtility.GetEditorCurve(source, binding);
        }

        if (channels.Count == 0)
            return;

        SortedSet<float> times = new();
        foreach (AnimationCurve curve in channels.Values)
        {
            for (int i = 0; i < curve.length; i++)
                times.Add(curve[i].time);
        }

        AnimationCurve px = new();
        AnimationCurve py = new();
        AnimationCurve pz = new();
        AnimationCurve qx = new();
        AnimationCurve qy = new();
        AnimationCurve qz = new();
        AnimationCurve qw = new();

        foreach (float time in times)
        {
            Vector3 was = new(Read(channels, "m_LocalPosition.x", time), Read(channels, "m_LocalPosition.y", time), Read(channels, "m_LocalPosition.z", time));
            Quaternion spun = new(
                Read(channels, "m_LocalRotation.x", time),
                Read(channels, "m_LocalRotation.y", time),
                Read(channels, "m_LocalRotation.z", time),
                Read(channels, "m_LocalRotation.w", time)
            );
            spun.Normalize();

            Vector3 position = rig.localPosition + onto * (was - blenderPosition);
            Quaternion rotation = onto * spun;

            px.AddKey(time, position.x);
            py.AddKey(time, position.y);
            pz.AddKey(time, position.z);
            qx.AddKey(time, rotation.x);
            qy.AddKey(time, rotation.y);
            qz.AddKey(time, rotation.z);
            qw.AddKey(time, rotation.w);
        }

        into.SetCurve(root, typeof(Transform), "m_LocalPosition.x", px);
        into.SetCurve(root, typeof(Transform), "m_LocalPosition.y", py);
        into.SetCurve(root, typeof(Transform), "m_LocalPosition.z", pz);
        into.SetCurve(root, typeof(Transform), "m_LocalRotation.x", qx);
        into.SetCurve(root, typeof(Transform), "m_LocalRotation.y", qy);
        into.SetCurve(root, typeof(Transform), "m_LocalRotation.z", qz);
        into.SetCurve(root, typeof(Transform), "m_LocalRotation.w", qw);
    }

    private static float Read(Dictionary<string, AnimationCurve> channels, string property, float time) =>
        channels.TryGetValue(property, out AnimationCurve curve) ? curve.Evaluate(time) : 0f;

    private static string RootPath(AnimationClip clip)
    {
        string shortest = null;
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (string.IsNullOrEmpty(binding.path))
                continue;
            if (shortest == null || binding.path.Length < shortest.Length)
                shortest = binding.path;
        }
        return shortest;
    }

    private static Vector3 Sample(AnimationClip clip, string path, string property, Vector3 fallback)
    {
        Vector3 value = fallback;
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.path != path || !binding.propertyName.StartsWith(property))
                continue;

            float at = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0f);
            if (binding.propertyName.EndsWith(".x"))
                value.x = at;
            else if (binding.propertyName.EndsWith(".y"))
                value.y = at;
            else if (binding.propertyName.EndsWith(".z"))
                value.z = at;
        }
        return value;
    }

    private static Quaternion SampleRotation(AnimationClip clip, string path)
    {
        Quaternion value = Quaternion.identity;
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.path != path || !binding.propertyName.StartsWith("m_LocalRotation"))
                continue;

            float at = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0f);
            if (binding.propertyName.EndsWith(".x"))
                value.x = at;
            else if (binding.propertyName.EndsWith(".y"))
                value.y = at;
            else if (binding.propertyName.EndsWith(".z"))
                value.z = at;
            else if (binding.propertyName.EndsWith(".w"))
                value.w = at;
        }
        return value;
    }

    /// <summary>
    /// Adds the new states to whatever controller the unit already has, or makes one if it has
    /// none.
    ///
    /// <para>
    /// Adding rather than rebuilding, because Sniper's controller is not ours to throw away: it
    /// carries seven hand-made clips across two layers — a character over a gun — that have been
    /// driving that unit since long before any of this. Only states whose names collide with the
    /// six being written are replaced; everything else on every layer is left exactly as found.
    /// </para>
    ///
    /// <para>
    /// The new states are a flat set with no transitions between them, as on the generated eight:
    /// the handler cross-fades by name, and a graph with its own opinion about what may follow
    /// what is a second authority on a question <c>Movement</c> and <c>Shooting</c> have already
    /// answered.
    /// </para>
    /// </summary>
    private static AnimatorController WriteController(
        Subject subject,
        IReadOnlyList<(string State, AnimationClip Clip)> states,
        AnimatorController existing
    )
    {
        AnimatorController controller = existing;
        if (controller == null)
        {
            EnsureFolder(ControllerFolder);
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(subject.Controller);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(subject.Controller);
        }

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        int kept = machine.states.Length;

        for (int i = 0; i < states.Count; i++)
        {
            AnimatorState state = machine.states
                .Select(child => child.state)
                .FirstOrDefault(candidate => candidate.name == states[i].State);

            if (state == null)
                state = machine.AddState(states[i].State, new Vector3(520f, 60f + i * 70f, 0f));

            state.motion = states[i].Clip;
            state.writeDefaultValues = true;
            if (states[i].State == "Idle")
                machine.defaultState = state;
        }

        Debug.Log($"[Characters] {subject.Unit}: {states.Count} states written, {kept} already there were left alone.");
        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>
    /// The unit's own pre-existing clips, pulled out of its model so they can be given states of
    /// their own. Matched by suffix, because Unity prefixes an imported take with the armature it
    /// was authored on — "PogoRider|Bounce" is the clip anybody would call "Bounce".
    /// </summary>
    private static List<(string State, AnimationClip Clip)> Legacy(Subject subject)
    {
        List<(string, AnimationClip)> found = new();
        if (subject.Keep.Length == 0)
            return found;

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(subject.Source))
        {
            if (asset is not AnimationClip clip || clip.name.StartsWith("__preview__"))
                continue;

            foreach (string wanted in subject.Keep)
            {
                if (clip.name == wanted || clip.name.EndsWith("|" + wanted))
                    found.Add((clip.name.Replace("|", " "), clip));
            }
        }

        if (found.Count < subject.Keep.Length)
            Debug.LogWarning($"[Characters] {subject.Unit}: kept {found.Count} of {subject.Keep.Length} original clips.");
        return found;
    }

    /// <summary>Whatever controller the prefab already drives this unit with, if any.</summary>
    private static AnimatorController ExistingController(Subject subject)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(subject.Prefab);
        Transform node = prefab != null ? prefab.transform.Find(subject.AnimatorPath) : null;
        Animator animator = node != null ? node.GetComponent<Animator>() : null;
        return animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
    }

    private static bool Assign(Subject subject, AnimatorController controller)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(subject.Prefab);
        if (root == null)
        {
            Debug.LogError($"[Characters] Missing {subject.Prefab}.");
            return false;
        }

        try
        {
            Transform node = root.transform.Find(subject.AnimatorPath);
            if (node == null)
            {
                Debug.LogError($"[Characters] {subject.Unit} has no {subject.AnimatorPath} to put an Animator on.");
                return false;
            }

            // Explicitly, not with ??: a missing component comes back as Unity's fake null, which
            // the null-coalescing operator happily accepts as a real object.
            Animator animator = node.GetComponent<Animator>();
            if (animator == null)
                animator = node.gameObject.AddComponent<Animator>();

            Hold(root, node, subject);

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            PrefabUtility.SaveAsPrefabAsset(root, subject.Prefab);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Hands, across both skeletons on this roster. Named rather than searched for, because the
    /// hand-drawn rig calls its bones "Bone.003" and the only thing distinguishing an arm from a
    /// leg in it is which number it got.
    /// </summary>
    private static readonly string[] Hands =
    {
        "Bone.001",
        "Bone.001_end",
        "Bone.003",
        "Bone.003_end",
        "hand.L",
        "hand.R",
    };

    /// <summary>
    /// Parents each loose prop to the hand nearest it, keeping it exactly where it was drawn.
    ///
    /// <para>
    /// These props hang off the unit root as siblings of the model, so no animation reaches them:
    /// the body walks off and the weapon stays behind in mid-air. Which hand holds which is
    /// measured rather than declared — the props sit where the artist put them, and asking which
    /// hand a pistol is nearest is more honest than guessing from a bone called "Bone.003".
    /// Reparenting keeps world position, so nothing moves; only what it follows changes.
    /// </para>
    /// </summary>
    private static void Hold(GameObject root, Transform node, Subject subject)
    {
        if (subject.Attach.Length == 0)
            return;

        List<Transform> hands = node
            .GetComponentsInChildren<Transform>(true)
            .Where(bone => Hands.Contains(bone.name) && bone.GetComponent<Renderer>() == null)
            .ToList();

        if (hands.Count == 0)
        {
            Debug.LogWarning($"[Characters] {subject.Unit} has no hand bone to hang anything off.");
            return;
        }

        foreach (string name in subject.Attach)
        {
            // Searched through the whole prefab, not just its root: an earlier pass may already
            // have parented this somewhere, and a prop that cannot be found again is a prop that
            // can never be corrected.
            Transform prop = root.transform
                .GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => candidate.name == name);

            if (prop == null)
                continue;

            Transform nearest = hands.OrderBy(hand => Vector3.Distance(hand.position, prop.position)).First();
            if (prop.parent == nearest)
                continue;

            float gap = Vector3.Distance(nearest.position, prop.position);
            prop.SetParent(nearest, worldPositionStays: true);
            Debug.Log($"[Characters] {subject.Unit}: {name} now hangs off {nearest.name}, {gap:0.00} away.");
        }
    }

    private sealed class Baked
    {
        public string Name;
        public string State;
        public int First;
        public int Last;
        public bool Loop;

        public static List<Baked> Read(string path)
        {
            // Hand-parsed rather than pulled through JsonUtility, which cannot read a bare array.
            List<Baked> clips = new();
            foreach (string chunk in System.IO.File.ReadAllText(path).Split('{'))
            {
                if (!chunk.Contains("\"name\""))
                    continue;

                clips.Add(
                    new Baked
                    {
                        Name = Field(chunk, "name"),
                        State = Field(chunk, "state"),
                        First = int.Parse(Field(chunk, "first")),
                        Last = int.Parse(Field(chunk, "last")),
                        Loop = Field(chunk, "loop") == "true",
                    }
                );
            }
            return clips;
        }

        private static string Field(string chunk, string key)
        {
            int at = chunk.IndexOf($"\"{key}\"", System.StringComparison.Ordinal);
            int colon = chunk.IndexOf(':', at) + 1;
            int end = chunk.IndexOfAny(new[] { ',', '\n', '}' }, colon);
            return chunk.Substring(colon, end - colon).Trim().Trim('"');
        }
    }

    private static void EnsureFolder(string folder)
    {
        string trimmed = folder.TrimEnd('/');
        if (AssetDatabase.IsValidFolder(trimmed))
            return;

        string parent = System.IO.Path.GetDirectoryName(trimmed)?.Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(trimmed);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
