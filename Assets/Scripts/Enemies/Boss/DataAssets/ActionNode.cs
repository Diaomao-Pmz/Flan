using UnityEngine;
using Flandre.CombatSystem;

public class ActionNode : ScriptableObject
{
    [Header("--- AI 决策核心数据 ---")]
    [Tooltip("动作的标识名称（可用于匹配动画、技能名称等）")]
    public string actionName;

    [Tooltip("AI 抽卡时的默认基础权重")]
    public int baseWeight = 100;

    [Tooltip("该技能的冷却时间（秒）")]
    public float cooldown = 0f;

    [Tooltip("允许释放该技能的最小玩家距离")]
    public float minCastDistance = 0f;

    [Tooltip("允许释放该技能的最大玩家距离")]
    public float maxCastDistance = 15f;

    [Header("--- 通用动画配置 ---")]
    [Tooltip("蓄力/前摇动画名")]
    public string chargeAnimName;
    [Tooltip("释放/持续动画名")]
    public string activeAnimName;
    [Tooltip("收招/后摇动画名")]
    public string recoverAnimName;

    // ==========================================================
    // 打断相关
    // ==========================================================
    [Header("--- 打断韧性 ---")]
    [Tooltip(
        "本动作属于哪一类，决定它能被玩家的哪几级蓄力打断。\n\n" +
        "留 None = 按卡片类型自动判定（近战卡算 Melee、弹幕卡算 Bullet…），\n" +
        "绝大多数情况不需要手动填。\n\n" +
        "想让某张特殊卡的韧性和它的类型不一样时，才在这里覆写 ——\n" +
        "比如一张「普通近战但特别硬」的卡，可以填 SpecialMelee，\n" +
        "这样只有 AA3 才打得断。")]
    public ActionCategory categoryOverride = ActionCategory.None;

    [Tooltip(
        "勾选后本动作【永不可被打断】，无视一切 breakMask。\n" +
        "传送类动作应当勾上 —— 打断传送会产生一堆位置不一致的边界情况。")]
    public bool immuneToInterrupt = false;

    /// <summary>
    /// 本卡片默认属于哪一类。子类覆写它，就不需要在每个资产上手填。
    ///
    /// 之所以做成虚属性而不是在 BossController 里写一张
    /// 「类型 → 类别」的对照表，是因为"我是什么类别"是卡片自己的知识 ——
    /// 加新卡片时应该在新卡片里声明，而不是回去改一张中央表格。
    /// </summary>
    public virtual ActionCategory DefaultCategory => ActionCategory.Melee;

    /// <summary>实际生效的类别。没覆写就用子类声明的默认值</summary>
    public ActionCategory Category
        => categoryOverride != ActionCategory.None ? categoryOverride : DefaultCategory;

    /// <summary>本动作能否被这次伤害打断</summary>
    public bool CanBeInterruptedBy(in DamageInfo info)
    {
        if (immuneToInterrupt) return false;
        return info.CanBreak(Category);
    }
}
