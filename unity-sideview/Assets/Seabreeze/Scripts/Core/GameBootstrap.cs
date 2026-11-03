using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Seabreeze
{
    [DefaultExecutionOrder(-1000)]
    public class GameBootstrap : MonoBehaviour
    {
        static GameBootstrap instance;
        string resumePlace;
        bool resume;
        void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            var session = gameObject.AddComponent<GameSession>();
            session.Initialize();
            resumePlace = session.State.place;
            resume = session.State.started;
            var world = gameObject.AddComponent<WorldDirector>();
            world.Initialize();
            var ui = gameObject.AddComponent<GameUI>();
            ui.Initialize();
            gameObject.AddComponent<AmbientAudio>().Initialize();
        }

        IEnumerator Start()
        {
            yield return null;
            var session = GameSession.Instance;
            if (session == null)
                yield break;
            if (resume && SceneManager.GetActiveScene().name != resumePlace)
            {
                WorldDirector.Instance.Travel(System.Enum.TryParse<PlaceId>(resumePlace, out var place) ? place : PlaceId.Street);
                while (WorldDirector.Instance.IsTransitioning)
                    yield return null;
            }
        }

        void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
                Time.timeScale = 1;
            }
        }
    }
}
