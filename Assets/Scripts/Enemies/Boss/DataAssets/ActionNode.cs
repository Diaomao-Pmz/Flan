using UnityEngine;

/// <summary>招式属于哪个作战阶段（配置在 BossAIDecider 卡池的每个条目上）</summary>
public enum SkillPhaseTag
{
    Mix,     // 两个阶段都能用
    Ranged,  // 只在远程为主阶段（护盾在）用
    Melee    // 只在近战为主阶段（护盾碎）用
}

public class ActionNode : ScriptableObject
{
    [Header("--- AI ���ߺ������� ---")]
    [Tooltip("�����ı�ʶ���ƣ�������ƥ�䶯�����������Ƶȣ�")]
    public string actionName;

    [Tooltip("AI �鿨ʱ��Ĭ�ϻ���Ȩ��")]
    public int baseWeight = 100;

    [Tooltip("�ü��ܵ���ȴʱ�䣨�룩")]
    public float cooldown = 0f;

    [Tooltip("�����ͷŸü��ܵ���С��Ҿ���")]
    public float minCastDistance = 0f;

<<<<<<< Updated upstream
    [Tooltip("�����ͷŸü��ܵ������Ҿ���")]
    public float maxCastDistance = 15f;

    [Header("--- ͨ�ö������� ---")]
    [Tooltip("����/ǰҡ������")]
=======
    [Tooltip("允许释放该技能的最大玩家距离。\n" +
             "近战卡勾了 Auto Range From Hitbox 时，以按判定框自动算出的值为准，这里填的不生效")]
    public float maxCastDistance = 15f;

    /// <summary>
    /// 实际生效的最大射程。AI 一律读这个，而不是直接读 maxCastDistance。
    /// 近战卡会按判定框自动计算（见 MeleeNode）。
    /// </summary>
    public virtual float MaxCastDistance => maxCastDistance;

    [Tooltip("理想距离：Boss 准备出这一招时，会走到离玩家这么远的地方。\n" +
             "填 -1 = 自动取射程中点（min 与 max 的平均）。\n" +
             "填的数会被夹在 min~max 射程之内")]
    public float preferredDistance = -1f;

    [Header("--- 击退（所有招式通用）---")]
    [Tooltip("勾选 = 本招命中玩家时，用下面的击退力度代替玩家默认的击飞力度（PlayerCombatConfig）。\n" +
             "不勾 = 玩家按自己的默认值被击飞。")]
    public bool overrideKnockback = false;

    [Tooltip("击退力度：X = 水平（正数 = 推离 Boss），Y = 竖直（正数 = 向上）。只在勾选上一项时生效")]
    public Vector2 knockbackForce = new Vector2(1.5f, 8f);

    [Header("--- 通用动画配置 ---")]
    [Tooltip("蓄力/前摇动画名")]
>>>>>>> Stashed changes
    public string chargeAnimName;
    [Tooltip("�ͷ�/����������")]
    public string activeAnimName;
    [Tooltip("����/��ҡ������")]
    public string recoverAnimName;
}
