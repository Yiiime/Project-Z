using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZFromMoon.Core;
using ZFromMoon.Interaction;
using ZFromMoon.World;

namespace ZFromMoon.EditorTools
{
    /// <summary>Repairs saved route data without rebuilding art, collision or scene geometry.</summary>
    public static class LocationRouteRepair
    {
        private const string AppPath = "Assets/_Game/Scenes/App.unity";
        private const string LocationDirectory = "Assets/_Game/Scenes/Locations";

        private sealed class Location
        {
            public Scene Scene;
            public SceneContext Context;
            public readonly List<Route> Routes = new List<Route>();
        }

        private sealed class Route
        {
            public SceneExit Exit;
            public Location Neighbor;
            public bool IsLeft;
            public Transform Anchor;
        }

        [MenuItem("Z From Moon/Repair Location Routes")]
        public static void Repair()
        {
            RequireEditableScenes();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            string activePath = SceneManager.GetActiveScene().path;
            try
            {
                // Load and resolve the whole graph before changing any serialized values.
                var locations = new Dictionary<string, Location>(StringComparer.Ordinal);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var paths = Directory.GetFiles(LocationDirectory, "*.unity");
                Array.Sort(paths, StringComparer.Ordinal);
                foreach (string rawPath in paths)
                {
                    string path = rawPath.Replace('\\', '/');
                    Scene scene = OpenAdditive(path);
                    var context = FindSingle<SceneContext>(scene);
                    if (string.IsNullOrWhiteSpace(context.LocationId) || !ids.Add(context.LocationId)
                        || context.InitialSpawn == null || context.InitialSpawn.gameObject.scene != scene)
                        throw new InvalidOperationException($"Invalid location identity or initial spawn: {path}");
                    locations.Add(path, new Location { Scene = scene, Context = context });
                }
                if (locations.Count == 0) throw new InvalidOperationException("No location scenes found.");

                foreach (var location in locations.Values)
                {
                    var sides = new HashSet<bool>();
                    foreach (var exit in FindAll<SceneExit>(location.Scene))
                    {
                        bool left = exit.name == "Exit_Left";
                        if ((!left && exit.name != "Exit_Right") || !sides.Add(left))
                            throw new InvalidOperationException($"Ambiguous exit side in {location.Scene.path}: {exit.name}");
                        if (!locations.TryGetValue(exit.TargetScenePath, out var neighbor))
                            throw new InvalidOperationException($"Unknown route destination: {exit.TargetScenePath}");
                        var route = new Route { Exit = exit, Neighbor = neighbor, IsLeft = left };
                        route.Anchor = ResolveAnchor(location.Context, EntryFrom(neighbor.Context.LocationId), left);
                        if (route.Anchor == null || route.Anchor.gameObject.scene != location.Scene
                            || route.Anchor == location.Context.InitialSpawn)
                            throw new InvalidOperationException($"Missing independent arrival anchor: {location.Scene.path}/{exit.name}");
                        var box = exit.GetComponent<BoxCollider2D>();
                        if (box == null || !box.isTrigger)
                            throw new InvalidOperationException($"Exit needs its existing trigger collider: {exit.name}");
                        RequireProperty(new SerializedObject(exit), "interactionId");
                        location.Routes.Add(route);
                    }
                    location.Routes.Sort((a, b) => b.IsLeft.CompareTo(a.IsLeft));
                }
                foreach (var location in locations.Values)
                {
                    foreach (var route in location.Routes)
                    {
                        if (!route.Neighbor.Routes.Exists(reverse => reverse.Neighbor == location))
                            throw new InvalidOperationException($"No return route: {location.Scene.path} -> {route.Neighbor.Scene.path}");
                    }
                }

                Scene app = OpenAdditive(AppPath);
                // Resolve required App objects before beginning the migration.
                FindSingle<GameSession>(app);
                FindSingle<PlayerInteractor>(app);
                FindSingle<CameraFollow>(app);
                var existingRouters = FindAll<LocationRouter>(app);
                if (existingRouters.Count > 1) throw new InvalidOperationException("App contains multiple LocationRouters.");
                if (existingRouters.Count == 1)
                {
                    var routerData = new SerializedObject(existingRouters[0]);
                    RequireProperty(routerData, "session");
                    RequireProperty(routerData, "interactor");
                    RequireProperty(routerData, "cameraFollow");
                }
                InteractionLayer();

                int exitCount = 0;
                foreach (var location in locations.Values)
                {
                    var so = new SerializedObject(location.Context);
                    so.FindProperty("entryId").stringValue = "start";
                    var entries = so.FindProperty("entries");
                    entries.arraySize = location.Routes.Count + 1;
                    SetEntry(entries.GetArrayElementAtIndex(0), "start", location.Context.InitialSpawn);
                    for (int i = 0; i < location.Routes.Count; i++)
                    {
                        var route = location.Routes[i];
                        SetEntry(entries.GetArrayElementAtIndex(i + 1), EntryFrom(route.Neighbor.Context.LocationId), route.Anchor);
                        // Names are stable for future repairs; their transforms remain untouched.
                        route.Anchor.name = route.IsLeft ? "Entry_Left" : "Entry_Right";
                        route.Exit.Configure(route.Neighbor.Scene.path, EntryFrom(location.Context.LocationId));
                        ConfigureExitInteraction(route.Exit, location.Context.LocationId,
                            route.Neighbor.Context.DisplayName, route.IsLeft, route.Anchor.position.y);
                        exitCount++;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Save(location.Scene);
                }
                WireAppRouter(app);
                Save(app);
                Debug.Log($"[LocationRouteRepair] Saved {locations.Count} locations and {exitCount} E exits; App router wired. Art, collision and existing transforms were preserved.");
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
                var active = SceneManager.GetSceneByPath(activePath);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            }
        }

        internal static void RequireEditableScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Location changes require Edit mode after compilation has finished.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty || string.IsNullOrEmpty(scene.path))
                    throw new InvalidOperationException($"Save or close the unsaved scene before repairing routes: {scene.name}");
            }
        }

        internal static string EntryFrom(string locationId) => "from_" + locationId;

        internal static T FindSingle<T>(Scene scene) where T : Component
        {
            var components = FindAll<T>(scene);
            if (components.Count != 1)
                throw new InvalidOperationException($"Expected one {typeof(T).Name} in {scene.path}, found {components.Count}.");
            return components[0];
        }

        private static List<T> FindAll<T>(Scene scene) where T : Component
        {
            var result = new List<T>();
            foreach (var root in scene.GetRootGameObjects()) result.AddRange(root.GetComponentsInChildren<T>(true));
            return result;
        }

        internal static void WireAppRouter(Scene app)
        {
            var session = FindSingle<GameSession>(app);
            var interactor = FindSingle<PlayerInteractor>(app);
            var camera = FindSingle<CameraFollow>(app);
            var routers = FindAll<LocationRouter>(app);
            if (routers.Count > 1) throw new InvalidOperationException("App contains multiple LocationRouters.");
            var router = routers.Count == 1 ? routers[0] : session.gameObject.AddComponent<LocationRouter>();
            var so = new SerializedObject(router);
            RequireProperty(so, "session").objectReferenceValue = session;
            RequireProperty(so, "interactor").objectReferenceValue = interactor;
            RequireProperty(so, "cameraFollow").objectReferenceValue = camera;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(router);
        }

        internal static void ConfigureExitInteraction(SceneExit exit, string sourceId, string destinationName, bool isLeft, float groundY)
        {
            exit.gameObject.layer = InteractionLayer();
            var point = GetOrCreateChild(exit.transform, "InteractionPoint");
            var prompt = GetOrCreateChild(exit.transform, "PromptAnchor");
            var position = exit.transform.position;
            point.position = new Vector3(position.x, groundY + 0.9f, position.z);
            prompt.position = new Vector3(position.x + (isLeft ? 0.5f : -0.5f), groundY + 3.4f, position.z);
            var so = new SerializedObject(exit);
            RequireProperty(so, "interactionId").stringValue = sourceId + (isLeft ? ".exit.left" : ".exit.right");
            RequireProperty(so, "displayName").stringValue = destinationName;
            RequireProperty(so, "actionLabel").stringValue = "前往" + destinationName;
            RequireProperty(so, "interactionPoint").objectReferenceValue = point;
            RequireProperty(so, "promptAnchor").objectReferenceValue = prompt;
            RequireProperty(so, "priority").intValue = 0;
            RequireProperty(so, "interactionEnabled").boolValue = true;
            RequireProperty(so, "requiredFlag").stringValue = string.Empty;
            RequireProperty(so, "responseText").stringValue = string.Empty;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(exit);
        }

        private static int InteractionLayer()
        {
            int layer = LayerMask.NameToLayer("Interactable");
            if (layer != 10) throw new InvalidOperationException("Expected Interactable layer 10 to match the player interaction mask.");
            return layer;
        }

        private static Transform GetOrCreateChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null) return child;
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Transform ResolveAnchor(SceneContext context, string canonicalId, bool left)
        {
            var entries = new SerializedObject(context).FindProperty("entries");
            for (int i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("id").stringValue == canonicalId)
                    return entry.FindPropertyRelative("anchor").objectReferenceValue as Transform;
            }
            var named = context.transform.Find(left ? "Entry_Left" : "Entry_Right");
            if (named != null) return named;
            // The original WorldBuilder stored right at index 1, left at index 2.
            int legacyIndex = left ? 2 : 1;
            return entries.arraySize > legacyIndex
                ? entries.GetArrayElementAtIndex(legacyIndex).FindPropertyRelative("anchor").objectReferenceValue as Transform
                : null;
        }

        private static void SetEntry(SerializedProperty entry, string id, Transform anchor)
        {
            entry.FindPropertyRelative("id").stringValue = id;
            entry.FindPropertyRelative("anchor").objectReferenceValue = anchor;
        }

        private static SerializedProperty RequireProperty(SerializedObject so, string name)
        {
            return so.FindProperty(name) ?? throw new InvalidOperationException($"Missing serialized field {name} on {so.targetObject.GetType().Name}; wait for the route scripts to compile.");
        }

        private static Scene OpenAdditive(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Scene not found", path);
            var scene = SceneManager.GetSceneByPath(path);
            return scene.IsValid() && scene.isLoaded ? scene : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        private static void Save(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException($"Could not save {scene.path}");
        }
    }
}
