using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Headless self-test for the measured-socket layout system, runnable via:
///
///   Unity.exe -batchmode -quit -projectPath ...
///     -executeMethod CreatureLayoutSelfTest.RunBatchTest
///
/// Builds a throwaway creature entirely in code (a torso with distinct per-axis
/// extents, plus head / legs / horns whose origin sits at their seam), equips
/// them, and verifies:
///   1. each attach point lands on the correct spot of the measured torso box,
///   2. head sockets (horns) are measured off the placed head,
///   3. seam origins meet their sockets (head neck at top, leg hip at bottom),
///   4. the layout is identical after spinning the root (determinism / reload).
///
/// Exits 0 on success, 1 on any failure. Creates nothing on disk.
/// </summary>
public static class CreatureLayoutSelfTest
{
    private const float Eps = 0.01f;

    public static void RunBatchTest()
    {
        var failures = new List<string>();
        var spawned = new List<GameObject>();
        try
        {
            RunChecks(failures, spawned);
        }
        catch (System.Exception e)
        {
            failures.Add("Unhandled exception: " + e);
        }
        finally
        {
            foreach (var go in spawned)
                if (go != null) Object.DestroyImmediate(go);
        }

        if (failures.Count == 0)
        {
            Debug.Log("LAYOUT SELF-TEST PASSED — sockets, seams and determinism all green.");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError("LAYOUT SELF-TEST FAILED:\n  • " + string.Join("\n  • ", failures));
            EditorApplication.Exit(1);
        }
    }

    private static void RunChecks(List<string> failures, List<GameObject> spawned)
    {
        // ---- Arrange: creature root + 8 attach points ----
        GameObject root = new GameObject("TestCreatureRoot");
        spawned.Add(root);
        var assembler = root.AddComponent<CreatureAssembler>();

        assembler.torsoAttach      = MakeAttach(root.transform, "AttachPoint_Torso");
        assembler.headAttach       = MakeAttach(root.transform, "AttachPoint_Head");
        assembler.frontLegsAttach  = MakeAttach(root.transform, "AttachPoint_FrontLegs");
        assembler.backLegsAttach   = MakeAttach(root.transform, "AttachPoint_BackLegs");
        assembler.tailAttach       = MakeAttach(root.transform, "AttachPoint_Tail");
        assembler.wingsAttach      = MakeAttach(root.transform, "AttachPoint_Wings");
        assembler.hornsAttach      = MakeAttach(root.transform, "AttachPoint_Horns");
        assembler.accessoriesAttach= MakeAttach(root.transform, "AttachPoint_Accessories");

        // Part templates. Torso mesh is non-cubic (distinct extents) to catch
        // axis mistakes. Head/legs/horns have their origin AT the seam:
        //   head neck  = bottom of mesh  → mesh sits above origin
        //   leg  hip   = top of mesh     → mesh sits below origin
        //   horn base  = bottom of mesh  → mesh sits above origin
        GameObject torsoTpl = MakePart("TorsoTpl", new Vector3(2f, 1f, 3f), Vector3.zero);
        GameObject headTpl  = MakePart("HeadTpl",  Vector3.one,             new Vector3(0f,  0.5f, 0f));
        GameObject flTpl    = MakePart("FLTpl",    Vector3.one,             new Vector3(0f, -0.5f, 0f));
        GameObject blTpl    = MakePart("BLTpl",    Vector3.one,             new Vector3(0f, -0.5f, 0f));
        GameObject hornTpl  = MakePart("HornTpl",  new Vector3(0.4f, 0.6f, 0.4f), new Vector3(0f, 0.3f, 0f));
        spawned.AddRange(new[] { torsoTpl, headTpl, flTpl, blTpl, hornTpl });

        var torsoData = MakeData(BodyPartCategory.Torso, torsoTpl);
        var headData  = MakeData(BodyPartCategory.Head, headTpl);
        var flData    = MakeData(BodyPartCategory.FrontLegs, flTpl);
        var blData    = MakeData(BodyPartCategory.BackLegs, blTpl);
        var hornData  = MakeData(BodyPartCategory.Horns, hornTpl);

        // ---- Act: equip (batch), then a single layout pass ----
        assembler.EquipPart(torsoData, notify: false);
        assembler.EquipPart(headData, notify: false);
        assembler.EquipPart(flData, notify: false);
        assembler.EquipPart(blData, notify: false);
        assembler.EquipPart(hornData, notify: false);
        assembler.NotifyCreatureChanged(); // root at identity here

        // ---- Assert: sockets land on the measured torso box ----
        GameObject torsoGo = assembler.torsoAttach.GetChild(0).gameObject;
        Bounds torso = PartScaleNormalizer.GetCompositeBounds(torsoGo);

        CheckSocket(failures, "Head",      assembler.headAttach,      torso, BodyPartCategory.Head);
        CheckSocket(failures, "FrontLegs", assembler.frontLegsAttach, torso, BodyPartCategory.FrontLegs);
        CheckSocket(failures, "BackLegs",  assembler.backLegsAttach,  torso, BodyPartCategory.BackLegs);

        // Front and back leg sockets must sit at the torso bottom, split fore/aft.
        if (assembler.frontLegsAttach.position.y > torso.min.y + torso.size.y * 0.25f)
            failures.Add("FrontLegs socket is not near the torso bottom.");
        if (assembler.frontLegsAttach.position.z <= assembler.backLegsAttach.position.z)
            failures.Add("FrontLegs socket should be forward (+Z) of BackLegs.");

        // ---- Assert: horns measured off the PLACED head ----
        GameObject headGo = assembler.headAttach.GetChild(0).gameObject;
        Bounds head = PartScaleNormalizer.GetCompositeBounds(headGo);
        CreatureSockets.TryGetRule(BodyPartCategory.Horns, out var hornRule);
        Vector3 expectedHorn = head.center + Vector3.Scale(head.extents, hornRule.boxFraction);
        if (Vector3.Distance(assembler.hornsAttach.position, expectedHorn) > Eps)
            failures.Add($"Horns socket {assembler.hornsAttach.position} != expected off head {expectedHorn}.");

        // ---- Assert: seam origins meet their sockets ----
        // Head neck (mesh bottom) should sit at the head socket height.
        if (Mathf.Abs(head.min.y - assembler.headAttach.position.y) > Eps)
            failures.Add($"Head neck seam (min.y {head.min.y:F3}) does not meet its socket (y {assembler.headAttach.position.y:F3}).");
        // Leg hip (mesh top) should sit at the leg socket height.
        Bounds fl = PartScaleNormalizer.GetCompositeBounds(assembler.frontLegsAttach.GetChild(0).gameObject);
        if (Mathf.Abs(fl.max.y - assembler.frontLegsAttach.position.y) > Eps)
            failures.Add($"Leg hip seam (max.y {fl.max.y:F3}) does not meet its socket (y {assembler.frontLegsAttach.position.y:F3}).");

        // ---- Assert: determinism across a spin (simulates reload while rotated) ----
        Vector3 headLocalBefore = assembler.headAttach.localPosition;
        Vector3 flLocalBefore   = assembler.frontLegsAttach.localPosition;
        Vector3 hornLocalBefore = assembler.hornsAttach.localPosition;

        root.transform.rotation = Quaternion.Euler(0f, 37f, 15f);
        assembler.NotifyCreatureChanged();

        if (Vector3.Distance(assembler.headAttach.localPosition, headLocalBefore) > Eps)
            failures.Add("Head socket local position changed after spinning the root — layout is rotation-dependent.");
        if (Vector3.Distance(assembler.frontLegsAttach.localPosition, flLocalBefore) > Eps)
            failures.Add("FrontLegs socket local position changed after spinning the root.");
        if (Vector3.Distance(assembler.hornsAttach.localPosition, hornLocalBefore) > Eps)
            failures.Add("Horns socket local position changed after spinning the root.");
    }

    // ------------------------------------------------------------------

    private static void CheckSocket(List<string> failures, string label, Transform attach,
                                    Bounds torso, BodyPartCategory cat)
    {
        if (!CreatureSockets.TryGetRule(cat, out var rule))
        {
            failures.Add($"{label}: no socket rule found.");
            return;
        }
        Vector3 expected = torso.center + Vector3.Scale(torso.extents, rule.boxFraction);
        if (Vector3.Distance(attach.position, expected) > Eps)
            failures.Add($"{label} socket {attach.position} != expected {expected} (from torso bounds).");

        // The part's origin (child of the attach point at zero offset) must sit
        // exactly on the socket.
        if (attach.childCount > 0)
        {
            Vector3 origin = attach.GetChild(0).position;
            if (Vector3.Distance(origin, attach.position) > Eps)
                failures.Add($"{label} part origin {origin} is not on its socket {attach.position}.");
        }
    }

    private static Transform MakeAttach(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        return go.transform;
    }

    /// <summary>
    /// A part template: an empty root (the origin/seam) with a cube mesh child
    /// offset so the seam lands where we want it.
    /// </summary>
    private static GameObject MakePart(string name, Vector3 meshScale, Vector3 meshOffset)
    {
        var root = new GameObject(name);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(root.transform, false);
        cube.transform.localPosition = meshOffset;
        cube.transform.localScale = meshScale;
        // Kept active so instantiated clones are active too (inactive renderers
        // are excluded from bounds). Templates aren't measured — only instances.
        return root;
    }

    private static BodyPartData MakeData(BodyPartCategory cat, GameObject prefab)
    {
        var d = ScriptableObject.CreateInstance<BodyPartData>();
        d.category = cat;
        d.prefab = prefab;
        d.useAutoScale = true;
        d.scaleMultiplier = 1f;
        return d;
    }
}
