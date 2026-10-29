using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProjectZ.Runtime
{
    public sealed class IslandScene
    {
        public PlayerMovement Player;
        public FollowCamera Camera;
        public ZPerformance Z;
        public readonly Dictionary<string,Vector3> Locations=new Dictionary<string,Vector3>();
        private Transform root;
        private CoastalArtCatalog art;
        private System.Random random=new System.Random(41);
        private float Range(float a,float b)=>a+(float)random.NextDouble()*(b-a);
        public void Create()
        {
            art=Resources.Load<CoastalArtCatalog>("Art/CoastalArtCatalog");
            if(art==null)throw new System.InvalidOperationException("请先运行 Project Z/Art/Rebuild Coastal Art。");
            root=new GameObject("Island · illustrated coast").transform;
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.50f,.64f,.82f);
            RenderSettings.ambientEquatorColor=new Color(.35f,.42f,.48f);
            RenderSettings.ambientGroundColor=new Color(.22f,.24f,.25f);
            RenderSettings.skybox=art.Sky;RenderSettings.fog=true;
            RenderSettings.fogColor=new Color(.73f,.85f,.85f);RenderSettings.fogMode=FogMode.Exponential;
            RenderSettings.fogDensity=.009f;
            var sun=new GameObject("Warm coastal sun").AddComponent<Light>();sun.transform.SetParent(root);
            sun.type=LightType.Directional;sun.intensity=1.45f;sun.color=new Color(1,.94f,.84f);
            sun.shadows=LightShadows.Soft;sun.shadowStrength=.88f;sun.shadowBias=.025f;sun.shadowNormalBias=.12f;
            sun.transform.rotation=Quaternion.Euler(45,130,0);RenderSettings.sun=sun;
            var volume=new GameObject("Coastal color and light").AddComponent<Volume>();
            volume.isGlobal=true;volume.sharedProfile=art.PostProcessing;
            Ground();
            var ocean=GameObject.CreatePrimitive(PrimitiveType.Plane);ocean.name="Tidal water";
            ocean.transform.SetParent(root);ocean.transform.position=new Vector3(0,-.22f,0);
            ocean.transform.localScale=Vector3.one*90;Object.Destroy(ocean.GetComponent<Collider>());
            ocean.GetComponent<Renderer>().sharedMaterial=art.Water;
            Locations.Add("cove",new Vector3(5.3f,GroundHeight(5.3f,-5.2f),-5.2f));
            Locations.Add("map_table",new Vector3(-5,GroundHeight(-5,3),3));
            Path(new[]{new Vector3(-5,0,5),new Vector3(-5,0,2),new Vector3(-1,0,.4f),new Vector3(2,0,-2),new Vector3(5.7f,0,-5.7f)},1.35f);
            Place(art.Bench,new Vector3(6.25f,.14f,-5.9f),-18,1);
            Place(art.Cottage,new Vector3(-5,.13f,7.7f),180,1);
            Place(art.MapTable,new Vector3(-5,.13f,4.4f),0,1);
            PlantCoast();
            ComposeCove();
            var player=new GameObject("Player");player.transform.position=new Vector3(1,.3f,-1);
            var controller=player.AddComponent<CharacterController>();controller.height=1.7f;controller.radius=.3f;
            controller.center=new Vector3(0,.88f,0);controller.minMoveDistance=0;
            Player=player.AddComponent<PlayerMovement>();CreateTraveler(player.transform);
            var actor=Object.Instantiate(art.Z);actor.transform.position=Locations["cove"]+new Vector3(0,0,.6f);
            Z=actor.GetComponent<ZPerformance>();Z.GazeTarget=Player.transform;
            Z.FaceToward(Player.transform.position);
            var cam=new GameObject("Main Camera");cam.tag="MainCamera";
            var camera=cam.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.Skybox;
            camera.fieldOfView=43;camera.nearClipPlane=.08f;camera.farClipPlane=600;
            camera.allowHDR=true;cam.AddComponent<AudioListener>();
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            Camera=cam.AddComponent<FollowCamera>();Camera.Target=Player.transform;Player.CameraTransform=cam.transform;
        }
        public void PlaceZ(string id)
        {
            if(Locations.TryGetValue(id,out var position))
            {
                Z.transform.position=position+new Vector3(0,0,.6f);
                Z.FaceToward(Player.transform.position);
            }
        }
        private GameObject Place(GameObject prefab,Vector3 position,float yaw,float scale)
        {
            // Keep the FBX root's unit/axis conversion under an independent placement transform.
            var obj=new GameObject(prefab.name);obj.transform.SetParent(root,false);
            Object.Instantiate(prefab,obj.transform,false);obj.transform.localPosition=position;
            obj.transform.localRotation=Quaternion.Euler(0,yaw,0);obj.transform.localScale=Vector3.one*scale;return obj;
        }
        private static float Coast(float a)
        {
            var da=Mathf.DeltaAngle(a*Mathf.Rad2Deg,-.76f*Mathf.Rad2Deg)*Mathf.Deg2Rad;
            return 1-.24f*Mathf.Exp(-da*da/.18f)+.025f*Mathf.Sin(a*7)+.018f*Mathf.Cos(a*11);
        }
        private static float GroundHeight(float x,float z)
        {
            var a=Mathf.Atan2(z,x);var radius=new Vector2(x/14,z/11.8f).magnitude/Coast(a);
            var shore=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.78f,1,radius));
            var hill=.95f*Mathf.Exp(-((x+8)*(x+8)+(z-1)*(z-1))/8)+
                .7f*Mathf.Exp(-((x-8)*(x-8)+(z-4)*(z-4))/12);
            return .13f+hill*(1-shore)-.44f*shore;
        }
        private void Ground()
        {
            const int segments=192,rings=48;
            var vertices=new List<Vector3>();var colors=new List<Color>();var uv=new List<Vector2>();var indices=new List<int>();
            ColorUtility.TryParseHtmlString("#7FA084",out var green);ColorUtility.TryParseHtmlString("#D6D0B8",out var sand);
            for(var r=0;r<=rings;r++)for(var i=0;i<=segments;i++)
            {
                var a=i/(float)segments*Mathf.PI*2;var t=r/(float)rings;var coast=Coast(a);
                var x=Mathf.Cos(a)*14*coast*t;var z=Mathf.Sin(a)*11.8f*coast*t;
                var shore=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.78f,1,t));
                vertices.Add(new Vector3(x,GroundHeight(x,z),z));uv.Add(new Vector2(x,z)*.1f);
                var cove=Mathf.Exp(-((x-5)*(x-5)+(z+5)*(z+5))/20);
                var beach=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.71f-cove*.19f,.9f-cove*.10f,t));
                var color=Color.Lerp(green,sand,beach)*(.90f+.2f*Mathf.PerlinNoise(x*.55f+20,z*.55f+20));
                colors.Add(color.linear);
                if(r<rings&&i<segments){var n=r*(segments+1)+i;indices.AddRange(new[]{n,n+1,n+segments+1,n+1,n+segments+2,n+segments+1});}
            }
            var mesh=new Mesh{name="Organic sand and meadow shoreline"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetColors(colors);
            mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var obj=new GameObject("Meadow and beach");obj.transform.SetParent(root);
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=art.Ground;
            obj.AddComponent<MeshCollider>().sharedMesh=mesh;
        }
        private void Path(Vector3[] anchors,float width)
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            Vector3 Sample(float s)
            {
                s=Mathf.Clamp(s,0,anchors.Length-1.001f);var k=Mathf.FloorToInt(s);var t=s-k;
                var a=anchors[Mathf.Max(0,k-1)];var b=anchors[k];var c=anchors[k+1];var d=anchors[Mathf.Min(anchors.Length-1,k+2)];
                return .5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);
            }
            const int steps=100;
            for(var i=0;i<=steps;i++)
            {
                var t=i/(float)steps*(anchors.Length-1);var p=Sample(t);
                var direction=(Sample(t+.02f)-Sample(t-.02f)).normalized;var right=new Vector3(direction.z,0,-direction.x);
                var w=width*(.5f+.02f*Mathf.Sin(t*15));p.y=GroundHeight(p.x,p.z)+.015f;
                var n=vertices.Count;vertices.Add(p-right*w);vertices.Add(p+right*w);uv.Add(new Vector2(0,t));uv.Add(new Vector2(1,t));
                if(i<steps)triangles.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3});
            }
            var mesh=new Mesh{name="Footpath"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();
            var obj=new GameObject("Worn coastal footpath");obj.transform.SetParent(root);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial=art.Path;
        }
        private void PlantCoast()
        {
            var positions=new[]{new Vector3(-9,0,4),new Vector3(-10,0,-.5f),new Vector3(-8,0,-5),new Vector3(-4,0,-7),
                new Vector3(0,0,8),new Vector3(4,0,6.5f),new Vector3(8,0,4),new Vector3(10,0,.4f),new Vector3(-9,0,7)};
            for(var i=0;i<positions.Length;i++)
            {
                var tree=Place(art.Trees[i%art.Trees.Length],positions[i]+Vector3.up*GroundHeight(positions[i].x,positions[i].z),Range(0,360),Range(.54f,.72f));
                var collider=tree.AddComponent<CapsuleCollider>();collider.center=Vector3.up*2;collider.height=4;collider.radius=.25f;
            }
            for(var i=0;i<82;i++)
            {
                var a=Range(-Mathf.PI,Mathf.PI);var radius=Range(.7f,.90f);var coast=Coast(a);
                var p=new Vector3(Mathf.Cos(a)*14*coast*radius,.10f,Mathf.Sin(a)*11.8f*coast*radius);
                p.y=GroundHeight(p.x,p.z);
                if(p.x>3&&p.z<-2)continue;
                if(p.x>-7.5f&&p.x<-2.5f&&p.z>4)continue;
                var set=i%4==0?art.Bushes:i%3==0?art.Flowers:art.Grasses;
                var scale=set==art.Flowers?Range(.13f,.23f):set==art.Grasses?Range(.23f,.42f):Range(.26f,.43f);
                if(set==art.Bushes&&i%set.Length==2)scale*=.25f;
                Place(set[i%set.Length],p,Range(0,360),scale);
            }
            for(var i=0;i<37;i++)
            {
                var a=Range(-Mathf.PI,Mathf.PI);var c=Coast(a);var r=Range(.92f,1.02f);
                var cove=a>-.95f&&a<-.45f;
                Place(art.Rocks[i%art.Rocks.Length],new Vector3(Mathf.Cos(a)*14*c*r,-.18f,Mathf.Sin(a)*11.8f*c*r),Range(0,360),cove?Range(.14f,.26f):Range(.28f,.58f));
            }
            foreach(var center in new[]{new Vector3(7.7f,0,-3.3f),new Vector3(3,0,-6),new Vector3(-6.7f,0,2.8f),new Vector3(-2.8f,0,5.4f)})
            for(var i=0;i<9;i++)
            {
                var p=center+new Vector3(Range(-.65f,.65f),.13f,Range(-.5f,.5f));
                p.y=GroundHeight(p.x,p.z);
                var set=i%3==0?art.Grasses:art.Flowers;Place(set[i%set.Length],p,Range(0,360),set==art.Flowers?Range(.14f,.22f):Range(.25f,.37f));
            }
            foreach(var center in new[]{new Vector3(-3,0,-3),new Vector3(3,0,2),new Vector3(6,0,1),new Vector3(-7,0,.5f),new Vector3(0,0,4)})
            for(var i=0;i<14;i++)
            {
                var p=center+new Vector3(Range(-1.5f,1.5f),.13f,Range(-1,1));
                p.y=GroundHeight(p.x,p.z);
                Place(art.Grasses[i%2],p,Range(0,360),Range(.12f,.23f));
            }
        }
        private void ComposeCove()
        {
            Place(art.Trees[0],new Vector3(8.4f,GroundHeight(8.4f,-1.8f),-1.8f),145,.63f);
            foreach(var center in new[]{new Vector3(8.2f,0,-2.7f),new Vector3(3.4f,0,-6.1f),new Vector3(8.7f,0,-5.2f)})
            {
                for(var i=0;i<22;i++)
                {
                    var angle=Range(0,Mathf.PI*2);var r=Range(.25f,1.3f);
                    var p=center+new Vector3(Mathf.Cos(angle)*r,0,Mathf.Sin(angle)*r);
                    p.y=GroundHeight(p.x,p.z);
                    var set=i%6==0?art.Bushes:i%5==0?art.Flowers:art.Grasses;
                    var index=i%set.Length;var scale=set==art.Bushes?.32f:set==art.Flowers?.18f:.33f;
                    if(set==art.Bushes&&index==2)scale=.09f;
                    Place(set[index],p,Range(0,360),scale*Range(.8f,1.2f));
                }
            }
            for(var i=0;i<32;i++)
            {
                var angle=Range(0,Mathf.PI*2);var r=Range(1.15f,2.5f);
                var p=new Vector3(6+Mathf.Cos(angle)*r,0,-5.4f+Mathf.Sin(angle)*r);
                p.y=GroundHeight(p.x,p.z)-.012f;
                Place(art.Rocks[3+i%2],p,Range(0,360),Range(.24f,.65f));
            }
            DistantIsland(new Vector3(64,-.30f,-28),new Vector3(18,2.6f,12));
            DistantIsland(new Vector3(85,-.30f,-58),new Vector3(24,4,17));
            DistantIsland(new Vector3(10,-.30f,-75),new Vector3(16,2.3f,9));
        }
        private void DistantIsland(Vector3 center,Vector3 size)
        {
            const int sectors=64,rings=16;
            var v=new List<Vector3>();var triangles=new List<int>();
            for(var j=0;j<=rings;j++)for(var i=0;i<=sectors;i++)
            {
                var r=j/(float)rings;var a=i/(float)sectors*Mathf.PI*2;
                var edge=1+.11f*Mathf.Sin(a*3)+.05f*Mathf.Cos(a*7);
                var h=Mathf.Pow(1-r*r,2.2f)*(1+.18f*Mathf.Sin(Mathf.Cos(a)*r*4)+.12f*Mathf.Cos(Mathf.Sin(a)*r*6));
                v.Add(new Vector3(Mathf.Cos(a)*r*size.x*edge,h*size.y,Mathf.Sin(a)*r*size.z*edge));
                if(j<rings&&i<sectors){var n=j*(sectors+1)+i;triangles.AddRange(new[]{n,n+1,n+sectors+1,n+1,n+sectors+2,n+sectors+1});}
            }
            var mesh=new Mesh{name="Distant coastal ridge"};mesh.SetVertices(v);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();
            var obj=new GameObject("Distant island");obj.transform.SetParent(root);obj.transform.position=center;
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            var material=new Material(art.Path);material.color=new Color(.40f,.50f,.56f);
            material.SetFloat("_UnlitBlend",.65f);material.SetFloat("_Specular",0);
            obj.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
        private void CreateTraveler(Transform parent)
        {
            Material Mat(string hex){ColorUtility.TryParseHtmlString(hex,out var c);var m=new Material(art.Path);m.color=c;return m;}
            var coat=Mat("#647F83");var skin=Mat("#EBC7AC");var hat=Mat("#D5C6A3");var brown=Mat("#665546");
            void Part(string name,PrimitiveType type,Vector3 p,Vector3 size,Material m)
            {var o=GameObject.CreatePrimitive(type);o.name=name;o.transform.SetParent(parent,false);o.transform.localPosition=p;o.transform.localScale=size;Object.Destroy(o.GetComponent<Collider>());o.GetComponent<Renderer>().sharedMaterial=m;}
            Part("Coat",PrimitiveType.Capsule,new Vector3(0,.93f,0),new Vector3(.54f,.49f,.37f),coat);
            Part("Head",PrimitiveType.Sphere,new Vector3(0,1.52f,0),Vector3.one*.36f,skin);
            Part("Straw hat brim",PrimitiveType.Cylinder,new Vector3(0,1.69f,0),new Vector3(.61f,.022f,.55f),hat);
            Part("Straw hat crown",PrimitiveType.Cylinder,new Vector3(0,1.78f,0),new Vector3(.37f,.08f,.34f),hat);
            Part("Satchel",PrimitiveType.Cube,new Vector3(0,.98f,-.23f),new Vector3(.37f,.39f,.17f),brown);
            foreach(var x in new[]{-.14f,.14f})
            {Part("Trouser",PrimitiveType.Capsule,new Vector3(x,.39f,0),new Vector3(.20f,.28f,.2f),brown);
             Part("Boot",PrimitiveType.Capsule,new Vector3(x,.13f,.05f),new Vector3(.20f,.10f,.33f),brown);}
        }
    }
}
