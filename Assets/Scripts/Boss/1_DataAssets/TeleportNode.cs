using UnityEngine;

// 定义传送的战术策略
public enum TeleportTargetType
{
    RandomPoint,    // 随机传送点（一阶段防反）
    BehindPlayer,   // 绕背偷袭
    Center,         // 回到场地中央（二阶段转场等）
    IntentDistance  // 落在"离玩家正好是意图理想距离"的位置（位移卡池用；不在移动阶段使用时等同原地不动）
}

/// <summary>受击反应：被哪种伤害打中才会触发</summary>
public enum HitReactDamageType
{
    Melee,   // 只有近战
    Ranged,  // 只有远程
    Any      // 都算
}

/// <summary>
/// 传送卡。两种用法，可以同时具备：
///   1. 主动：放进 BossAIDecider 的传送池 / 套路，由 AI 抽出来用
///   2. 被动：勾选 React To Hit 并拖进 BAE_Teleporter 的 Hit Reactions 列表，挨打时按概率触发
///
/// 【冷却只有一个】就是基类的 cooldown。不管是 AI 主动用、还是挨打被动触发，
/// 用掉一次都记同一份冷却 —— 像一个充能槽，谁按的按钮都一样要等它充好。
/// </summary>
[CreateAssetMenu(fileName = "NewTeleportNode", menuName = "ScriptableObjects/TeleportNode")]
public class TeleportNode : ActionNode
{
    [Header("--- 传送专属配置 ---")]
    [Tooltip("传送前摇（秒）。0 表示瞬发。")]
    public float teleportDelay = 0.2f;

    [Tooltip("AI 决定使用哪种传送策略")]
    public TeleportTargetType targetType = TeleportTargetType.RandomPoint;

    [Header("--- 受击反应（被动触发）---")]
    [Tooltip("勾选 = 这张卡可以作为挨打时的被动传送。\n" +
             "还需要把它拖进 Boss 身上 BAE_Teleporter 的 Hit Reactions 列表才会生效。\n" +
             "冷却沿用上面的 Cooldown（与 AI 主动使用共用）")]
    public bool reactToHit = false;

    [Tooltip("在哪个作战姿态下生效：\n" +
             "Ranged = 护盾在（远程为主）\n" +
             "Melee  = 护盾碎（近战为主，眩晕期间不触发）\n" +
             "Any    = 都行")]
    public RoutineMode reactMode = RoutineMode.Ranged;

    [Tooltip("被哪种伤害打中才算")]
    public HitReactDamageType reactDamageType = HitReactDamageType.Melee;

    [Tooltip("基础触发概率（0~1）。每次符合条件的受击都会掷一次")]
    [Range(0f, 1f)]
    public float baseChance = 0.3f;

    [Tooltip("每挨一下却没触发，下次的概率增加多少。\n" +
             "例：基础 0.3、增加 0.2 → 30% → 50% → 70% → 90% → 100%。\n" +
             "触发后概率回到基础值；冷却期间挨打不累加")]
    [Range(0f, 1f)]
    public float chanceGainPerHit = 0.2f;

    [Tooltip("多久没挨打，累积的概率就清零回到基础值（秒）。0 = 永不清零")]
    [Min(0f)]
    public float chanceResetAfter = 2f;

    [Tooltip("勾选 = 触发时打断 Boss 正在放的招和正在走的套路，传完回到移动状态。\n" +
             "不勾 = 只把人传走，手上的招照常放完")]
    public bool interruptAction = false;
}
