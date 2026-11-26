using UnityEngine;

/// <summary>
/// Boss 的战斗节奏、姿态倾向、动作倍率（一张表管一个阶段）。
/// 战斗节奏和姿态倾向按远程为主（护盾在）/ 近战为主（护盾碎）各一套。
/// 拖到 Boss 身上 BossAIDecider 的 Phase 1 / Phase 2 Tempo Config 栏位。二阶段栏位留空 = 二阶段沿用一阶段的表。
/// 运行时只读。
/// </summary>
[CreateAssetMenu(fileName = "BossTempoConfig", menuName = "Flandre/Boss/Tempo Config")]
public class BossTempoConfig : ScriptableObject
{
    [Header("--- 动作倍率 ---")]
    [Tooltip("整体加快 / 放慢 Boss 出招：招式卡和套路里的所有时间都按这个倍率缩放。\n" +
             "2 = 快一倍（5 秒的招 2.5 秒放完）；0.5 = 慢一倍。\n" +
             "包括：近战前摇 / 出刀 / 收招、弹幕发射间隔和总时长、激光蓄力 / 照射 / 收招 / 追踪、\n" +
             "冲刺和突进斩（距离不变、速度变快）、传送前摇、套路的出招前停顿、脱手时间点。\n" +
             "不包括：子弹飞出去之后的速度、走路速度、本表的换位时间 / 靠位超时、冷却、被击退、破盾眩晕")]
    [Range(0.25f, 3f)]
    public float actionSpeed = 1f;

    [Header("--- 战斗节奏（每个作战姿态一套）---")]
    [Tooltip("远程为主（护盾在）时的节奏")]
    public BossAIDecider.CombatTempo rangedTempo = new BossAIDecider.CombatTempo(0.8f, 1.8f, true, 4f);

    [Tooltip("近战为主（护盾碎）时的节奏")]
    public BossAIDecider.CombatTempo meleeTempo = new BossAIDecider.CombatTempo(0.2f, 0.5f, true, 4f);

    [Header("--- 姿态倾向（单招和套路抽卡的权重倍率）---")]
    [Tooltip("护盾在（远程为主）时，各类招式的权重倍率。0 = 这一类完全不出。\n" +
             "单招：远程 / 近战按卡被放进 AI 配置表的哪个单招池算。\n" +
             "套路：按套路上的「招式定位」取 Ranged / Melee / Mix 倍率。\n" +
             "只影响同类之间（单招和单招、套路和套路）谁更容易被抽中；出套路还是出单招的比例仍看 AI 配置表的 Single Interleave Chance")]
    public BossAIDecider.ModeWeights rangedModeWeights = new BossAIDecider.ModeWeights(3f, 1f);

    [Tooltip("护盾碎（近战为主）时，各类招式的权重倍率。0 = 这一类完全不出")]
    public BossAIDecider.ModeWeights meleeModeWeights = new BossAIDecider.ModeWeights(1f, 3f);
}
