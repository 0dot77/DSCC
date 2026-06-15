"""Strip a Rigify control-rig FBX down to its DEF (deform) skeleton.

Reads job list from the UTF-8 JSON file named by env BLENDER_FBX_JOBS:
  [{"src": "...", "dst": "..."}, ...]

For every kept bone the new parent is resolved by walking the ORIGINAL
ancestor chain: the first DEF- ancestor wins; otherwise an ORG-x ancestor
whose DEF-x twin exists maps to that twin; otherwise the rig's "root" bone.
This reconnects deform chains that Rigify scattered across the control
hierarchy (e.g. DEF-thigh parented under ORG-spine).
"""

import bpy
import json
import os
import sys


def is_kept(name: str) -> bool:
    if name.endswith("_end"):
        return False
    return name.startswith("DEF-") or name == "root"


FACE_TOKENS = {
    "face", "brow", "lid", "cheek", "chin", "ear", "jaw", "lip", "nose",
    "forehead", "temple", "teeth", "tongue", "eye",
}


def is_face_bone(name: str) -> bool:
    if not name.startswith("DEF-"):
        return False
    stem = name[len("DEF-"):]
    token = stem.split(".")[0].split("_")[0].lower()
    return token in FACE_TOKENS


def find_head_bone(bone_names) -> str | None:
    if "DEF-head" in bone_names:
        return "DEF-head"
    # Rigify spine rigs without named head bones end the chain at the highest
    # DEF-spine.N; that terminal bone is the head.
    spine_numbers = []
    for name in bone_names:
        if name == "DEF-spine":
            spine_numbers.append(0)
        elif name.startswith("DEF-spine."):
            suffix = name[len("DEF-spine."):]
            if suffix.isdigit():
                spine_numbers.append(int(suffix))
    if not spine_numbers:
        return None
    top = max(spine_numbers)
    return "DEF-spine" if top == 0 else f"DEF-spine.{top:03d}"


def process(src: str, dst: str) -> dict:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src, ignore_leaf_bones=True, automatic_bone_orientation=False)

    armatures = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if len(armatures) != 1:
        raise RuntimeError(f"expected exactly 1 armature, found {len(armatures)}: {[a.name for a in armatures]}")
    arm = armatures[0]

    # Safety: every vertex group that carries real weight must survive the prune.
    weighted = set()
    for mesh in meshes:
        names = [g.name for g in mesh.vertex_groups]
        seen = set()
        for v in mesh.data.vertices:
            for g in v.groups:
                if g.weight > 1e-6:
                    seen.add(g.group)
        weighted.update(names[i] for i in seen if i < len(names))
    bad_weights = sorted(n for n in weighted if not is_kept(n))
    if bad_weights:
        raise RuntimeError("skin weights exist on non-DEF bones; aborting to avoid breaking the mesh: " + ", ".join(bad_weights[:20]))

    orig_parent = {}
    for b in arm.data.bones:
        orig_parent[b.name] = b.parent.name if b.parent else None
    bone_names = set(orig_parent)

    def resolve_parent(name: str):
        cur = orig_parent.get(name)
        while cur is not None:
            if is_kept(cur):
                return cur
            if cur.startswith("ORG-"):
                twin = "DEF-" + cur[len("ORG-"):]
                if twin in bone_names and twin != name:
                    return twin
            # Rigify control bones reuse the metarig bone name (e.g. the
            # "head"/"neck" controls); map them onto their DEF twin too.
            control_twin = "DEF-" + cur
            if control_twin in bone_names and control_twin != name:
                return control_twin
            cur = orig_parent.get(cur)
        if "root" in bone_names and name != "root":
            return "root"
        return None

    head_bone = find_head_bone(bone_names)
    kept = [n for n in bone_names if is_kept(n)]
    kept_set = set(kept)
    new_parent = {}
    reparented = []
    face_to_head = []
    for name in kept:
        if name == "root":
            new_parent[name] = None
            continue
        parent = None
        # Face deform bones hang off control/MCH face rigs that do not survive
        # the prune; ancestor walking lands them on torso bones. Pin them to
        # the head deform bone unless their direct original parent is a kept
        # face bone (preserves e.g. chained ear segments).
        direct = orig_parent.get(name)
        if is_face_bone(name):
            if direct in kept_set and is_face_bone(direct):
                parent = direct
            elif head_bone is not None:
                parent = head_bone
                face_to_head.append(name)
        if parent is None:
            parent = resolve_parent(name)
        new_parent[name] = parent
        if parent != orig_parent.get(name):
            reparented.append(f"{name}: {orig_parent.get(name)} -> {parent}")

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    edit_bones = arm.data.edit_bones
    for name, parent in new_parent.items():
        bone = edit_bones.get(name)
        if bone is None:
            continue
        bone.use_connect = False
        bone.parent = edit_bones.get(parent) if parent else None
    for bone in list(edit_bones):
        if not is_kept(bone.name):
            edit_bones.remove(bone)
    bpy.ops.object.mode_set(mode="OBJECT")

    for b in arm.data.bones:
        b.use_deform = b.name.startswith("DEF-")

    kept_names = {b.name for b in arm.data.bones}
    removed_groups = 0
    for mesh in meshes:
        for g in list(mesh.vertex_groups):
            if g.name not in kept_names and g.name not in weighted:
                mesh.vertex_groups.remove(g)
                removed_groups += 1

    bpy.ops.export_scene.fbx(
        filepath=dst,
        use_selection=False,
        object_types={"ARMATURE", "MESH", "EMPTY"},
        add_leaf_bones=False,
        bake_anim=False,
    )

    return {
        "source": os.path.basename(src),
        "output": os.path.basename(dst),
        "bones_before": len(bone_names),
        "bones_after": len(kept_names),
        "head_bone": head_bone,
        "weighted_groups": len(weighted),
        "weighted_groups_missing_after_prune": sorted(n for n in weighted if n not in kept_names),
        "empty_groups_removed": removed_groups,
        "face_bones_pinned_to_head": len(face_to_head),
        "reparented_non_face": [r for r in reparented if not is_face_bone(r.split(":")[0])],
    }


def main():
    jobs_path = os.environ["BLENDER_FBX_JOBS"]
    with open(jobs_path, "r", encoding="utf-8") as handle:
        jobs = json.load(handle)

    reports = []
    for job in jobs:
        reports.append(process(job["src"], job["dst"]))

    print("REPORT_JSON:" + json.dumps(reports, ensure_ascii=False, indent=1))


main()
