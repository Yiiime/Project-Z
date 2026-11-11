using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Seabreeze.Editor
{
    // Authored island backgrounds, ground lines, and bidirectional routes.
    public static class IslandMapBuilder
    {
        const string Art="Assets/Seabreeze/Art/Island/", Scenes="Assets/Seabreeze/Scenes/";
        const string CatalogPath="Assets/Seabreeze/Resources/Content/IslandMap.asset";
        sealed class Layout
        {
            public PlaceId place; public string label,source; public int row,col; public float footY,ppu,width,height,pixelWidth;
            public bool indoor;
            public Layout(PlaceId p,string l,string s,int r,int c,float y,bool inside=false)
            {place=p;label=l;source=s;row=r;col=c;footY=y;indoor=inside;ppu=inside?165:120;}
        }
        static readonly Layout[] Maps={
            new Layout(PlaceId.ResidentialCenter,"居民街","居民街-1110.png",0,0,1200),
            new Layout(PlaceId.Forest,"居民街 · 森林入口","居民街-森林入口.png",0,1,1175),
            new Layout(PlaceId.ForestPath1,"森林 · 林间步道","森林1.png",-1,0,1210),
            new Layout(PlaceId.ForestPath2,"森林 · 林深处","森林-森林中2.png",-1,1,1145),
            new Layout(PlaceId.ForestCabin,"梨园的木屋","林间木屋 1.png",-1,2,1175),
            new Layout(PlaceId.Research,"阿里的研究所","山顶-研究所.png",-2,0,1185),
            new Layout(PlaceId.Summit,"山顶观景台","山顶观景台-1110.png",-2,1,1260),
            new Layout(PlaceId.Harbor,"码头 · 游客中心","2-1码头-游客中心-1110.png",1,0,1215),
            new Layout(PlaceId.Street,"花店 · 咖啡店 · 照相馆","2-2 花店照相馆.png",1,1,1210),
            new Layout(PlaceId.Square,"市民广场","2-3市民广场.png",1,2,1220),
            new Layout(PlaceId.EastCrossing,"公共街 · 广场东口","2-4广场东口.png",1,3,1210),
            new Layout(PlaceId.Boardwalk,"海滨步道","3-1海滨步道-1110.png",2,0,1180),
            new Layout(PlaceId.Seafront,"海滨街 · 酒吧与餐厅","3-2酒吧餐厅.png",2,1,1220),
            new Layout(PlaceId.BeachEntrance,"沙滩入口","3-3沙滩入口.png",2,3,1200),
            new Layout(PlaceId.Beach,"沙滩","海滨街-沙滩.png",2,4,1230),
            new Layout(PlaceId.CaptainHome,"船长的家","居民街-船长室内.png",3,0,916,true),
            new Layout(PlaceId.LiTingHome,"李庭的家","居民街-李庭家室内.png",3,1,895,true),
            new Layout(PlaceId.ZiqingHome,"子晴的家","居民街-子晴家室内.png",3,2,899,true),
            new Layout(PlaceId.ZHome,"Z 家客厅","室内6-Z家客厅 1.png",3,3,888,true),
            new Layout(PlaceId.ZBedroom,"Z 家卧室","室内7-Z家卧室 1.png",3,13,895,true),
            new Layout(PlaceId.ZhouHome,"周伯伯的家","居民姐-周伯伯家室内.png",3,4,903,true),
            new Layout(PlaceId.VisitorCenter,"游客中心","公共街-游客中心室内.png",3,5,910,true),
            new Layout(PlaceId.FlowerShop,"花店","公共街-花店室内.png",3,6,942,true),
            new Layout(PlaceId.Cafe,"咖啡店","公共街-咖啡店室内.png",3,7,880,true),
            new Layout(PlaceId.PhotoStudio,"照相馆","公共街-照相馆室内.png",3,8,899,true),
            new Layout(PlaceId.CabinInterior,"梨园的木屋 · 室内","室内11-木屋 1.png",3,9,1340,true),
            new Layout(PlaceId.ResearchInterior,"研究所 · 室内","山顶-研究所室内.png",3,10,1168,true),
            new Layout(PlaceId.BarInterior,"海风酒吧 · 室内","室内酒吧 1.png",3,11,955,true),
            new Layout(PlaceId.RestaurantInterior,"餐厅 · 室内","餐厅室内 1.png",3,12,930,true),
            new Layout(PlaceId.GeneralStore,"海岛杂货铺","杂货铺室内.png",3,14,714,true)
        };
        static readonly List<IslandMapLink> links=new List<IslandMapLink>();
        static Layout Find(PlaceId p)=>Maps.Single(m=>m.place==p);
        static float X(PlaceId p,float pixel)=> (pixel-Find(p).pixelWidth*.5f)/Find(p).ppu;
        static void Pair(PlaceId a,PlaceId b,float ax,float bx,string ab,string ba,bool auto=false)
        {
            string id=a+"_"+b;
            links.Add(new IslandMapLink{id=id+"_out",arrivalId=id+"_in",from=a,to=b,x=ax,label=ab,automatic=auto});
            links.Add(new IslandMapLink{id=id+"_back",arrivalId=id+"_return",from=b,to=a,x=bx,label=ba,automatic=auto});
        }
        static void Street(params PlaceId[] places)
        {
            for(int i=0;i<places.Length-1;i++)Pair(places[i],places[i+1],Find(places[i]).width/2-.55f,-Find(places[i+1]).width/2+.55f,"向右继续","向左继续",true);
        }
        static void Path(PlaceId a,PlaceId b,float ax,float bx,string ab,string ba)
        {Pair(a,b,X(a,ax),X(b,bx),ab,ba);}
        static void Door(PlaceId outdoor,PlaceId indoor,float doorPixel,float exitPixel)
        {Path(outdoor,indoor,doorPixel,exitPixel,"进入 · "+Find(indoor).label,"离开 · 返回"+Find(outdoor).label);}
        static void Routes()
        {
            links.Clear();
            Street(PlaceId.ResidentialCenter,PlaceId.Forest);
            Street(PlaceId.Harbor,PlaceId.Street,PlaceId.Square,PlaceId.EastCrossing);
            Street(PlaceId.Boardwalk,PlaceId.Seafront,PlaceId.BeachEntrance,PlaceId.Beach);
            Street(PlaceId.ForestPath1,PlaceId.ForestPath2,PlaceId.ForestCabin);
            Path(PlaceId.ResidentialCenter,PlaceId.Harbor,335,4270,"下行 · 码头与游客中心","上行 · 居民街西段");
            Path(PlaceId.ResidentialCenter,PlaceId.Street,3320,3100,"下行 · 公共街","上行 · 居民街");
            Path(PlaceId.ResidentialCenter,PlaceId.EastCrossing,6470,930,"下行 · 广场东口","上行 · 居民街东段");
            Path(PlaceId.Harbor,PlaceId.Boardwalk,4040,2870,"下行 · 海滨步道","上行 · 码头与游客中心");
            Path(PlaceId.EastCrossing,PlaceId.BeachEntrance,2310,730,"下行 · 沙滩入口","上行 · 广场东口");
            Path(PlaceId.Forest,PlaceId.ForestPath1,1175,300,"上山 · 林间步道","下山 · 居民街");
            Path(PlaceId.ForestCabin,PlaceId.Research,1580,2160,"上山 · 研究所","下山 · 梨园木屋");
            Path(PlaceId.ForestCabin,PlaceId.EastCrossing,2740,1980,"下行 · 广场东口","上山 · 梨园木屋");
            Street(PlaceId.Summit,PlaceId.Research);
            Door(PlaceId.ResidentialCenter,PlaceId.CaptainHome,1295,1940);
            Door(PlaceId.ResidentialCenter,PlaceId.LiTingHome,2605,1925);
            Door(PlaceId.ResidentialCenter,PlaceId.ZiqingHome,4040,1940);
            Door(PlaceId.ResidentialCenter,PlaceId.ZHome,5530,1930);
            Door(PlaceId.ResidentialCenter,PlaceId.ZBedroom,5740,1980);
            Door(PlaceId.ResidentialCenter,PlaceId.ZhouHome,6950,1930);
            Door(PlaceId.Harbor,PlaceId.VisitorCenter,2150,1620);
            Door(PlaceId.Boardwalk,PlaceId.GeneralStore,4010,1017);
            Door(PlaceId.Street,PlaceId.FlowerShop,2155,1940);
            Door(PlaceId.Street,PlaceId.Cafe,4120,1940);
            Door(PlaceId.Street,PlaceId.PhotoStudio,5515,1960);
            Door(PlaceId.ForestCabin,PlaceId.CabinInterior,2175,2960);
            Door(PlaceId.Research,PlaceId.ResearchInterior,1430,2470);
            Door(PlaceId.Seafront,PlaceId.BarInterior,1080,130);
            Door(PlaceId.Seafront,PlaceId.RestaurantInterior,3195,100);
            // Retain the pre-existing story location without substituting it for any supplied map.
            Pair(PlaceId.Seafront,PlaceId.Lighthouse,X(PlaceId.Seafront,3850),-13.2f,"沿海堤 · 灯塔","返回 · 海滨街");
        }
        [MenuItem("Seabreeze/Island Map/Build Current Map Assets")]
        public static void Build()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode first.");
            if(SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save the active scene first.");
            if(!AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Art+"WayfindingFont.asset"))
                AssetDatabase.CopyAsset("Assets/Seabreeze/Resources/Fonts/SeabreezeChinese.asset",Art+"WayfindingFont.asset");
            // Keep route anchors in source pixels even when an oversized texture is downsampled.
            foreach(var m in Maps)
            {
                var path=Art+m.place+".png";var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                if(importer==null)throw new FileNotFoundException(path);
                importer.GetSourceTextureWidthAndHeight(out int sourceWidth,out int sourceHeight);
                m.ppu=m.indoor?165f*sourceHeight/1152f:120f;
                importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
                importer.spritePixelsPerUnit=m.ppu;importer.mipmapEnabled=false;
                var platform=importer.GetDefaultPlatformTextureSettings();
                platform.maxTextureSize=8192;
                importer.SetPlatformTextureSettings(platform);
                importer.textureCompression=TextureImporterCompression.Uncompressed;importer.alphaIsTransparency=false;importer.npotScale=TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
                m.pixelWidth=sourceWidth;m.width=sourceWidth/m.ppu;m.height=sourceHeight/m.ppu;
            }
            Routes();ValidateRoutes();foreach(var m in Maps)BuildScene(m);LegacyLighthouse();
            var catalog=AssetDatabase.LoadAssetAtPath<IslandMapCatalog>(CatalogPath);
            if(!catalog){catalog=ScriptableObject.CreateInstance<IslandMapCatalog>();AssetDatabase.CreateAsset(catalog,CatalogPath);}
            catalog.locations=Maps.Select(m=>new IslandMapNode{place=m.place,mapId=m.place.ToString(),displayName=m.label,row=m.row,column=m.col,width=m.width,groundPixelY=m.footY,sourceFile=m.source,indoor=m.indoor}).ToArray();
            catalog.routes=links.ToArray();EditorUtility.SetDirty(catalog);
            var retired=new[]{"ResidentialWest","ResidentialEast","Garden","Bookshop","Restaurant"};
            var existing=EditorBuildSettings.scenes.Where(s=>!retired.Contains(System.IO.Path.GetFileNameWithoutExtension(s.path))).ToList();
            foreach(var m in Maps){string path=Scenes+m.place+".unity";var prior=existing.Find(s=>s.path==path);if(prior==null)existing.Add(new EditorBuildSettingsScene(path,true));else prior.enabled=true;}
            EditorBuildSettings.scenes=existing.ToArray();AssetDatabase.SaveAssets();AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(Scenes+"Street.unity");
            Debug.Log($"ISLAND_MAP_BUILD_OK: {Maps.Length} current assets, {links.Count} directed routes. Player/story/UI unchanged.");
        }
        static void ValidateRoutes()
        {
            foreach(var link in links)
            {
                if(!links.Any(r=>r.from==link.to&&r.to==link.from))throw new InvalidOperationException("Missing return route: "+link.id);
                if(link.automatic||link.from==PlaceId.Lighthouse)continue;
                var m=Find(link.from);
                bool autoOnSide=links.Any(r=>r.from==link.from&&r.automatic&&Mathf.Sign(r.x)==Mathf.Sign(link.x));
                // Boundary centre .55 + half trigger .5 + player half-width .25 + E radius .65 + safety .05.
                if(autoOnSide&&m.width/2-Mathf.Abs(link.x)<2f)throw new InvalidOperationException("E route overlaps automatic boundary: "+link.id);
            }
        }
        static void ClearGeography(WorldScene stage)
        {
            foreach(var c in stage.GetComponents<SceneAtmosphere>())UnityEngine.Object.DestroyImmediate(c);
            // Only ground geometry and routing generated by either map builder; NPC colliders are retained.
            var names=new[]{"Continuous walkable pavement","Left boundary","Right boundary","Low bench platform","Walkable foreground pavement","Left end barrier","Right end barrier"};
            foreach(var c in stage.GetComponentsInChildren<Collider2D>(true).Where(c=>names.Contains(c.name)).ToArray())UnityEngine.Object.DestroyImmediate(c.gameObject);
            foreach(var name in new[]{"Island Routes","Location placard","Island legacy arrival"}){var child=stage.transform.Find(name);if(child)UnityEngine.Object.DestroyImmediate(child.gameObject);}
            foreach(var door in stage.GetComponentsInChildren<Interactable>(true).Where(i=>i.isDoor).ToArray())UnityEngine.Object.DestroyImmediate(door.gameObject);
        }
        static void BuildScene(Layout m)
        {
            WorldScene stage;
            if(File.Exists(Scenes+m.place+".unity"))
            {EditorSceneManager.OpenScene(Scenes+m.place+".unity");stage=UnityEngine.Object.FindFirstObjectByType<WorldScene>();ClearGeography(stage);}
            else
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("Game lifecycle").AddComponent<GameBootstrap>();
                stage=new GameObject(m.place+" - current island map").AddComponent<WorldScene>();
                stage.backdrop=new GameObject("Current authored background").AddComponent<SpriteRenderer>();stage.backdrop.transform.SetParent(stage.transform,false);
            }
            stage.place=m.place;stage.width=m.width;stage.streetSpawn=0;stage.fromCafe=X(PlaceId.Street,4120);
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Art+m.place+".png");stage.floorImageFraction=1-m.footY/(m.height*m.ppu);
            stage.cameraSize=Mathf.Min(m.indoor?4f:5.25f,m.height*.48f,m.width/(2f*16f/9f));
            stage.cameraY=Mathf.Clamp(m.indoor?2.2f:3.7f,m.footY/m.ppu-m.height+stage.cameraSize,m.footY/m.ppu-stage.cameraSize);
            stage.backdrop.sprite=sprite;stage.backdrop.sortingOrder=-20;stage.backdrop.transform.localScale=new Vector3(m.width/sprite.bounds.size.x,m.height/sprite.bounds.size.y,1);
            stage.backdrop.transform.position=new Vector3(0,m.footY/m.ppu-m.height*.5f,0);
            var atmosphere=stage.GetComponent<IslandAtmosphere>()??stage.gameObject.AddComponent<IslandAtmosphere>();atmosphere.indoor=m.indoor;
            var location=stage.GetComponent<IslandLocation>()??stage.gameObject.AddComponent<IslandLocation>();
            location.mapId=m.place.ToString();location.displayName=m.label;location.row=m.row;location.column=m.col;location.groundPixelY=m.footY;location.sourceAsset=m.source;
            Ground(stage.transform,"Walkable foreground pavement",new Vector2(0,-.5f),new Vector2(m.width+2,1));
            Ground(stage.transform,"Left end barrier",new Vector2(-m.width/2-.3f,6),new Vector2(.5f,16));
            float right=m.place==PlaceId.ForestCabin?X(m.place,2970):m.width/2;
            Ground(stage.transform,"Right end barrier",new Vector2(right+.3f,6),new Vector2(.5f,16));
            var routes=new GameObject("Island Routes").transform;routes.SetParent(stage.transform,false);
            foreach(var link in links.Where(l=>l.from==m.place))AddRoute(routes,link);
            foreach(var inbound in links.Where(l=>l.to==m.place))
            {
                var reverse=links.Single(l=>l.from==m.place&&l.to==inbound.from);
                float x=reverse.automatic?Mathf.Sign(reverse.x)*(m.width/2-2.1f):reverse.x;
                // Enter a room beside its door, clear of the boundary and facing its open floor.
                if(m.indoor)x=Mathf.Clamp(x,-m.width/2+1.2f,m.width/2-1.2f);
                Arrival(routes,inbound.arrivalId,x);
            }
            // Legacy story interactions retain IDs and NPC schedules; only spatial anchors adapt to the new backgrounds.
            if(m.place==PlaceId.Cafe){Relocate(stage,"radio",-2.9f);Relocate(stage,"guestbook",1.6f);Relocate(stage,"coffee",-.7f);}
            if(m.place==PlaceId.Street){Relocate(stage,"postcard",X(m.place,4490));Relocate(stage,"bench",X(m.place,4530));Relocate(stage,"noticeboard",X(m.place,2770));}
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),Scenes+m.place+".unity");
        }
        static void Relocate(WorldScene stage,string id,float x)
        {foreach(var i in stage.GetComponentsInChildren<Interactable>(true).Where(i=>i.id==id)){i.transform.position=new Vector3(x,0,0);PrefabUtility.RecordPrefabInstancePropertyModifications(i.transform);}}
        static void Ground(Transform root,string name,Vector2 pos,Vector2 size)
        {var g=new GameObject(name);g.layer=8;g.transform.SetParent(root,false);g.transform.localPosition=pos;g.AddComponent<BoxCollider2D>().size=size;}
        static void Arrival(Transform parent,string id,float x)
        {var g=new GameObject("Arrival "+id);g.transform.SetParent(parent,false);g.transform.position=new Vector3(x,.08f,0);g.AddComponent<IslandArrival>().id=id;}
        static void AddRoute(Transform root,IslandMapLink link)
        {
            var g=new GameObject(link.id);g.transform.SetParent(root,false);g.transform.position=new Vector3(link.x,0,0);
            if(link.automatic)
            {var trigger=g.AddComponent<IslandRouteTrigger>();trigger.destination=link.to;trigger.arrivalId=link.arrivalId;var box=g.GetComponent<BoxCollider2D>();box.isTrigger=true;box.size=new Vector2(1,14);box.offset=new Vector2(0,5);}
            else
            {var i=g.AddComponent<Interactable>();i.id=link.id;i.label=link.label;i.isDoor=true;i.destination=link.to;i.arrivalId=link.arrivalId;i.radius=.65f;i.promptOffset=new Vector3(0,2.5f,0);
             if(Find(link.from).indoor)return;
             // Small world-space direction signs belong to geography, not the parallel map-panel UI.
             var sign=new GameObject("Route sign");sign.transform.SetParent(g.transform,false);sign.transform.localPosition=new Vector3(Mathf.Clamp(link.x,-Find(link.from).width/2+2.5f,Find(link.from).width/2-2.5f)-link.x,link.to==PlaceId.ZBedroom?5f:3.6f,-.1f);
             var t=sign.AddComponent<TextMeshPro>();t.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Art+"WayfindingFont.asset");t.text=link.label;t.fontSize=1.15f;t.color=new Color(.94f,.98f,1,.92f);t.alignment=TextAlignmentOptions.Center;t.rectTransform.sizeDelta=new Vector2(5,1);t.GetComponent<MeshRenderer>().sortingOrder=45;}
        }
        static void LegacyLighthouse()
        {
            EditorSceneManager.OpenScene(Scenes+"Lighthouse.unity");var stage=UnityEngine.Object.FindFirstObjectByType<WorldScene>();
            var link=links.Single(l=>l.from==PlaceId.Lighthouse);var door=stage.GetComponentsInChildren<Interactable>(true).Single(i=>i.id=="lighthouse_exit");
            door.destination=link.to;door.arrivalId=link.arrivalId;door.label=link.label;PrefabUtility.RecordPrefabInstancePropertyModifications(door);EditorUtility.SetDirty(door);
            var old=stage.transform.Find("Island legacy arrival");if(old)UnityEngine.Object.DestroyImmediate(old.gameObject);
            var root=new GameObject("Island legacy arrival").transform;root.SetParent(stage.transform,false);
            Arrival(root,links.Single(l=>l.to==PlaceId.Lighthouse).arrivalId,link.x+1.2f);EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }
    }
}
