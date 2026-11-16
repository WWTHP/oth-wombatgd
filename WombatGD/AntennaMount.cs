using System.Collections.Generic;
using UnityEngine;

namespace WombatGD
{
    /// <summary>
    /// Moves the Wombat's antenna from the front fender to the passenger A-pillar,
    /// raked back to follow the pillar like a 300GD's. The pillar is found by
    /// raycasting the car's own body collider, so the antenna lands on the
    /// windscreen corner whatever the exact model; the menu sliders fine-tune it.
    /// The antenna wobbles on a physics joint, which is re-seated after each move
    /// so it treats the new spot as its rest position.
    /// </summary>
    internal static class AntennaMount
    {
        private struct Pillar { public Vector3 Base; public float RakeDeg; public bool Probed; }
        private struct Original { public Vector3 Pos; public Quaternion Rot; }

        // Used when the probe can't find the windscreen (car space: +x passenger, +z forward).
        private static readonly Pillar FallbackPillar = new() { Base = new Vector3(0.78f, 1.42f, 0.78f), RakeDeg = 12f };

        private static readonly Dictionary<int, Original> Originals = new();
        private static readonly Dictionary<int, Pillar> Pillars = new();

        /// <summary>Puts the antenna on the A-pillar (<paramref name="onPillar"/>) or back on the fender.</summary>
        public static void Set(Transform root, bool onPillar)
        {
            var mount = FindMount(root);
            if (mount == null)
            {
                if (onPillar) WombatGDMod.Log.Warning("Antenna not found on Wombat; left where it is.");
                return;
            }

            int id = mount.GetInstanceID();
            if (!Originals.TryGetValue(id, out var orig))
                Originals[id] = orig = new Original { Pos = mount.localPosition, Rot = mount.localRotation };

            Vector3 pos = orig.Pos;
            Quaternion rot = orig.Rot;
            if (onPillar)
            {
                if (!Pillars.TryGetValue(id, out var pillar))
                {
                    pillar = Probe(root);
                    if (pillar.Probed) Pillars[id] = pillar; // retry later if the car wasn't measurable yet
                }
                var parent = mount.parent;
                // Base sits just proud of the glass at the pillar's foot, plus the user's nudges.
                var carPos = pillar.Base + new Vector3(Settings.AntennaSide.Value, Settings.AntennaHeight.Value,
                                                       0.03f + Settings.AntennaForward.Value);
                pos = parent.InverseTransformPoint(root.TransformPoint(carPos));
                // Tip leans back (toward -z) by the pillar rake about the car's X axis.
                var tilt = Quaternion.AngleAxis(-(pillar.RakeDeg + Settings.AntennaTilt.Value), Vector3.right);
                var carToParent = Quaternion.Inverse(parent.rotation) * root.rotation;
                rot = carToParent * tilt * Quaternion.Inverse(carToParent) * orig.Rot;
            }

            if ((mount.localPosition - pos).sqrMagnitude < 1e-6f && Quaternion.Angle(mount.localRotation, rot) < 0.05f)
                return; // already there
            mount.localPosition = pos;
            mount.localRotation = rot;
            ReseatJoints(mount);
        }

        /// <summary>The outer "Antenna" holder under the cosmetic "Visual" group.</summary>
        private static Transform FindMount(Transform t)
        {
            if (t.name == "Antenna" && t.parent != null && t.parent.name == "Visual") return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var hit = FindMount(t.GetChild(i));
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>
        /// Scans the body collider front-on just inside the passenger edge. Rays
        /// below the hood line hit the grille; above it they pass over the hood
        /// and hit the windscreen/pillar. A line fit through the windscreen hits
        /// gives the pillar's foot and rake.
        /// </summary>
        private static Pillar Probe(Transform root)
        {
            var body = FindBodyCollider(root);
            if (body == null)
            {
                WombatGDMod.Log.Warning("Antenna: no body collider to measure; using the default pillar position.");
                return FallbackPillar;
            }

            foreach (float x in new[] { 0.72f, 0.65f, 0.55f })
            {
                var ys = new List<float>();
                var zs = new List<float>();
                float frontZ = float.MinValue;
                for (float y = 0.9f; y <= 2.3f; y += 0.02f)
                {
                    if (!Hit(body, root, new Vector3(x, y, 4f), Vector3.back, out var p)) continue;
                    frontZ = Mathf.Max(frontZ, p.z);
                    ys.Add(y);
                    zs.Add(p.z);
                }
                if (ys.Count < 6) continue;

                // Windscreen hits are the ones well behind the nose.
                var wy = new List<float>();
                var wz = new List<float>();
                for (int i = 0; i < ys.Count; i++)
                    if (zs[i] < frontZ - 0.5f) { wy.Add(ys[i]); wz.Add(zs[i]); }
                if (wy.Count < 4) continue;

                // Least-squares z = a + b*y over the windscreen.
                float my = 0, mz = 0;
                for (int i = 0; i < wy.Count; i++) { my += wy[i]; mz += wz[i]; }
                my /= wy.Count; mz /= wy.Count;
                float sxy = 0, sxx = 0;
                for (int i = 0; i < wy.Count; i++) { sxy += (wy[i] - my) * (wz[i] - mz); sxx += (wy[i] - my) * (wy[i] - my); }
                if (sxx < 1e-5f) continue;
                float b = sxy / sxx;
                float yBase = wy[0];
                float zBase = mz + b * (yBase - my);
                float rake = Mathf.Atan(-b) * Mathf.Rad2Deg;

                // Outer face of the pillar: scan sideways just behind the glass.
                float xOut = 0.78f;
                float yMid = yBase + 0.15f;
                if (Hit(body, root, new Vector3(3f, yMid, zBase + b * 0.15f - 0.06f), Vector3.left, out var side))
                    xOut = side.x - 0.04f;

                var result = new Pillar { Base = new Vector3(xOut, yBase, zBase), RakeDeg = rake, Probed = true };
                bool sane = rake > -5f && rake < 40f && yBase > 1.0f && yBase < 1.9f &&
                            zBase > -0.2f && zBase < 1.6f && xOut > 0.4f && xOut < 1.1f;
                WombatGDMod.Log.Msg($"Antenna: windscreen probe at x={x:F2}: foot=({xOut:F2},{yBase:F2},{zBase:F2}) " +
                                    $"rake={rake:F1}° from {wy.Count} hits" + (sane ? "" : " — implausible, ignored"));
                if (sane) return result;
            }

            WombatGDMod.Log.Warning("Antenna: couldn't find the windscreen; using the default pillar position.");
            return FallbackPillar;
        }

        private static bool Hit(Collider c, Transform root, Vector3 carFrom, Vector3 carDir, out Vector3 carHit)
        {
            carHit = default;
            var ray = new Ray(root.TransformPoint(carFrom), root.TransformDirection(carDir));
            if (!c.Raycast(ray, out var hit, 8f)) return false;
            carHit = root.InverseTransformPoint(hit.point);
            return true;
        }

        /// <summary>The cab-shaped "Top" collider under "Collider" (any collider there as a fallback).</summary>
        private static Collider FindBodyCollider(Transform root)
        {
            var group = root.Find("Collider");
            if (group == null) return null;
            var top = group.Find("Top");
            var c = top != null ? top.GetComponent<Collider>() : null;
            return c != null ? c : group.GetComponentInChildren<Collider>();
        }

        /// <summary>
        /// The antenna bone hangs on a ConfigurableJoint whose rest pose was captured
        /// where the stock antenna stood. Cycling the bone's GameObject rebuilds the
        /// joint from its current pose, so it settles at the new spot instead of
        /// springing back to the fender.
        /// </summary>
        private static void ReseatJoints(Transform mount)
        {
            foreach (var j in mount.GetComponentsInChildren<ConfigurableJoint>(true))
            {
                var rb = j.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.position = j.transform.position;
                    rb.rotation = j.transform.rotation;
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                var cb = j.connectedBody;
                if (!j.autoConfigureConnectedAnchor && cb != null)
                    j.connectedAnchor = cb.transform.InverseTransformPoint(j.transform.TransformPoint(j.anchor));
                var go = j.gameObject;
                if (go.activeSelf)
                {
                    go.SetActive(false);
                    go.SetActive(true);
                }
            }
        }
    }
}
