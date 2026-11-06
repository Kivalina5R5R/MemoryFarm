using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    // Pooled geometry only: no textures, random state changes, or per-frame GameObjects.
    public sealed class DeepspaceImpactGraphic : MaskableGraphic
    {
        public const float Duration = .62f;
        public bool HydraFangs {get;set;}
        public bool SwordCross {get;set;}
        public bool ElectricBurst {get;set;}
        public bool VoidCollapse {get;set;}
        public bool GoldenCore {get;set;}
        public bool OceanSurge {get;set;}
        public bool UseCustomTint {get;set;}
        // บาส 2026-10-02: Deepspace stage 2-5 boss impact frames (DeepspaceBossImpactFrame.ApplyBossStyle).
        public bool TwinCross {get;set;}
        public bool HiveBurst {get;set;}
        public bool HeavyQuake {get;set;}
        public Color CustomTint {get;set;} = Color.white;
        float age;Vector2 focus;bool laser,major;
        public float FrameAge=>age;
        public void SetFrame(float elapsed,Vector2 origin,bool beam,bool full)
        {age=elapsed;focus=origin;laser=beam;major=full;SetVerticesDirty();}
        static Color Alpha(Color c,float a){c.a=Mathf.Clamp01(a);return c;}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;
            var center=new Vector2(Mathf.Lerp(r.xMin,r.xMax,focus.x),Mathf.Lerp(r.yMin,r.yMax,focus.y));
            float size=r.size.magnitude,t=Mathf.Clamp01(age/Duration),fade=Mathf.Pow(1-t,2),strength=major?1:.4f;
            Color hot=(OceanSurge||HydraFangs)?new Color(.015f,.85f,.85f):GoldenCore?new Color(1,.65f,.02f):VoidCollapse?new Color(.5f,.06f,.95f):ElectricBurst?new Color(.08f,.45f,1):laser?new Color(1,.13f,.21f):new Color(1,.51f,.08f);
            Color pale=(OceanSurge||HydraFangs)?new Color(.8f,1,1):GoldenCore?new Color(1,1,.72f):VoidCollapse?new Color(.83f,.65f,1):ElectricBurst?new Color(.7f,1,1):laser?new Color(1,.87f,.91f):new Color(1,.95f,.74f);
            if (UseCustomTint) { hot = CustomTint; pale = Color.Lerp(CustomTint, Color.white, .8f); }
            // One short flash, a dark contrast beat, then only the expanding edge treatment.
            if(major&&age<.045f)Quad(vh,new Vector2(r.xMin,r.yMin),new Vector2(r.xMax,r.yMin),new Vector2(r.xMax,r.yMax),new Vector2(r.xMin,r.yMax),Alpha(pale,.62f*(1-age/.045f)));
            float ink=major?(.52f*(1-Mathf.SmoothStep(0,1,(age-.045f)/.16f))):0;
            if(age>=.045f&&ink>0)Quad(vh,new Vector2(r.xMin,r.yMin),new Vector2(r.xMax,r.yMin),new Vector2(r.xMax,r.yMax),new Vector2(r.xMin,r.yMax),new Color(.004f,.009f,.025f,ink));
            float radius=size*(.045f+.72f*Mathf.Pow(t,.68f));
            Ring(vh,center,radius,size*.006f*(1-t),Alpha(hot,fade*.65f*strength));
            Ring(vh,center,radius*.93f,size*.0018f,Alpha(pale,fade*.9f*strength));
            // Uneven long wedges create a burst without hiding the centre of the attack.
            for(int i=0;i<52;i++)
            {
                float seed=Mathf.Repeat(i*.618034f,1),angle=(i+seed*.35f)*Mathf.PI*2/52;
                Vector2 dir=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)),side=new Vector2(-dir.y,dir.x);
                float inner=size*(.075f+t*.32f+seed*.11f),outer=inner+size*(.12f+seed*.28f)*(1-t);
                float width=size*(.001f+seed*.0028f)*(1-t);
                Tri(vh,center+dir*inner,center+dir*outer-side*width,center+dir*outer+side*width,Alpha(i%3==0?pale:hot,fade*(.28f+seed*.6f)*strength));
            }
            if(laser)
            {
                float w=r.width*(.004f+.016f*Mathf.Exp(-age*17));
                Quad(vh,new Vector2(center.x-w,r.yMin),new Vector2(center.x+w,r.yMin),new Vector2(center.x+w,r.yMax),new Vector2(center.x-w,r.yMax),Alpha(pale,fade*.7f*strength));
                float h=r.height*.007f*(1-t);
                Quad(vh,new Vector2(r.xMin,center.y-h),new Vector2(r.xMax,center.y-h),new Vector2(r.xMax,center.y+h),new Vector2(r.xMin,center.y+h),Alpha(hot,fade*.55f*strength));
            }
            if(ElectricBurst && major)
            {
                for(int ray=0;ray<16;ray++)
                {
                    float angle=ray*Mathf.PI*2/16;var dir=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));var side=new Vector2(-dir.y,dir.x);
                    Vector2 previous=center+dir*size*.025f;
                    for(int segment=1;segment<=7;segment++)
                    {
                        var next=center+dir*size*(.025f+segment*.042f+t*.15f)+side*Mathf.Sin(segment*7+ray*3)*size*.012f;
                        var normal=new Vector2(-(next-previous).y,(next-previous).x).normalized*size*.0015f*fade;
                        Quad(vh,previous-normal,next-normal,next+normal,previous+normal,Alpha(pale,fade));previous=next;
                    }
                }
                Ring(vh,center,radius*1.18f,size*.004f,Alpha(hot,fade));
            }

            if(VoidCollapse && major)
            {
                float inward=size*(.42f*(1-t)+.025f);
                Ring(vh,center,inward,size*.007f*fade,Alpha(new Color(.52f,.12f,1),fade*.9f));
                Ring(vh,center,inward*.78f,size*.002f,Alpha(pale,fade));
                for(int ray=0;ray<24;ray++)
                {
                    float a=ray*Mathf.PI*2/24;var dir=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var side=new Vector2(-dir.y,dir.x);
                    float jag=Mathf.Sin(ray*4.71f)*size*.025f;
                    var tip=center+dir*(size*.045f+t*.06f);
                    var outer=center+dir*(inward+size*.12f)+side*jag;
                    Tri(vh,tip,outer-side*size*.008f*fade,outer+side*size*.008f*fade,Alpha(hot,fade*.8f));
                }
                // A brief dark void with two pale eye slits makes this boss's impact recognizable.
                for(int i=0;i<32;i++)
                {
                    float a=i*Mathf.PI*2/32,b=(i+1)*Mathf.PI*2/32,voidRadius=size*.07f*(1-t);
                    Tri(vh,center,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*voidRadius,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*voidRadius,new Color(.018f,.002f,.04f,fade*.8f));
                }
                for(int side=-1;side<=1;side+=2)
                {
                    var eye=center+new Vector2(side*size*.025f,size*.009f);
                    Tri(vh,eye+new Vector2(-size*.015f,side*size*.003f),eye+new Vector2(size*.015f,-side*size*.003f),eye+Vector2.down*size*.006f,Alpha(pale,fade));
                }
            }

            if(OceanSurge && major)
            {
                for(int wave=0;wave<3;wave++)for(int i=0;i<48;i++){
                    float x0=Mathf.Lerp(r.xMin,r.xMax,i/48f),x1=Mathf.Lerp(r.xMin,r.xMax,(i+1)/48f);
                    float y0=center.y+Mathf.Sin(i/48f*9+wave*.8f)*r.height*.10f+(t-.2f)*r.height*(wave+1)*.35f;
                    float y1=center.y+Mathf.Sin((i+1)/48f*9+wave*.8f)*r.height*.10f+(t-.2f)*r.height*(wave+1)*.35f;
                    float w=r.height*.015f*fade;
                    Quad(vh,new Vector2(x0,y0-w),new Vector2(x1,y1-w),new Vector2(x1,y1+w),new Vector2(x0,y0+w),Alpha(wave==0?pale:hot,fade*.6f));
                }
            }
            if(HydraFangs && major)
            {
                // Seven pairs of descending fangs, synchronized with the floor bites.
                float biteFade=Mathf.Clamp01(1-age/.24f);
                for(int head=0;head<7;head++)for(int fang=-1;fang<=1;fang+=2)
                {
                    float x=Mathf.Lerp(r.xMin,r.xMax,(head+.5f)/7)+fang*r.width*.013f;
                    var tip=new Vector2(x,r.yMin+r.height*(.12f-age*.7f));
                    var top=new Vector2(x-fang*r.width*.008f,r.yMax);
                    Tri(vh,tip,top-Vector2.right*r.width*.008f,top+Vector2.right*r.width*.008f,Alpha(hot,biteFade*.7f));
                    Tri(vh,tip,top-Vector2.right*r.width*.002f,top+Vector2.right*r.width*.002f,Alpha(pale,biteFade));
                }
            }
            if(SwordCross && major)
            {
                for(int sign=-1;sign<=1;sign+=2)
                {
                    var dir=new Vector2(.94f,sign*.34f).normalized;var side=new Vector2(-dir.y,dir.x);
                    float length=r.width*(.3f+t*.3f),width=r.height*.045f*fade;
                    Quad(vh,center-dir*length,center-side*width,center+dir*length,center+side*width,Alpha(new Color(1,.035f,.06f),fade));
                    Quad(vh,center-dir*length,center-side*width*.24f,center+dir*length,center+side*width*.24f,Alpha(Color.white,fade));
                }
            }
            if(TwinCross && major)
            {
                // TWIN CARRIER: two hull beams crossing in an X, thicker and orange.
                for(int sign=-1;sign<=1;sign+=2)
                {
                    var dir=new Vector2(.72f,sign*.69f).normalized;var side=new Vector2(-dir.y,dir.x);
                    float length=size*(.22f+t*.35f),width=size*.022f*fade;
                    Quad(vh,center-dir*length,center-side*width,center+dir*length,center+side*width,Alpha(hot,fade*.9f));
                    Quad(vh,center-dir*length,center-side*width*.3f,center+dir*length,center+side*width*.3f,Alpha(Color.white,fade));
                }
            }
            if(HiveBurst && major)
            {
                // HIVE QUEEN: honeycomb hexagons blooming outward.
                for(int ring=0;ring<3;ring++)
                {
                    float hexRadius=size*(.06f+ring*.07f+t*.22f),w=size*.005f*fade;
                    for(int k=0;k<6;k++)
                    {
                        float a=(k*60+30)*Mathf.Deg2Rad,b=((k+1)*60+30)*Mathf.Deg2Rad;
                        var u=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var v=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                        Quad(vh,center+u*(hexRadius-w),center+u*hexRadius,center+v*hexRadius,center+v*(hexRadius-w),Alpha(ring==1?pale:hot,fade*.85f));
                    }
                }
                for(int cell=0;cell<12;cell++)
                {
                    float a=cell*Mathf.PI*2/12+.26f,dist=size*(.16f+t*.3f+Mathf.Repeat(cell*.37f,1)*.08f),cellRadius=size*.018f*fade;
                    var c=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*dist;
                    for(int k=0;k<6;k++){float p=k*Mathf.PI/3,q=(k+1)*Mathf.PI/3;Tri(vh,c,c+new Vector2(Mathf.Cos(p),Mathf.Sin(p))*cellRadius,c+new Vector2(Mathf.Cos(q),Mathf.Sin(q))*cellRadius,Alpha(hot,fade*.7f));}
                }
            }
            if(HeavyQuake && major)
            {
                // DREADNOUGHT: heavy shock rings and horizontal crack bars across the whole frame.
                Ring(vh,center,radius*.7f,size*.016f*fade,Alpha(hot,fade*.75f));
                Ring(vh,center,radius*.45f,size*.01f*fade,Alpha(pale,fade*.8f));
                for(int bar=-2;bar<=2;bar++)
                {
                    if(bar==0)continue;
                    float y=center.y+bar*r.height*(.09f+t*.08f),h=r.height*.008f*fade,jag=Mathf.Sin(bar*3.1f)*r.width*.05f;
                    Quad(vh,new Vector2(r.xMin,y-h),new Vector2(r.xMax,y-h+jag*.1f),new Vector2(r.xMax,y+h+jag*.1f),new Vector2(r.xMin,y+h),Alpha(hot,fade*.3f));
                }
            }
            // Thin luminous borders tie the two displays together without moving the UI/cameras.
            float border=Mathf.Min(r.width,r.height)*.012f*(1-t);
            Quad(vh,new Vector2(r.xMin,r.yMin),new Vector2(r.xMax,r.yMin),new Vector2(r.xMax,r.yMin+border),new Vector2(r.xMin,r.yMin+border),Alpha(hot,fade*.65f*strength));
            Quad(vh,new Vector2(r.xMin,r.yMax-border),new Vector2(r.xMax,r.yMax-border),new Vector2(r.xMax,r.yMax),new Vector2(r.xMin,r.yMax),Alpha(hot,fade*.65f*strength));
        }
        static void Tri(VertexHelper v,Vector2 a,Vector2 b,Vector2 c,Color color)
        {int n=v.currentVertCount;v.AddVert(a,color,Vector2.zero);v.AddVert(b,color,Vector2.zero);v.AddVert(c,color,Vector2.zero);v.AddTriangle(n,n+1,n+2);}
        static void Quad(VertexHelper v,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color color)
        {int n=v.currentVertCount;v.AddVert(a,color,Vector2.zero);v.AddVert(b,color,Vector2.zero);v.AddVert(c,color,Vector2.zero);v.AddVert(d,color,Vector2.zero);v.AddTriangle(n,n+1,n+2);v.AddTriangle(n,n+2,n+3);}
        static void Ring(VertexHelper v,Vector2 center,float radius,float width,Color color)
        {
            for(int i=0;i<72;i++)
            {
                float a=i*Mathf.PI*2/72,b=(i+1)*Mathf.PI*2/72;var u=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var w=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                Quad(v,center+u*(radius-width),center+u*radius,center+w*radius,center+w*(radius-width),color);
            }
        }
    }
}
