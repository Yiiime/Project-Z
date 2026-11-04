using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace ZLive {
public sealed class DrawingPresenter:VisualElement {
    JObject scene; int stage; public string ArtifactId{get;private set;} public int RenderedVersion{get;private set;} int version;
    public DrawingPresenter(){style.height=150;style.minHeight=150;style.flexGrow=1;style.flexShrink=1;style.backgroundColor=new Color(1,.99f,.96f);generateVisualContent+=Draw;}
    public void Present(JObject value,int nextStage,string id){Validate(value,nextStage);scene=(JObject)value.DeepClone();stage=nextStage;ArtifactId=id;version++;MarkDirtyRepaint();}
    static void Fail(){throw new Exception("invalid_drawing");}
    public static void Validate(JObject value,int stage) {
        var stages=value["stages"] as JArray;var elements=value["elements"] as JArray;
        if(stages==null||stages.Count<1||stages.Count>3||stage<0||stage>=stages.Count||elements==null||elements.Count<1||elements.Count>64||value.ToString().Length>30000)Fail();
        var ids=new HashSet<string>();foreach(var e in elements){var id=(string)e["id"];if(id==null||!System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-zA-Z][a-zA-Z0-9_-]{0,47}$")||!ids.Add(id))Fail();
            var s=(int?)e["stage"];var w=(float?)e["strokeWidth"];if(s==null||s<0||s>=stages.Count||w==null||w<1||w>8||float.IsNaN(w.Value))Fail();
            foreach(string name in new[]{"stroke","fill"}){var color=(string)e[name];if(color!="none"&&(color==null||!System.Text.RegularExpressions.Regex.IsMatch(color,"^#[0-9a-fA-F]{6}$")))Fail();}
            if((string)e["stroke"]=="none"&&(string)e["fill"]=="none")Fail();var commands=e["commands"] as JArray;if(commands==null||commands.Count<1||commands.Count>24||(string)commands[0][0]!="M")Fail();bool visible=false;
            foreach(JArray c in commands){string op=(string)c[0];int len=op=="M"||op=="L"?3:op=="Q"?5:op=="C"?7:op=="Z"?1:0;if(c.Count!=len||len==0)Fail();if(op=="L"||op=="Q"||op=="C")visible=true;for(int i=1;i<c.Count;i++){if(c[i].Type!=JTokenType.Integer&&c[i].Type!=JTokenType.Float)Fail();float v=(float)c[i];if(float.IsNaN(v)||float.IsInfinity(v)||v<0||v>(i%2==1?480:360))Fail();}}if(!visible)Fail();
        }
    }
    void Draw(MeshGenerationContext ctx){if(scene==null||contentRect.width<=0||contentRect.height<=0)return;var p=ctx.painter2D;float scale=Mathf.Min(contentRect.width/480,contentRect.height/360);var offset=new Vector2((contentRect.width-480*scale)/2,(contentRect.height-360*scale)/2);
        foreach(JObject e in (JArray)scene["elements"]){if((int)e["stage"]>stage)continue;p.BeginPath();foreach(JArray c in (JArray)e["commands"]){Vector2 P(int i)=>offset+new Vector2((float)c[i],(float)c[i+1])*scale;switch((string)c[0]){case "M":p.MoveTo(P(1));break;case "L":p.LineTo(P(1));break;case "Q":p.QuadraticCurveTo(P(1),P(3));break;case "C":p.BezierCurveTo(P(1),P(3),P(5));break;case "Z":p.ClosePath();break;}}
            if((string)e["fill"]!="none"){ColorUtility.TryParseHtmlString((string)e["fill"],out var fill);p.fillColor=fill;p.Fill(FillRule.NonZero);}if((string)e["stroke"]!="none"){ColorUtility.TryParseHtmlString((string)e["stroke"],out var stroke);p.strokeColor=stroke;p.lineWidth=(float)e["strokeWidth"]*scale;p.lineCap=LineCap.Round;p.lineJoin=LineJoin.Round;p.Stroke();}
        }RenderedVersion=version;
    }
    public bool HasRendered=>scene!=null&&RenderedVersion==version;
}
}
