using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.IL2CPP;
using BepInEx.Logging;
using BepInEx.Configuration;
using UnityEngine;
using UnhollowerBaseLib;
using Sannabi._scripts.Jaeyun.Core.Game_State;
using Object = UnityEngine.Object;

namespace CharacterAbilities;

#if HERO_DLC
[BepInPlugin("kuro.sanabi.heroabilities", "SANABI · Gancho del héroe en DLC", "0.1.0")]
#else
[BepInPlugin("kuro.sanabi.majorabilities", "SANABI · Combate de Major Son", "0.1.0")]
#endif
[BepInProcess("SNB.exe")]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource Logger=null!;
    internal static ConfigEntry<float> HookRange=null!, HookSpeed=null!;
    internal static ConfigEntry<int> Damage=null!;
    public override void Load()
    {
        Logger=Log;
        Damage=Config.Bind("Combat","Damage",1,new ConfigDescription("Daño base del conjunto de combate.",new AcceptableValueRange<int>(1,10)));
        HookRange=Config.Bind("Hook","Range",16f,new ConfigDescription("Alcance máximo del gancho.",new AcceptableValueRange<float>(3,30)));
        HookSpeed=Config.Bind("Hook","Speed",18f,new ConfigDescription("Velocidad de atracción.",new AcceptableValueRange<float>(5,30)));
        AddComponent<AbilityBehaviour>();
        Log.LogInfo("Adaptación experimental de habilidades; las skins se instalan por separado.");
    }
}

public sealed class AbilityBehaviour : MonoBehaviour
{
    public AbilityBehaviour(IntPtr pointer):base(pointer) { }
    readonly AbilityLogic logic=new();
    PlayerBase? actor;
    GameStateManager? gameState;
    float dashUntil, effectUntil, failedUntil;
    Vector2 dashDirection, hookTip, hookDirection, anchorLocal;
    Collider2D? anchor;
    bool hookFlying, attached;
    float travelled;
    bool wasGrounded;
    GameObject? visual;
    LineRenderer? line;
    Material? material;

    public void Update()
    {
        if(Time.unscaledTime<failedUntil)return;
        try
        {
#if HERO_DLC
            var current=PlayerBase.Instance?.TryCast<DlcPlayer>();
#else
            var current=PlayerBase.Instance?.TryCast<Player>();
#endif
            if(current==null || actor==null || current.Pointer!=actor.Pointer)
            { Cleanup();actor=current;logic.Reset();gameState=Object.FindObjectOfType<GameStateManager>();if(actor==null)return;wasGrounded=actor.OnGround; }
            if(!CanAct()) { CancelHook();dashUntil=0;HideLine();return; }
            logic.Tick(Time.deltaTime,actor.OnGround,true);
#if HERO_DLC
            UpdateHook();
#else
            UpdateCombat();
#endif
            wasGrounded=actor.OnGround;
            if(Time.time>effectUntil && !attached && !hookFlying)HideLine();
        }
        catch(Exception e) { Cleanup();actor=null;failedUntil=Time.unscaledTime+5;Plugin.Logger.LogWarning("Habilidades suspendidas; controlador original conservado. "+e.Message); }
    }
    bool CanAct() => actor!=null && Time.timeScale>0 && !actor.IsDead && !actor.IsIgnoreAllInput && (gameState==null || gameState.IsGamePlay)
        && (actor.TryCast<Player>() is not { } hero || !hero.IsControlClone);

    Vector2 Origin => actor!.bodyRenderer!=null ? (Vector2)actor.bodyRenderer.transform.position : actor.Position;
    Vector2 Aim()
    {
        var input=PlayerInputManager.Instance;
        if(input!=null) { var dir=input.MouseDirection();if(dir.sqrMagnitude>.01f)return dir.normalized; }
        return new Vector2(actor!.IsFacingLeft?-1:1,0);
    }

    void UpdateCombat()
    {
        // Story sequences can lock all movement; never bypass that lock.
        if(actor!.TryCast<Player>() is not { } hero || !hero.JumpActive)return;
        if(Input.GetKeyDown(KeyCode.J))
        {
            var combo=logic.Melee(Time.time);
            if(combo>0)
            {
                var center=Origin+new Vector2(actor.IsFacingLeft?-.65f:.65f,0);
                HitArea(center,combo==3?1.2f:.9f,AttackType.PlayerMeleeAttack,Plugin.Damage.Value+(combo==3?1:0));
                DrawLine(Origin,center+new Vector2(0,.3f),new Color(.5f,.9f,1),.09f);
            }
        }
        if(Input.GetKeyDown(KeyCode.K) && logic.Shot(Time.time))
        {
            var start=Origin;var direction=Aim();var hits=Physics2D.RaycastAll(start,direction,14);
            var sorted=new List<RaycastHit2D>();foreach(var hit in hits)sorted.Add(hit); sorted.Sort((a,b)=>a.distance.CompareTo(b.distance));
            var end=start+direction*14;
            foreach(var hit in sorted)
            {
                var col=hit.collider;if(col==null || col.transform.root==actor.transform.root)continue;
                var enemy=col.GetComponentInParent<EnemyHitBox>();
                if(enemy!=null) { end=hit.point; DamageEnemy(enemy,AttackType.PlayerBullet,hit.point,Plugin.Damage.Value);break; }
                if(!col.isTrigger){end=hit.point;break;}
            }
            DrawLine(start,end,new Color(1,.35f,.45f),.045f);
        }
        if(Input.GetKeyDown(KeyCode.L) && logic.Dash(Time.time)) { hero.ResetHook(); dashDirection=Aim();dashUntil=Time.time+.16f; }
        if(Input.GetKeyDown(KeyCode.R) && logic.Burst(Time.time))
        {
            HitArea(Origin,2,AttackType.PlayerBurstAttack,Plugin.Damage.Value+2);
            DrawLine(Origin-new Vector2(2,0),Origin+new Vector2(2,0),new Color(.7f,.4f,1),.18f);
        }
        if(Input.GetKeyDown(KeyCode.Space) && !actor.OnGround && !wasGrounded && logic.AirJump(false))
        {
            var velocity=actor._velocity;
            velocity.y=2*actor.maxJumpHeight/Math.Max(.1f,actor.timeToJumpApex);
            actor._velocity=velocity;
        }
    }

    void HitArea(Vector2 center,float radius,AttackType type,int damage)
    {
        var seen=new HashSet<IntPtr>();
        foreach(var col in Physics2D.OverlapCircleAll(center,radius))
        {
            var hit=col.GetComponentInParent<EnemyHitBox>();if(hit==null || hit.enemy==null || !seen.Add(hit.enemy.Pointer))continue;
            DamageEnemy(hit,type,center,damage);
        }
    }
    static void DamageEnemy(EnemyHitBox hit,AttackType type,Vector2 point,int damage)
    {
        if(hit.enemy==null || hit.enemy.IsDead || hit.IsInvincible() || type==AttackType.PlayerBullet && hit.IsProjectileInvincible())return;
        hit.TakeDamage(type,point,damage,out var _);
    }

    void UpdateHook()
    {
        if(Input.GetKeyDown(KeyCode.E))
        { CancelHook();hookTip=Origin;hookDirection=Aim();travelled=0;hookFlying=true; }
        if(!Input.GetKey(KeyCode.E)) { CancelHook();return; }
        if(hookFlying)
        {
            var step=Math.Min(45*Time.deltaTime,Plugin.HookRange.Value-travelled);
            var hits=Physics2D.RaycastAll(hookTip,hookDirection,step);
            RaycastHit2D? closest=null;
            foreach(var hit in hits)
                if(hit.collider!=null && !hit.collider.isTrigger && hit.collider.transform.root!=actor!.transform.root
                    && (closest==null || hit.distance<closest.Value.distance))closest=hit;
            if(closest is { } found)
            { anchor=found.collider;anchorLocal=anchor.transform.InverseTransformPoint(found.point);attached=true;hookFlying=false; }
            else { hookTip+=hookDirection*step;travelled+=step; if(travelled>=Plugin.HookRange.Value)CancelHook(); }
        }
        if(attached)
        {
            if(anchor==null) { CancelHook();return; }
            hookTip=anchor.transform.TransformPoint(anchorLocal);
            if((hookTip-Origin).magnitude>Plugin.HookRange.Value+1) { CancelHook();return; }
            if(Input.GetKeyDown(KeyCode.Space))
            { var direction=(hookTip-Origin).normalized;actor!._velocity=direction*Plugin.HookSpeed.Value+Vector2.up*5;CancelHook();return; }
        }
        if(attached || hookFlying)DrawLine(Origin,hookTip,new Color(.35f,.9f,1),.035f);
    }

    public void LateUpdate()
    {
        if(!CanAct())return;
        try
        {
            if(Time.time<dashUntil)
            { actor!._velocity=dashDirection*20; actor.controller.Move(dashDirection*Math.Min(.1f,Time.deltaTime)*20); }
            if(attached && anchor!=null)
            {
                var delta=hookTip-Origin;var distance=delta.magnitude;
                if(distance<.75f) { CancelHook();return; }
                var move=delta.normalized*Math.Min(distance-.65f,Plugin.HookSpeed.Value*Math.Min(.05f,Time.deltaTime));
                actor!._velocity=delta.normalized*Plugin.HookSpeed.Value;
                actor.controller.Move(move); // Uses the game's collision controller, never teleports through walls.
            }
        }
        catch(Exception e) { CancelHook();dashUntil=0;Plugin.Logger.LogWarning(e.Message); }
    }

    void DrawLine(Vector2 start,Vector2 end,Color color,float width)
    {
        if(line==null)
        {
            visual=new GameObject("SANABI ability effect");line=visual.AddComponent<LineRenderer>();
            material=new Material(Shader.Find("Sprites/Default"));line.material=material;line.useWorldSpace=true;line.positionCount=2;line.sortingOrder=100;
        }
        visual!.SetActive(true);line.startWidth=line.endWidth=width;line.startColor=line.endColor=color;
        var z=actor!.bodyRenderer!=null?actor.bodyRenderer.transform.position.z-.02f:0;
        line.SetPosition(0,new Vector3(start.x,start.y,z));line.SetPosition(1,new Vector3(end.x,end.y,z));effectUntil=Time.time+.12f;
    }
    void HideLine() { if(visual!=null)visual.SetActive(false); }
    void CancelHook() { hookFlying=attached=false;anchor=null;HideLine(); }
    void Cleanup() { CancelHook();dashUntil=0;if(visual!=null)Object.Destroy(visual);if(material!=null)Object.Destroy(material);visual=null;line=null;material=null; }
    public void OnDestroy()=>Cleanup();
    public void OnGUI()
    {
        if(!CanAct())return;
#if HERO_DLC
        GUI.Label(new Rect(20,Screen.height-50,680,30),"GANCHO DEL HEROE [E mantener] · Espacio: impulso / soltar E: liberar");
#else
        GUI.Label(new Rect(20,Screen.height-50,800,30),"MAJOR SON · J combo / K disparo / L dash / R ráfaga · energía "+(int)logic.Energy);
#endif
    }
}
