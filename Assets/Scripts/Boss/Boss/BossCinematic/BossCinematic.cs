using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 【Boss 演出卡】—— 一段演出的剧本：整场开关 + 一串按顺序执行的步骤。
///
/// ==========================================================
/// 比喻：演出卡是分镜剧本，BossCinematicDirector（挂在 Boss 身上）是现场导演。
///       战斗逻辑只负责喊「该演哪一场了」，怎么演全看剧本。
///
/// 谁来播：
///   · 转二阶段：BossCinematicDirector 的 Phase 2 Cinematic
///   · 某张招式卡放完后：招式卡的 Cinematic After
/// 演出期间 Boss 的 AI 不做决定；回盾倒计时、招式 / 套路 / 反应冷却都暂停，演完接着走。
/// 演完 Boss 回到移动状态，套路进度保留。Boss 死亡时演出立刻结束、一切还原；其他情况都不打断演出。
///
/// 以后加新步骤（例如镜头）：写一个 CinematicStep 的子类即可，步骤下拉框会自动列出来。
/// ==========================================================
/// </summary>
[CreateAssetMenu(fileName = "NewBossCinematic", menuName = "Flandre/Boss/Cinematic")]
public class BossCinematic : ScriptableObject
{
    [Header("--- 整场开关 ---")]
    [Tooltip("演出期间 Boss 无敌：挨打不扣血、不扣盾、不触发受击传送")]
    public bool bossInvulnerable = true;

    [Tooltip("演出期间锁住玩家输入：按键无效，移动输入清零；演完恢复（还按着的方向键会接着生效）")]
    public bool lockPlayerInput = false;

    [Tooltip("演出期间玩家无敌")]
    public bool playerInvulnerable = false;

    [Header("--- 步骤（从上往下依次执行）---")]
    [Tooltip("点每一步右边的下拉框选步骤类型：\n" +
             "出招   = 放一张 Boss 招式卡（传送卡也算，例：Center 传送卡 = 传到场地中央）。跟动作倍率走\n" +
             "等待   = 原地停几秒（真实时间，不跟动作倍率走）\n" +
             "播动画 = 播 Boss 动画器里的某个状态")]
    [SerializeReference, SubclassSelector]
    public List<CinematicStep> steps = new List<CinematicStep>();

#if UNITY_EDITOR
    /// <summary>点「+」新加的步骤会和上一步共用同一份参数（改一个另一个跟着变）：发现共用就给后面那步复制一份独立的</summary>
    private void OnValidate()
    {
        if (steps == null) return;
        var seen = SerializeReferenceDedup.NewSet();
        for (int i = 0; i < steps.Count; i++)
            steps[i] = SerializeReferenceDedup.Unshare(steps[i], seen);
    }
#endif
}

/// <summary>演出步骤的基类。新步骤类型继承它、实现 Run 即可</summary>
[Serializable]
public abstract class CinematicStep
{
    /// <summary>执行这一步。导演会等它跑完再执行下一步</summary>
    public abstract IEnumerator Run(BossCinematicDirector director);

    /// <summary>日志里显示的一句话描述</summary>
    public virtual string Describe() => GetType().Name;
}

/// <summary>出招：放一张 Boss 招式卡，走现有的执行器（伤害、拼刀、击退规则和平时一样）</summary>
[Serializable, SubclassName("出招")]
public class CinematicActionStep : CinematicStep
{
    [Tooltip("要放的招式卡（任意 ActionNode：弹幕、近战、激光、突进、传送、冲刺…）。\n" +
             "不看射程、不看冷却、不记冷却；放完（或被拼刀结束）再执行下一步")]
    public ActionNode action;

    public override IEnumerator Run(BossCinematicDirector director) => director.RunAction(action);

    public override string Describe() => $"出招「{(action != null ? action.name : "空")}」";
}

/// <summary>等待：原地停几秒（真实时间，不跟动作倍率走）</summary>
[Serializable, SubclassName("等待")]
public class CinematicWaitStep : CinematicStep
{
    [Tooltip("停多久（秒，真实时间）")]
    [Min(0f)]
    public float seconds = 1f;

    public override IEnumerator Run(BossCinematicDirector director)
    {
        if (seconds > 0f) yield return new WaitForSeconds(seconds);
    }

    public override string Describe() => $"等待 {seconds:F2} 秒";
}

/// <summary>播动画：播 Boss 动画器里的某个状态（不等它播完，要等就在后面接一个「等待」）</summary>
[Serializable, SubclassName("播动画")]
public class CinematicAnimStep : CinematicStep
{
    [Tooltip("Boss 动画器（Animator）里的状态名。Boss 没有动画器或名字为空时这一步什么都不做")]
    public string stateName;

    public override IEnumerator Run(BossCinematicDirector director)
    {
        director.PlayAnim(stateName);
        yield break;
    }

    public override string Describe() => $"播动画「{stateName}」";
}

/// <summary>
/// 镜头接口（预留）：以后做「切镜头 / 镜头还原」步骤时，镜头控制组件实现它，
/// 拖到 BossCinematicDirector 的 Camera Rig 栏位上。演出结束时导演会自动调用 RestoreDefault。
/// </summary>
public interface IBossCinematicCamera
{
    /// <summary>切到某台镜头（key 是镜头编号），blendTime 秒内过渡</summary>
    void SwitchTo(string key, float blendTime);

    /// <summary>还原到平时的跟随镜头</summary>
    void RestoreDefault(float blendTime);
}

/// <summary>[SubclassSelector] 下拉框里显示的名字（不加就显示类名）</summary>
[AttributeUsage(AttributeTargets.Class)]
public class SubclassNameAttribute : Attribute
{
    public readonly string displayName;
    public SubclassNameAttribute(string displayName) { this.displayName = displayName; }
}
