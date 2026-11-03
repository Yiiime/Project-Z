using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZFromMoon.World;

namespace ZFromMoon.Core
{
    /// <summary>Composition root in App. The App scene stays loaded for the session.</summary>
    [DisallowMultipleComponent]
    public sealed class GameSession : MonoBehaviour
    {
        [SerializeField] private SessionInput input;
        [SerializeField] private string initialScenePath = "Assets/_Game/Scenes/SeasidePlaceholder.unity";
        [SerializeField, Min(1)] private int initialDay = 1;
        [SerializeField] private TimeSlot initialTimeSlot = TimeSlot.Evening;
        private GameMode resumeMode = GameMode.Exploration;
        private bool travelInProgress;

        public GameModeController Modes { get; private set; }
        public GameStateService State { get; private set; }
        public SessionInput Input => input;
        public SceneContext Location { get; private set; }
        public bool IsReady => State != null && Location != null;
        public bool IsTraveling => travelInProgress;
        public event Action Ready;
        public event Action<string> LocationChanged;

        private void Awake()
        {
            Modes = new GameModeController();
            if (input == null)
            {
                Debug.LogError("GameSession requires an explicitly assigned SessionInput.", this);
                enabled = false;
                return;
            }
        }

        private IEnumerator Start()
        {
            // All PlayerInput/EventSystem OnEnable calls have completed before map ownership begins.
            input.Initialize(Modes);
            // Initial loading only. The router owns modes and fades for subsequent travel.
            Scene locationScene = SceneManager.GetSceneByPath(initialScenePath);
            if (!locationScene.isLoaded)
            {
                AsyncOperation load;
                try
                {
                    load = SceneManager.LoadSceneAsync(initialScenePath, LoadSceneMode.Additive);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Cannot load initial location: {initialScenePath}\n{exception.Message}", this);
                    yield break;
                }
                if (load == null) yield break;
                yield return load;
                locationScene = SceneManager.GetSceneByPath(initialScenePath);
            }

            SceneContext context = null;
            foreach (GameObject root in locationScene.GetRootGameObjects())
            {
                foreach (SceneContext candidate in root.GetComponentsInChildren<SceneContext>(true))
                {
                    if (context != null)
                    {
                        Debug.LogError("Initial location must contain exactly one SceneContext.", this);
                        yield break;
                    }
                    context = candidate;
                }
            }
            if (context == null || !context.IsConfigured)
            {
                Debug.LogError("Initial location needs a configured SceneContext and spawn anchor.", this);
                yield break;
            }

            Location = context;
            State = new GameStateService(initialDay, initialTimeSlot, context.LocationId, context.EntryId);
            SceneManager.SetActiveScene(locationScene);
            if (isActiveAndEnabled)
                Modes.SetMode(GameMode.Exploration);
            else
                resumeMode = GameMode.Exploration;
            Ready?.Invoke();
        }

        /// <summary>Validates a loaded destination before replacing the current location. Does not change modes.</summary>
        public IEnumerator TravelRoutine(string scenePath, string entryId, Action<bool> completed)
        {
            if (!isActiveAndEnabled || !IsReady || travelInProgress || Modes.Current != GameMode.Transition
                || string.IsNullOrWhiteSpace(scenePath) || string.IsNullOrWhiteSpace(entryId))
            {
                CompleteTravel(completed, false);
                yield break;
            }

            travelInProgress = true;
            bool committed = false;
            SceneContext previousLocation = Location;
            Scene previous = previousLocation.gameObject.scene;
            Scene destination = default;
            bool ownsDestination = false;

            try
            {
                if (string.Equals(previous.path, scenePath, StringComparison.Ordinal))
                {
                    destination = previous;
                }
                else
                {
                    destination = SceneManager.GetSceneByPath(scenePath);
                    if (destination.IsValid() && destination.isLoaded)
                    {
                        Debug.LogError($"Destination is already loaded outside this travel request: {scenePath}", this);
                        yield break;
                    }

                    AsyncOperation load = null;
                    try
                    {
                        load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError($"Cannot load location '{scenePath}': {exception.Message}", this);
                    }
                    if (load == null) yield break;
                    yield return load;
                    destination = SceneManager.GetSceneByPath(scenePath);
                    ownsDestination = destination.IsValid() && destination.isLoaded;
                }

                if (isActiveAndEnabled && TryResolveLocation(destination, entryId, out SceneContext context))
                    committed = TryCommitLocation(destination, context, entryId, previousLocation);

                if (!committed)
                {
                    if (ownsDestination) yield return UnloadLocation(destination);
                    yield break;
                }

                NotifyLocationChanged(entryId);
                if (destination != previous) yield return UnloadLocation(previous);
            }
            finally
            {
                travelInProgress = false;
                CompleteTravel(completed, committed);
            }
        }

        private bool TryResolveLocation(Scene scene, string entryId, out SceneContext context)
        {
            context = null;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("The destination scene did not load successfully.", this);
                return false;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (SceneContext candidate in root.GetComponentsInChildren<SceneContext>(true))
                {
                    if (context != null)
                    {
                        Debug.LogError($"Location '{scene.path}' must contain exactly one SceneContext.", this);
                        return false;
                    }
                    context = candidate;
                }
            }

            if (context == null || !context.isActiveAndEnabled || !context.IsConfigured
                || !context.TryGetEntry(entryId, out _))
            {
                Debug.LogError($"Location '{scene.path}' has no valid, unique entry '{entryId}'.", this);
                return false;
            }
            return true;
        }

        private bool TryCommitLocation(Scene scene, SceneContext context, string entryId, SceneContext previous)
        {
            try
            {
                if (!SceneManager.SetActiveScene(scene)) return false;
            }
            catch (Exception exception)
            {
                Debug.LogError($"Cannot activate location '{scene.path}': {exception.Message}", this);
                return false;
            }

            Location = context;
            try
            {
                State.SetLocation(context.LocationId, entryId);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                // State publishes its snapshot before invoking listeners. A listener failure
                // must not roll back the scene while leaving the new snapshot committed.
                if (State.Snapshot.LocationId != context.LocationId || State.Snapshot.EntryId != entryId)
                {
                    Location = previous;
                    SceneManager.SetActiveScene(previous.gameObject.scene);
                    return false;
                }
            }
            return true;
        }

        private void NotifyLocationChanged(string entryId)
        {
            if (LocationChanged == null) return;
            foreach (Action<string> listener in LocationChanged.GetInvocationList())
            {
                try { listener(entryId); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }
        }

        private IEnumerator UnloadLocation(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            // An unload failure must not leave a second set of visible art and colliders active.
            foreach (GameObject root in scene.GetRootGameObjects()) root.SetActive(false);
            AsyncOperation unload = null;
            try
            {
                unload = SceneManager.UnloadSceneAsync(scene);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Cannot unload location '{scene.path}': {exception.Message}", this);
            }
            if (unload != null) yield return unload;
            if (scene.isLoaded)
                Debug.LogError($"Location '{scene.path}' remains loaded but disabled after cleanup.", this);
        }

        private void CompleteTravel(Action<bool> completed, bool succeeded)
        {
            try { completed?.Invoke(succeeded); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        /// <summary>Called by the router only after its fade has finished, including failed travel.</summary>
        public void CompleteTravelMode()
        {
            if (!IsReady || IsTraveling || Modes.Current != GameMode.Transition) return;
            // A disabled session must also resume exploration when it is enabled again.
            resumeMode = GameMode.Exploration;
            if (isActiveAndEnabled) Modes.SetMode(GameMode.Exploration);
        }

        private void OnEnable()
        {
            if (IsReady)
                Modes.SetMode(resumeMode);
        }

        private void OnDisable()
        {
            if (Modes != null)
                resumeMode = Modes.Current;
            Modes?.SetMode(GameMode.Transition);
        }
    }
}
