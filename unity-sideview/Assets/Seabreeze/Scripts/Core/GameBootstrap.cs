using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Seabreeze
{
    [DefaultExecutionOrder(-1000)]
    public class GameBootstrap : MonoBehaviour
    {
        static GameBootstrap instance;
        GameSession session;
        WorldDirector world;
        GameUI ui;
        StartPageView startPage;
        bool entering, smokeRun;
        string resumePlace;

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
            smokeRun = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-memorySmokeOutput") >= 0;
            session = gameObject.AddComponent<GameSession>();
            session.Initialize(!smokeRun);
            resumePlace = session.State.place;
            string saveError = SaveRepository.LastError;
            var preview = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(session.State));
            if (!smokeRun) Time.timeScale = 0;
            world = gameObject.AddComponent<WorldDirector>();
            world.Initialize();
            ui = gameObject.AddComponent<GameUI>();
            gameObject.AddComponent<MusicDirector>().Initialize(session);
            ui.Initialize();
            gameObject.AddComponent<AmbientAudio>().Initialize();
            gameObject.AddComponent<PhotoCaptureService>().Initialize(session);
            session.FirstDay = gameObject.AddComponent<FirstDayDirector>();
            session.FirstDay.Initialize(session, world);
            if (smokeRun)
                InitializeMemory();
            else
            {
                ui.SetTitleVisible(true);
                startPage = gameObject.AddComponent<StartPageView>();
                startPage.Initialize(session.HasResumeSave, preview, EnterGame);
                if (!session.HasResumeSave && !string.IsNullOrEmpty(saveError))
                    startPage.ShowSaveNotice("暂时无法读取上次的进度。你可以重新开始一段小岛生活；确认前，旧存档会原样保留。");
            }
        }

        IEnumerator Start()
        {
            // Automated memory acceptance keeps its existing direct-entry contract.
            if (!smokeRun) yield break;
            yield return null;
            if (session != null && SceneManager.GetActiveScene().name != resumePlace)
            {
                world.Travel(System.Enum.TryParse<PlaceId>(resumePlace, out var place) ? place : PlaceId.Street);
                while (world.IsTransitioning) yield return null;
            }
        }

        void InitializeMemory()
        {
            if (!MemoryRuntimeConfig.Enabled || session.Memory != null) return;
            session.Memory = gameObject.AddComponent<MemoryGameController>();
            session.Memory.Initialize(session, world);
            if (smokeRun) gameObject.AddComponent<UnifiedMemorySmokeRunner>();
        }

        void EnterGame(bool newGame)
        {
            if (entering || !session.IsAtTitle) return;
            entering = true;
            startPage.ShowLoading();
            StartCoroutine(EnterGameRoutine(newGame));
        }

        IEnumerator EnterGameRoutine(bool newGame)
        {
            // Give the loading label a frame before scene import/instantiation work.
            yield return null;
            if (!session.PrepareTitleEntry(newGame))
            {
                entering = false;
                startPage.ShowError("暂时无法读取上次的进度。请重试，或重新开始一段小岛生活。");
                yield break;
            }
            var destination = session.CurrentPlace;
            if (!Application.CanStreamedLevelBeLoaded(destination.ToString())) destination = PlaceId.Street;
            if (!Application.CanStreamedLevelBeLoaded(destination.ToString()))
            {
                entering = false;
                startPage.ShowError("小岛还没有准备好。请重新打开游戏后再试。");
                yield break;
            }
            // Always reattach, including the same map: Continue must restore its saved X,
            // and New Game must not inherit the preview scene's position or story flags.
            world.Travel(destination);
            while (world.IsTransitioning) yield return null;
            if (world.Stage == null || world.Stage.place != destination)
            {
                entering = false;
                startPage.ShowError("小岛暂时没有加载完成。请再试一次。");
                yield break;
            }
            InitializeMemory();
            yield return null; // Let the existing NPC spawner attach to the destination.
            yield return startPage.Dismiss();
            session.IsAtTitle = false;
            Time.timeScale = 1;
            ui.SetTitleVisible(false);
            session.Story.Evaluate();
            session.NotifyChanged();
            session.Save();
            Destroy(startPage);
            startPage = null;
            session.FirstDay.Resume();
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
