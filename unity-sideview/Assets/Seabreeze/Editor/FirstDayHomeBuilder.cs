using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Seabreeze.Editor
{
    // Production authoring command; only the new guest-room scene is created/updated.
    public static class FirstDayHomeBuilder
    {
        [MenuItem("Seabreeze/First Day/Build Guest Room")]
        public static void Build()
        {
            const string art="Assets/Seabreeze/Resources/FirstDay/player_room.png";
            const string path="Assets/Seabreeze/Scenes/PlayerHome.unity";
            var importer=(TextureImporter)AssetImporter.GetAtPath(art);
            if(importer==null)throw new System.InvalidOperationException("Guest room art is missing.");
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.spritePixelsPerUnit=120;importer.filterMode=FilterMode.Bilinear;importer.maxTextureSize=4096;importer.mipmapEnabled=false;importer.SaveAndReimport();
            var prior=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                new GameObject("Game lifecycle").AddComponent<GameBootstrap>();
                var stage=new GameObject("Your room by the sea").AddComponent<WorldScene>();
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(art);
                float width=sprite.bounds.size.x,height=sprite.bounds.size.y;
                stage.place=PlaceId.PlayerHome;stage.width=width;stage.streetSpawn=4.2f;
                stage.cameraSize=3.9f;stage.cameraY=2.25f;stage.floorImageFraction=.211f;
                var backdrop=new GameObject("Handpainted guest room").AddComponent<SpriteRenderer>();backdrop.transform.SetParent(stage.transform,false);
                backdrop.sprite=sprite;backdrop.sortingOrder=-20;backdrop.transform.position=new Vector3(0,height*(.789f-.5f),0);stage.backdrop=backdrop;
                stage.gameObject.AddComponent<IslandAtmosphere>().indoor=true;
                Ground(stage.transform,"Room floor",new Vector2(0,-.5f),new Vector2(width+2,1));
                Ground(stage.transform,"Left wall",new Vector2(-width/2-.25f,4),new Vector2(.5f,12));
                Ground(stage.transform,"Right wall",new Vector2(width/2+.25f,4),new Vector2(.5f,12));
                var bed=new GameObject("A bed for the first night");bed.transform.SetParent(stage.transform,false);bed.transform.position=new Vector3((.215f-.5f)*width,0,0);
                var interact=bed.AddComponent<Interactable>();interact.id="firstday_bed";interact.label="休息";interact.radius=1.8f;interact.promptOffset=new Vector3(0,1.8f,0);
                var door=new GameObject("Guest room exit");door.transform.SetParent(stage.transform,false);door.transform.position=new Vector3(width*.43f,0,0);
                var exit=door.AddComponent<Interactable>();exit.id="firstday_room_exit";exit.label="出门 · 码头";exit.isDoor=true;exit.destination=PlaceId.Harbor;exit.radius=1.4f;exit.promptOffset=new Vector3(0,2.3f,0);
                EditorSceneManager.SaveScene(scene,path);
                var scenes=EditorBuildSettings.scenes.ToList();if(!scenes.Any(s=>s.path==path))scenes.Add(new EditorBuildSettingsScene(path,true));EditorBuildSettings.scenes=scenes.ToArray();
                AssetDatabase.SaveAssets();
            }
            finally {EditorSceneManager.CloseScene(scene,true);if(prior.IsValid())SceneManager.SetActiveScene(prior);}
        }
        static void Ground(Transform root,string name,Vector2 position,Vector2 size)
        {var go=new GameObject(name);go.transform.SetParent(root,false);go.transform.position=position;go.layer=8;go.AddComponent<BoxCollider2D>().size=size;}
    }
}
