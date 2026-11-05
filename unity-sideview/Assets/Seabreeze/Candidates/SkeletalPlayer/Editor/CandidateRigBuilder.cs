#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace Seabreeze.Candidates.Editor
{
    /// <summary>
    /// Rebuilds only the isolated skeletal candidate from its authored PSB and pixel-space layout.
    /// Uses public Sprite Editor data providers; all skinning data is persisted on the PSB importer.
    /// Running this replaces candidate skinning edits and the visual prefab. Run motion authoring after it.
    /// </summary>
    public static class CandidateRigBuilder
    {
        public const string CandidateRoot = "Assets/Seabreeze/Candidates/SkeletalPlayer";
        public const string PsbPath = CandidateRoot + "/Art/player-rig.psb";
        public const string LayoutPath = CandidateRoot + "/Art/rig-layout.json";
        public const string PrefabPath = CandidateRoot + "/Prefabs/PlayerRigCandidate.prefab";
        const float GridSpacing = 30f;
        static readonly string[] RequiredParts = { "FarArm", "FarLeg", "NearLeg", "Head", "Torso", "NearArm" };

        [Serializable]
        public sealed class RigLayout
        {
            public int canvasWidth, canvasHeight;
            public float pixelsPerUnit, pivotX, pivotY;
            public float[] pivotPixels;
            public BoneLayout[] bones;
            public PartLayout[] parts;
        }

        [Serializable]
        public sealed class BoneLayout
        {
            public string name, parent;
            public float x, y, endX, endY;
        }

        [Serializable]
        public sealed class PartLayout
        {
            public string name;
            public string[] bones;
            public int renderOrder;
        }

        [MenuItem("Seabreeze/Candidates/Build Skeletal Player Rig")]
        public static void BuildRig()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build the skeletal candidate outside Play mode.");
            if (!File.Exists(LayoutPath) || !File.Exists(PsbPath))
                throw new FileNotFoundException("Candidate requires both " + LayoutPath + " and " + PsbPath);

            var layout = JsonUtility.FromJson<RigLayout>(File.ReadAllText(LayoutPath));
            ValidateLayout(layout);
            var byName = layout.bones.ToDictionary(b => b.name, StringComparer.Ordinal);
            var globalIndices = layout.bones.Select((b, i) => new { b.name, index = i })
                .ToDictionary(b => b.name, b => b.index, StringComparer.Ordinal);

            AssetDatabase.ImportAsset(PsbPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = GetImporter();
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.useMosaicMode = true;
            importer.useCharacterMode = true;
            importer.spritePixelsPerUnit = layout.pixelsPerUnit;
            importer.spriteMeshType = SpriteMeshType.Tight;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.mosiacPadding = 8;
            importer.SetImporterPlatformSettings(new TextureImporterPlatformSettings
            {
                name = "DefaultTexturePlatform",
                maxTextureSize = 8192,
                textureCompression = TextureImporterCompression.Uncompressed,
                crunchedCompression = false,
                overridden = false
            });
            importer.SaveAndReimport();

            importer = GetImporter();
            var provider = (ISpriteEditorDataProvider)importer;
            provider.InitSpriteEditorDataProvider();
            var characterProvider = RequireProvider<ICharacterDataProvider>(provider);
            var boneProvider = RequireProvider<ISpriteBoneDataProvider>(provider);
            var meshProvider = RequireProvider<ISpriteMeshDataProvider>(provider);
            var character = characterProvider.GetCharacterData();
            if (character.dimension != new Vector2Int(layout.canvasWidth, layout.canvasHeight))
                throw new InvalidDataException($"PSB canvas {character.dimension} does not match layout {layout.canvasWidth}x{layout.canvasHeight}.");

            var rects = provider.GetSpriteRects();
            if (rects.Length != RequiredParts.Length)
                throw new InvalidDataException($"Expected six PSB sprites; found {rects.Length}.");
            var stableIds = new Dictionary<string, string>(StringComparer.Ordinal);
            if (character.bones != null)
                foreach (var bone in character.bones)
                    if (!string.IsNullOrEmpty(bone.name) && !string.IsNullOrEmpty(bone.guid))
                        stableIds[bone.name] = bone.guid;
            foreach (var bone in layout.bones)
                if (!stableIds.ContainsKey(bone.name)) stableIds.Add(bone.name, GUID.Generate().ToString());

            character.bones = BuildBones(layout, layout.bones.Select(b => b.name).ToArray(), Vector2.zero, byName, stableIds);
            Vector2 pivot = layout.pivotPixels != null && layout.pivotPixels.Length == 2
                ? new Vector2(layout.pivotPixels[0], layout.pivotPixels[1]) : new Vector2(layout.pivotX, layout.pivotY);
            character.pivot = new Vector2(pivot.x / layout.canvasWidth, 1f - pivot.y / layout.canvasHeight);

            var vertexCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var part in layout.parts)
            {
                var matches = rects.Where(r => r.name == part.name).ToArray();
                if (matches.Length != 1) throw new InvalidDataException("PSB requires one layer named " + part.name);
                var rect = matches[0];
                int partIndex = Array.FindIndex(character.parts, p => p.spriteId == rect.spriteID.ToString());
                if (partIndex < 0) throw new InvalidDataException("Character part missing for " + part.name);
                var characterPart = character.parts[partIndex];

                // Character part position is the trimmed layer's origin on the original document,
                // whereas SpriteRect.rect is its location in the packed mosaic texture.
                Vector2 origin = characterPart.spritePosition.position;
                var skinBones = SkinBoneNames(part);
                characterPart.bones = skinBones.Select(n => globalIndices[n]).ToArray();
                character.parts[partIndex] = characterPart;
                boneProvider.SetBones(rect.spriteID, BuildBones(layout, skinBones, origin, byName, stableIds).ToList());
                BuildMesh(layout, part, rect.rect.size, origin, byName,
                    out var vertices, out var indices, out var edges);
                meshProvider.SetVertices(rect.spriteID, vertices);
                meshProvider.SetIndices(rect.spriteID, indices);
                meshProvider.SetEdges(rect.spriteID, edges);
                vertexCounts.Add(part.name, vertices.Length);
            }

            characterProvider.SetCharacterData(character);
            EditorUtility.SetDirty(importer);
            provider.Apply();
            importer.SaveAndReimport();
            VerifyPersistedData(layout, vertexCounts);
            SaveVisualPrefab(layout, vertexCounts);
        }

        static PSDImporter GetImporter()
        {
            var importer = AssetImporter.GetAtPath(PsbPath) as PSDImporter;
            if (importer == null) throw new InvalidOperationException("Official PSD Importer is not available for " + PsbPath);
            return importer;
        }

        static T RequireProvider<T>(ISpriteEditorDataProvider provider) where T : class
        {
            var result = provider.GetDataProvider<T>();
            if (result == null) throw new InvalidOperationException("PSB does not expose " + typeof(T).Name);
            return result;
        }

        static Vector2 DocumentPoint(RigLayout layout, float x, float y) => new Vector2(x, layout.canvasHeight - y);
        static Vector2 Start(RigLayout layout, BoneLayout bone) => DocumentPoint(layout, bone.x, bone.y);
        static Vector2 End(RigLayout layout, BoneLayout bone) => DocumentPoint(layout, bone.endX, bone.endY);
        static bool IsArm(PartLayout part) => part.name == "NearArm" || part.name == "FarArm";
        // The shoulder cap shares its chest anchor. This reuses an existing skeleton bone;
        // the source limb chain in the layout remains intact and the PSB pixels are untouched.
        static string[] SkinBoneNames(PartLayout part) => IsArm(part)
            ? new[] { "Chest" }.Concat(part.bones).ToArray() : part.bones;
        static Quaternion Rotation(RigLayout layout, BoneLayout bone)
        {
            Vector2 direction = End(layout, bone) - Start(layout, bone);
            return Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }

        static SpriteBone[] BuildBones(RigLayout layout, string[] names, Vector2 origin,
            Dictionary<string, BoneLayout> byName, Dictionary<string, string> stableIds)
        {
            var indices = names.Select((name, i) => new { name, index = i })
                .ToDictionary(b => b.name, b => b.index, StringComparer.Ordinal);
            var result = new SpriteBone[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var bone = byName[names[i]];
                int parent = -1;
                if (!string.IsNullOrEmpty(bone.parent)) indices.TryGetValue(bone.parent, out parent);
                if (string.IsNullOrEmpty(bone.parent) || !indices.ContainsKey(bone.parent)) parent = -1;
                Vector3 position = Start(layout, bone) - origin;
                Quaternion rotation = Rotation(layout, bone);
                if (parent >= 0)
                {
                    var parentBone = byName[names[parent]];
                    Quaternion inverseParent = Quaternion.Inverse(Rotation(layout, parentBone));
                    position = inverseParent * (Start(layout, bone) - Start(layout, parentBone));
                    rotation = inverseParent * rotation;
                }
                result[i] = new SpriteBone
                {
                    name = bone.name,
                    guid = stableIds[bone.name],
                    parentId = parent,
                    position = position,
                    rotation = rotation,
                    length = Vector2.Distance(Start(layout, bone), End(layout, bone)),
                    color = new Color(.32f, .72f, .87f, 1f)
                };
            }
            return result;
        }

        static void BuildMesh(RigLayout layout, PartLayout part, Vector2 size, Vector2 origin,
            Dictionary<string, BoneLayout> byName, out Vertex2DMetaData[] vertices,
            out int[] indices, out Vector2Int[] edges)
        {
            var joints = part.bones.Select(n => Start(layout, byName[n]) - origin).ToArray();
            var xs = Axis(size.x, joints.Select(p => p.x));
            var ys = Axis(size.y, joints.Select(p => p.y));
            int width = xs.Length, height = ys.Length;
            if (width * height > 65000) throw new InvalidDataException("Candidate grid is too large for " + part.name);
            vertices = new Vertex2DMetaData[width * height];
            var chain = part.bones.Select(n => byName[n]).ToArray();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var position = new Vector2(xs[x], ys[y]);
                    vertices[y * width + x] = new Vertex2DMetaData
                    {
                        position = position,
                        boneWeight = PartWeight(layout, part, chain, position + origin)
                    };
                }

            indices = new int[(width - 1) * (height - 1) * 6];
            int cursor = 0;
            for (int y = 0; y < height - 1; y++)
                for (int x = 0; x < width - 1; x++)
                {
                    int a = y * width + x, b = a + 1, c = a + width, d = c + 1;
                    indices[cursor++] = a; indices[cursor++] = c; indices[cursor++] = b;
                    indices[cursor++] = b; indices[cursor++] = c; indices[cursor++] = d;
                }
            var boundary = new List<Vector2Int>();
            for (int x = 0; x < width - 1; x++)
            {
                boundary.Add(new Vector2Int(x, x + 1));
                boundary.Add(new Vector2Int((height - 1) * width + x, (height - 1) * width + x + 1));
            }
            for (int y = 0; y < height - 1; y++)
            {
                boundary.Add(new Vector2Int(y * width, (y + 1) * width));
                boundary.Add(new Vector2Int(y * width + width - 1, (y + 1) * width + width - 1));
            }
            edges = boundary.ToArray();
        }

        static float[] Axis(float extent, IEnumerable<float> joints)
        {
            if (!Finite(extent) || extent <= 0) throw new InvalidDataException("Empty sprite canvas.");
            var points = new List<float>();
            int cells = Mathf.Max(1, Mathf.CeilToInt(extent / GridSpacing));
            for (int i = 0; i <= cells; i++) points.Add(extent * i / cells);
            foreach (float joint in joints)
                foreach (float offset in new[] { -96f, -72f, -48f, -24f, -12f, 0f, 12f, 24f, 48f, 72f, 96f })
                {
                    float point = joint + offset;
                    if (point > .1f && point < extent - .1f) points.Add(point);
                }
            points.Sort();
            var unique = new List<float>();
            foreach (float point in points)
                if (unique.Count == 0 || point - unique[unique.Count - 1] > .1f) unique.Add(point);
            return unique.ToArray();
        }

        static BoneWeight PartWeight(RigLayout layout, PartLayout part, BoneLayout[] chain, Vector2 point)
        {
            var weights = ChainWeight(layout, chain, point);
            if (!IsArm(part)) return weights;

            Vector2 shoulder = Start(layout, chain[0]);
            Vector2 downArm = (End(layout, chain[0]) - shoulder).normalized;
            float distanceBelowShoulder = Vector2.Dot(point - shoulder, downArm);
            // Preserve the round sleeve cap against the torso, then hand deformation over to
            // the upper arm across 138px. At the elbow the original limb weights are unchanged.
            float chest = 1f - Mathf.SmoothStep(0, 1,
                Mathf.InverseLerp(-28f, 110f, distanceBelowShoulder));
            float limb = 1f - chest;
            var influences = new[]
            {
                new KeyValuePair<int, float>(0, chest),
                new KeyValuePair<int, float>(weights.boneIndex0 + 1, weights.weight0 * limb),
                new KeyValuePair<int, float>(weights.boneIndex1 + 1, weights.weight1 * limb)
            }.OrderByDescending(influence => influence.Value).ToArray();
            return new BoneWeight
            {
                boneIndex0 = influences[0].Key, weight0 = influences[0].Value,
                boneIndex1 = influences[1].Key, weight1 = influences[1].Value,
                boneIndex2 = influences[2].Key, weight2 = influences[2].Value
            };
        }

        // Project onto this part's own bone chain. Blend only neighboring bones around each joint,
        // keeping elbow/knee/wrist/ankle deformation continuous without affecting the opposite limb.
        static BoneWeight ChainWeight(RigLayout layout, BoneLayout[] chain, Vector2 point)
        {
            var lengths = new float[chain.Length];
            float total = 0, nearestDistance = float.PositiveInfinity, coordinate = 0;
            for (int i = 0; i < chain.Length; i++)
            {
                Vector2 a = Start(layout, chain[i]), delta = End(layout, chain[i]) - a;
                float length = delta.magnitude;
                lengths[i] = length;
                float t = Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude);
                float distance = (point - a - delta * t).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    coordinate = total + t * length;
                }
                total += length;
            }
            float boundary = 0;
            for (int i = 0; i < chain.Length - 1; i++)
            {
                boundary += lengths[i];
                // Broad transitions around cloth-covered elbows/knees distribute the bend;
                // small ankle/wrist transitions keep the shoe sole and hand from stretching.
                string joint = chain[i + 1].name;
                float desiredRadius = joint.EndsWith("Shin", StringComparison.Ordinal) ? 85f :
                    joint.EndsWith("Forearm", StringComparison.Ordinal) ? 64f :
                    joint.EndsWith("Foot", StringComparison.Ordinal) ? 24f :
                    joint.EndsWith("Hand", StringComparison.Ordinal) ? 18f : 60f;
                float radius = Mathf.Min(desiredRadius, Mathf.Min(lengths[i], lengths[i + 1]) * .34f);
                if (coordinate < boundary - radius) return new BoneWeight { boneIndex0 = i, weight0 = 1f };
                if (coordinate <= boundary + radius)
                {
                    float right = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(boundary - radius, boundary + radius, coordinate));
                    float left = 1f - right;
                    return left >= right
                        ? new BoneWeight { boneIndex0 = i, weight0 = left, boneIndex1 = i + 1, weight1 = right }
                        : new BoneWeight { boneIndex0 = i + 1, weight0 = right, boneIndex1 = i, weight1 = left };
                }
            }
            return new BoneWeight { boneIndex0 = chain.Length - 1, weight0 = 1f };
        }

        static void VerifyPersistedData(RigLayout layout, Dictionary<string, int> expectedVertices)
        {
            var provider = (ISpriteEditorDataProvider)GetImporter();
            provider.InitSpriteEditorDataProvider();
            var character = RequireProvider<ICharacterDataProvider>(provider).GetCharacterData();
            if (character.bones == null || character.bones.Length != layout.bones.Length)
                throw new InvalidDataException("Shared character bones did not survive reimport.");
            var bones = RequireProvider<ISpriteBoneDataProvider>(provider);
            var meshes = RequireProvider<ISpriteMeshDataProvider>(provider);
            foreach (var part in layout.parts)
            {
                var rect = provider.GetSpriteRects().Single(r => r.name == part.name);
                var vertices = meshes.GetVertices(rect.spriteID);
                int skinBoneCount = SkinBoneNames(part).Length;
                if (bones.GetBones(rect.spriteID).Count != skinBoneCount || vertices.Length != expectedVertices[part.name] ||
                    meshes.GetIndices(rect.spriteID).Length == 0)
                    throw new InvalidDataException("Persistent skinning data is incomplete for " + part.name);
                foreach (var vertex in vertices)
                {
                    var w = vertex.boneWeight;
                    if (Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1f) > .001f ||
                        w.boneIndex0 < 0 || w.boneIndex0 >= skinBoneCount ||
                        (w.weight1 > 0 && (w.boneIndex1 < 0 || w.boneIndex1 >= skinBoneCount)) ||
                        (w.weight2 > 0 && (w.boneIndex2 < 0 || w.boneIndex2 >= skinBoneCount)))
                        throw new InvalidDataException("Invalid local bone weights for " + part.name);
                }
            }
        }

        static void SaveVisualPrefab(RigLayout layout, Dictionary<string, int> vertexCounts)
        {
            var imported = AssetDatabase.LoadAllAssetsAtPath(PsbPath).OfType<GameObject>().FirstOrDefault();
            if (imported == null) throw new InvalidOperationException("PSD Importer did not generate its character prefab.");
            var root = new GameObject("PlayerRigCandidate");
            try
            {
                var facing = new GameObject("FacingRoot");
                facing.transform.SetParent(root.transform, false);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(imported, facing.transform);
                instance.name = "PSBInstance";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                var animator = root.AddComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                var skins = instance.GetComponentsInChildren<SpriteSkin>(true);
                if (skins.Length != layout.parts.Length)
                    throw new InvalidDataException($"Expected six SpriteSkins, got {skins.Length}.");
                var boneTransformsByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (var skin in skins)
                {
                    var renderer = skin.GetComponent<SpriteRenderer>();
                    var part = layout.parts.Single(p => p.name == renderer.sprite.name);
                    var skinBones = SkinBoneNames(part);
                    renderer.sortingOrder = part.renderOrder;
                    if (skin.boneTransforms.Length != skinBones.Length || renderer.sprite.GetBindPoses().Length != skinBones.Length)
                        throw new InvalidDataException("Imported bind pose mismatch for " + part.name);
                    for (int i = 0; i < skinBones.Length; i++)
                    {
                        string expectedName = skinBones[i];
                        var boneTransform = skin.boneTransforms[i];
                        if (boneTransform == null ||
                            (boneTransformsByName.TryGetValue(expectedName, out var existing) && existing != boneTransform))
                            throw new InvalidDataException("Conflicting imported bone mapping for " + expectedName);
                        // PSD import may suffix a bone that shares a layer name (Head -> Head_1).
                        // Preserve its binding reference and normalize only the bone GameObject name.
                        boneTransform.gameObject.name = expectedName;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(boneTransform.gameObject);
                        boneTransformsByName[expectedName] = boneTransform;
                    }
                    var state = skin.SetBoneTransforms(skin.boneTransforms);
                    if (state != SpriteSkinState.Ready)
                        throw new InvalidDataException("SpriteSkin is not ready for " + part.name + ": " + state);
                }
                foreach (var bone in layout.bones)
                    if (!boneTransformsByName.ContainsKey(bone.name))
                        throw new InvalidDataException("No bound transform was generated for " + bone.name);

                if (!AssetDatabase.IsValidFolder(CandidateRoot + "/Prefabs")) AssetDatabase.CreateFolder(CandidateRoot, "Prefabs");
                var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (saved == null) throw new IOException("Could not save " + PrefabPath);
                AssetDatabase.SaveAssets();

                var report = new StringBuilder("Built candidate visual rig: " + PrefabPath + "\nBone paths relative to root Animator:\n");
                foreach (var bone in layout.bones)
                {
                    var transform = boneTransformsByName[bone.name];
                    report.Append(bone.name).Append(" = ").Append(AnimationUtility.CalculateTransformPath(transform, root.transform)).Append('\n');
                }
                report.Append("Persistent vertex counts: ").Append(string.Join(", ", vertexCounts.Select(p => p.Key + "=" + p.Value)));
                Debug.Log(report.ToString(), saved);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static void ValidateLayout(RigLayout layout)
        {
            if (layout == null || layout.canvasWidth <= 0 || layout.canvasHeight <= 0 ||
                !Finite(layout.pixelsPerUnit) || layout.pixelsPerUnit <= 0 || layout.bones == null || layout.bones.Length == 0 ||
                layout.parts == null || layout.parts.Length != RequiredParts.Length)
                throw new InvalidDataException("Invalid rig layout header, bones, or six-layer parts list.");
            Vector2 pivot = layout.pivotPixels != null && layout.pivotPixels.Length == 2
                ? new Vector2(layout.pivotPixels[0], layout.pivotPixels[1]) : new Vector2(layout.pivotX, layout.pivotY);
            if (!Finite(pivot.x) || !Finite(pivot.y) || pivot.x < 0 || pivot.y < 0 || pivot.x > layout.canvasWidth || pivot.y > layout.canvasHeight)
                throw new InvalidDataException("Rig pivot must be inside the source canvas.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bone in layout.bones)
            {
                if (bone == null || string.IsNullOrWhiteSpace(bone.name) || !names.Add(bone.name) ||
                    !Finite(bone.x) || !Finite(bone.y) || !Finite(bone.endX) || !Finite(bone.endY) ||
                    Vector2.Distance(new Vector2(bone.x, bone.y), new Vector2(bone.endX, bone.endY)) < .1f)
                    throw new InvalidDataException("Rig bones require unique names, finite positions, and nonzero lengths.");
            }
            var byName = layout.bones.ToDictionary(b => b.name, StringComparer.Ordinal);
            foreach (var bone in layout.bones)
            {
                var ancestors = new HashSet<string>(StringComparer.Ordinal) { bone.name };
                string parent = bone.parent;
                while (!string.IsNullOrEmpty(parent))
                {
                    if (!byName.TryGetValue(parent, out var parentBone) || !ancestors.Add(parent))
                        throw new InvalidDataException("Missing parent or cyclic bone hierarchy at " + bone.name);
                    parent = parentBone.parent;
                }
            }
            if (layout.parts.Any(p => p == null || string.IsNullOrEmpty(p.name)) ||
                !new HashSet<string>(layout.parts.Select(p => p.name), StringComparer.Ordinal).SetEquals(RequiredParts))
                throw new InvalidDataException("Rig part names must match the six authored PSB layers.");
            foreach (var part in layout.parts)
            {
                if (part.bones == null || part.bones.Length == 0 || part.bones.Distinct().Count() != part.bones.Length ||
                    part.bones.Any(n => string.IsNullOrEmpty(n) || !byName.ContainsKey(n)))
                    throw new InvalidDataException("Invalid layer bone mapping for " + part.name);
                for (int i = 1; i < part.bones.Length; i++)
                {
                    var parent = byName[part.bones[i - 1]];
                    var child = byName[part.bones[i]];
                    if (child.parent != parent.name || Vector2.Distance(End(layout, parent), Start(layout, child)) > 2f)
                        throw new InvalidDataException("Part bones must be a continuous proximal-to-distal chain: " + part.name);
                }
            }
        }
    }
}
#endif
