using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Boss 的 AI 配置表（一张表管一个阶段）：单招池、套路池、位移卡池、近身反应池、兜底卡、弹幕在发时的规则表。
/// 拖到 Boss 身上 BossAIDecider 的 Phase 1 / Phase 2 AI Config 栏位。二阶段栏位留空 = 二阶段沿用一阶段的表。
/// 运行时只读。
/// </summary>
[CreateAssetMenu(fileName = "BossAIConfig", menuName = "Flandre/Boss/AI Config")]
public class BossAIConfig : ScriptableObject
{
    [Header("--- 单招池（每个元素：阶段标签 + 招式）---")]
    [FormerlySerializedAs("phase1Bullets")]
    public List<BossAIDecider.PoolEntry<BulletNode>> bullets = new List<BossAIDecider.PoolEntry<BulletNode>>();

    [FormerlySerializedAs("phase1Melees")]
    public List<BossAIDecider.PoolEntry<MeleeNode>> melees = new List<BossAIDecider.PoolEntry<MeleeNode>>();

    [Tooltip("远程系特殊技能（激光等）。放任何 ActionNode 派生资产即可。")]
    [FormerlySerializedAs("phase1SpecialBullets")]
    public List<BossAIDecider.PoolEntry<ActionNode>> specialBullets = new List<BossAIDecider.PoolEntry<ActionNode>>();

    [Tooltip("近战系特殊技能（突进斩等）。放任何 ActionNode 派生资产即可。")]
    [FormerlySerializedAs("phase1SpecialMelees")]
    public List<BossAIDecider.PoolEntry<ActionNode>> specialMelees = new List<BossAIDecider.PoolEntry<ActionNode>>();

    [Header("--- 套路池（每个元素：套路 + 情境加成）---")]
    [Tooltip("这个阶段可用的套路。会按套路自己的姿态 / 距离 / 玩家状态条件筛选")]
    [FormerlySerializedAs("phase1Routines")]
    public List<BossAIDecider.RoutineEntry> routines = new List<BossAIDecider.RoutineEntry>();

    [Tooltip("有合适套路时，仍有这个概率改出单招（节奏穿插，让 Boss 不那么好预判）。0 = 有套路就一定走套路")]
    [Range(0f, 1f)]
    public float singleInterleaveChance = 0.2f;

    [Header("--- 位移卡池（传送 / 冲刺：到位的手段，不参与出招抽卡）---")]
    [FormerlySerializedAs("phase1Movement")]
    public List<BossAIDecider.MovementEntry> movement = new List<BossAIDecider.MovementEntry>();

    [Header("--- 近身反应卡池（放弹幕时玩家贴近先出的招）---")]
    [Tooltip("只对开了「允许近身反应」的弹幕生效（弹幕卡或套路步骤上开）")]
    [FormerlySerializedAs("phase1Reactions")]
    public List<BossAIDecider.ReactionEntry> reactions = new List<BossAIDecider.ReactionEntry>();

    [Tooltip("全局反应冷却（秒）：每次挑反应招掷骰之后（不管掷没掷中），这么久之内不再反应。\n" +
             "反应招自己的 Cooldown 另外算")]
    [Min(0f)]
    public float reactionCooldown = 2f;

    [Header("--- 兜底 ---")]
    [Tooltip("卡池全空或全在冷却时的兜底牌。强烈建议配一张，否则会出现状态抖动。")]
    public ActionNode fallbackNode;

    [Header("--- 弹幕在发时（规则表）---")]
    [Tooltip("已经脱手的弹幕还在发时（Boss 身体空出来了），每一类卡能不能出。\n" +
             "不勾 = 等弹幕发完再出：等的时候照常走位，不算靠位超时；位移卡池里的这一类直接不考虑（改走路）。\n" +
             "卡上的「While Barrage」可以单独改成允许 / 禁止")]
    public BossAIDecider.BarrageRuleTable barrageRules = new BossAIDecider.BarrageRuleTable();
}
