using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;
using Flandre.CombatSystem;

namespace Flandre.CombatSystem
{
    /// <summary>这一招打出去后，同一只手最早从哪个阶段开始可以蓄下一次力</summary>
    public enum ChargeAllowedFrom
    {
        [InspectorName("后摇结束（等这一招整个走完）")]
        AfterRecovery,

        [InspectorName("后摇开始")]
        RecoveryStart,

        [InspectorName("判定段开始")]
        ActiveStart,
    }

    public enum CastCondition
    {
        Anywhere,   // 海陆空均可
        GroundOnly, // 仅限地面
        AirOnly     // 仅限空中
    }

    public enum RequiredState
    {
        Any,
        IdleOrRun,
        Dash,
        Slide,
        Crouch
    }

    /// <summary>
    /// 【一段判定窗口】
    /// </summary>
    [System.Serializable]
    public class HitboxWindow
    {
        [Tooltip("给策划看的标签，如「第1刀」「收招突刺」。不影响逻辑")]
        public string label = "判定段";

        [Tooltip("从判定框打开那一刻起算（节拍总表的 Hitbox Start Time，或动画里的出刀事件），延迟多少秒开启本段判定")]
        public float startDelay = 0f;

        [Tooltip("本段判定持续多少秒。填 0 表示只判定一帧（瞬间伤害）；填 -1 表示从这一段开始一直开到判定段结束")]
        public float duration = 0f;

        [Tooltip("判定框尺寸")]
        public Vector2 size = new Vector2(1f, 1f);

        [Tooltip("判定框相对角色的偏移。X 会自动跟随朝向翻转")]
        public Vector2 offset = new Vector2(0.5f, 0f);

        [Tooltip("本段伤害")]
        public int damage = 1;

        [Tooltip(
            "本段是否清空命中名单。\n" +
            "勾选 = 同一个敌人可以被本段再打中一次（三连斩每刀都吃伤害）\n" +
            "取消 = 整个招式内每个敌人只吃一次伤害")]
        public bool refreshHitList = true;
    }

    /// <summary>蓄力位移的方向自由度</summary>
    public enum AimDashMode
    {
        /// <summary>任意角度，完全跟随瞄准（远程武器用）</summary>
        Free,

        /// <summary>八向吸附</summary>
        EightWay,

        /// <summary>四向吸附：上下左右（近战武器用）</summary>
        FourWay,

        /// <summary>只能朝角色当前面朝方向</summary>
        FacingOnly,
    }

    /// <summary>本招式允许被谁打断</summary>
    [System.Serializable]
    public class CancelPermission
    {
        [Tooltip("允许被主武器攻击派生/打断")]
        public bool byMainWeapon = true;

        [Tooltip("允许被副武器攻击派生/打断")]
        public bool bySubWeapon = true;

        [Tooltip("允许被位移动作（跳/冲/铲）打断")]
        public bool byMovement = true;
    }
}

[CreateAssetMenu(fileName = "NewComboNode", menuName = "Flandre/Combat/Combo Node")]
public class ComboNode : ScriptableObject
{
    [Header("节点基础属性")]
    public string nodeName = "XXX";

    [Tooltip(
        "招式类型，决定下面显示哪些参数、出招时走哪条路：\n" +
        "  Melee：近战（不显示弹幕参数）\n" +
        "  Ranged：远程（不显示单段判定）\n" +
        "  Mix：全部显示\n" +
        "  Special Melee / Special Ranged：特殊招式，在下面的「特殊招式」里选具体是哪一种；不出普通判定和子弹")]
    public ComboMoveType moveType = ComboMoveType.Mix;

    [Tooltip("特殊招式（只在招式类型选 Special 时生效）")]
    [SerializeReference]
    public SpecialMove specialMove;

    /// <summary>招式类型是 Special Melee / Special Ranged</summary>
    public bool IsSpecial => moveType == ComboMoveType.SpecialMelee || moveType == ComboMoveType.SpecialRanged;

    /// <summary>出招时要跑的特殊招式；不是特殊类型或没选时为 null</summary>
    public SpecialMove ActiveSpecial => IsSpecial ? specialMove : null;

    /// <summary>特殊招式要求前摇和判定段里站住不动</summary>
    public bool HoldsStillUntilRecovery => ActiveSpecial != null && ActiveSpecial.HoldsStillUntilRecovery;

    // ==========================================================
    // 连段窗口 / 三段时长 —— 已搬到 1_Config/P_Animation 的 AttackTimingConfig（招式节拍总表）
    // 这里留同名的只读属性去查表，用到它们的代码不用改。
    // 表里没有的招式：三段 = -1（跟动画走），连段窗口 = 0。
    // ==========================================================

    /// <summary>本招总时长走完之后还能接下一招的时间（查 AttackTimingConfig）</summary>
    public float comboWindow => AttackTimingConfig.GetComboWindow(this);

    /// <summary>前摇时长，-1 = 跟动画事件走（查 AttackTimingConfig）</summary>
    public float windupOverride => AttackTimingConfig.GetWindup(this);

    /// <summary>攻击时长，-1 = 跟动画事件走（查 AttackTimingConfig）</summary>
    public float activeOverride => AttackTimingConfig.GetActive(this);

    /// <summary>后摇时长，-1 = 跟动画事件走（查 AttackTimingConfig）</summary>
    public float recoveryOverride => AttackTimingConfig.GetRecovery(this);

    [Header("连段窗口 / 三段时长 / 动画与表现 / 蓄力位移 → 在节拍总表（1_Config/P_Animation）里调")]
    [Header("触发限制条件 (Trigger Requirements)")]
    public CastCondition castCondition = CastCondition.Anywhere;
    public RequiredState requiredState = RequiredState.Any;

    [Tooltip("按键序列：例如'上+攻击'，Size填2，Element0填Up，Element1填MainAttack")]
    public List<InputCmd> inputSequence = new List<InputCmd>() { InputCmd.MainAttack };

    // ==========================================================
    // 蓄力设定 (批次J 重做：从「一个阈值」升级为「等级系统」)
    // ==========================================================
    [Header("蓄力设定")]
    [Tooltip("这是否是一个蓄力招式？勾上后必须填下方的蓄力等级")]
    public bool isChargeSkill = false;

    [Tooltip(
        "这一招打出去后，同一只手最早从哪个阶段开始可以蓄下一次力（不管这一招是不是蓄力招）：\n" +
        "  后摇结束：等这一招整个走完才开始蓄（默认）\n" +
        "  后摇开始：这一招一进后摇就开始蓄\n" +
        "  判定段开始：这一招一进判定段就开始蓄\n" +
        "在这之前按住的，会存着等到点再开蓄。只是蓄力计时提前开始：松手放招仍然要等这一招走到「可被抢拍的阶段」（节拍总表）。\n" +
        "攻击动画照常播完，蓄力姿势要等攻击结束才会出现。换手蓄力不受这一项影响。")]
    public ChargeAllowedFrom chargeAllowedFrom = ChargeAllowedFrom.AfterRecovery;

    [Tooltip(
        "本招属于第几级蓄力 (AA1=1 / AA2=2 / AA3=3)。\n" +
        "松手时，引擎会在【等级 <= 当前蓄力等级】的候选里挑最高的那一个。\n" +
        "所以玩家蓄到 2 级松手会出 AA2，蓄到 3 级松手会出 AA3。")]
    [Range(0, 3)]
    public int chargeLevel = 0;

    [Tooltip(
        "平a后起蓄时间（秒）。平a打到第 N 段后长按，按住满这么久，第 N 级的蓄力招就生效（松手能放出来）。\n" +
        "读的是「这只手的武器里、等级 ≤ N 的最高那一招」（只看不带方向的那一招，比如 AA2，不看 UP_AA2）。\n" +
        "换手也一样：A2 之后长按副武器，读 BB2 的这个值。\n" +
        "不影响原地起手（按武器的 Charge Time Lv1）和按侧键升级（按武器的等级阈值）。")]
    [Min(0f)]
    public float comboChargeStartTime = 0.3f;

    [Tooltip(
        "蓄到这一招的等级时，移动要不要打折（移速、跳跃力从下面的起始倍率，用 Charge Slow Ramp Time 线性变到终点倍率）。\n" +
        "蓄力等级在 1→2→3 之间循环：蓄到哪一级就用哪一招的设定（比如只给 AA3 勾，就只有蓄到 3 级时减速）。\n" +
        "两只手同时蓄力时，取减速更重的那一个。")]
    public bool chargeSlowsMovement = false;

    [Tooltip(
        "移速的起始倍率：刚开始蓄力、减速刚生效时的速度。1 = 原速。\n" +
        "只在这只手刚开始蓄力时用；蓄着蓄着换到这一招（按侧键升级）时，从换招那一刻的实际速度出发，不会突然变快")]
    [Range(0f, 1f)]
    public float chargeMoveSpeedStart = 1f;

    [Tooltip("移速的终点倍率：渐变走完后稳定在这个速度。0.2 = 只有平时的两成，1 = 不影响移速")]
    [Range(0f, 1f)]
    public float chargeMoveSpeedMultiplier = 0.2f;

    [Tooltip("跳跃力的起始倍率，规则和移速一样。1 = 原本的跳跃力")]
    [Range(0.1f, 1f)]
    public float chargeJumpForceStart = 1f;

    [Tooltip("跳跃力的终点倍率：渐变走完后稳定在这个值。0.7 = 跳得比平时低三成，1 = 不影响跳跃")]
    [Range(0.1f, 1f)]
    public float chargeJumpForceMultiplier = 0.7f;

    [Tooltip(
        "渐变时长（秒）：从这一招的减速开始生效起，用多久从起始倍率线性变到终点倍率（移速和跳跃力一起）。\n" +
        "-1 = 和这一招的 Combo Charge Start Time（平a后起蓄时间）一样长：起蓄生效的那一刻正好减到最慢\n" +
        "0 = 一下子到终点倍率")]
    [Min(-1f)]
    public float chargeSlowRampTime = -1f;

    /// <summary>实际的渐变时长：填 -1 时取这一招的平a后起蓄时间</summary>
    public float ResolvedChargeSlowRampTime => chargeSlowRampTime < 0f ? Mathf.Max(0f, comboChargeStartTime) : chargeSlowRampTime;

    // ==========================================================
    // 动画与表现 —— 已搬到 1_Config/P_Animation 的 AttackTimingConfig（招式节拍总表）
    // 这里留同名的只读属性去查表，用到它们的代码不用改。
    // 表里没有的招式：没有动画（也打不出判定），不位移。
    // ==========================================================

    /// <summary>攻击动画剪辑（查 AttackTimingConfig）</summary>
    public AnimationClip attackClip => AttackTimingConfig.GetClip(this);

    /// <summary>出招后第几秒播招式特效（节拍总表）。-1 = 跟动画里的 PlayEffect 事件走</summary>
    public float playEffectTime => AttackTimingConfig.GetPlayEffectTime(this);
    public string animName => attackClip != null ? attackClip.name : string.Empty;

    /// <summary>出招位移【距离】：从 thrustStart 选的阶段开头起步，匀速走完（查节拍总表）</summary>
    public Vector2 forwardThrust => AttackTimingConfig.GetThrust(this);

    /// <summary>出招位移从哪个阶段的开头起步（查节拍总表）</summary>
    public ThrustStartPhase thrustStart => AttackTimingConfig.GetThrustStart(this);

    /// <summary>位移用多久走完，-1 = 和所选阶段一样长（查节拍总表）</summary>
    public float thrustDuration => AttackTimingConfig.GetThrustDuration(this);

    /// <summary>位移走着的时候可以穿过 Boss（查节拍总表）</summary>
    public bool passThroughDuringThrust => AttackTimingConfig.GetPassThroughBoss(this);

    public bool applyThrustInAir => AttackTimingConfig.GetApplyThrustInAir(this);
    public bool lockMovementDuringAttack => AttackTimingConfig.GetLockMovement(this);

    // ---- 旧数据：只用来第一次搬进节拍总表，Inspector 里不显示 ----
    [SerializeField, HideInInspector, FormerlySerializedAs("attackClip")] private AnimationClip legacyAttackClip;
    [SerializeField, HideInInspector, FormerlySerializedAs("forwardThrust")] private Vector2 legacyForwardThrust;
    [SerializeField, HideInInspector, FormerlySerializedAs("thrustDuration")] private float legacyThrustDuration = 0.12f;
    [SerializeField, HideInInspector, FormerlySerializedAs("applyThrustInAir")] private bool legacyApplyThrustInAir = true;
    [SerializeField, HideInInspector, FormerlySerializedAs("lockMovementDuringAttack")] private bool legacyLockMovement = true;

    public AnimationClip LegacyAttackClip => legacyAttackClip;
    public Vector2 LegacyForwardThrust => legacyForwardThrust;
    public float LegacyThrustDuration => legacyThrustDuration;
    public bool LegacyApplyThrustInAir => legacyApplyThrustInAir;
    public bool LegacyLockMovement => legacyLockMovement;

    // ==========================================================
    // 【批次F 新增】取消权限
    // ==========================================================
    [Header("取消权限 (谁可以打断这一招)")]
    [Tooltip(
        "原先只有一个「能不能被取消」的开关，粒度不够 ——\n" +
        "无法表达「这一招只能被副武器接，不能被主武器自己接」这类规则。\n" +
        "这是主副武器配合连招的关键：靠限制取消权限来引导玩家换武器。")]
    public CancelPermission cancelPermission = new CancelPermission();

    // ==========================================================
    // 【批次F 新增】多段判定窗口
    // ==========================================================
    // 【2026-11-21】一招多段的判定窗口列表、单段判定的持续时间 → 搬到节拍总表（和时间放在一起）。
    // 这里留同名的只读属性去查表；旧数据隐藏保留，表里第一次绑上招式时自动搬过去。
    [FormerlySerializedAs("hitboxWindows")]
    [SerializeField, HideInInspector] private List<HitboxWindow> legacyHitboxWindows = new List<HitboxWindow>();

    [FormerlySerializedAs("hitboxDuration")]
    [SerializeField, HideInInspector] private float legacyHitboxDuration = 0f;

    /// <summary>一招多段的判定窗口（查节拍总表）。为空时用下面的单段判定</summary>
    public List<HitboxWindow> hitboxWindows => AttackTimingConfig.GetHitboxWindows(this);

    /// <summary>单段判定持续多久（查节拍总表）</summary>
    public float hitboxDuration => AttackTimingConfig.GetHitboxDuration(this);

    /// <summary>判定段开始后第几秒开判定框（节拍总表）。-1 = 跟动画里的 TriggerAttackHitbox 事件走</summary>
    public float hitboxStartTime => AttackTimingConfig.GetHitboxStartTime(this);

    /// <summary>判定段开始后第几秒出弹（节拍总表）。-1 = 跟动画里的 SpawnProjectile 事件走</summary>
    public float projectileStartTime => AttackTimingConfig.GetProjectileStartTime(this);

    /// <summary>出刀 / 出弹至少有一样按节拍总表的时间走（不看动画事件）</summary>
    public bool HasTableStrikeTime => hitboxStartTime >= 0f || projectileStartTime >= 0f;

    /// <summary>这一招在哪几段可以被下一招抢拍（查节拍总表，可多选）</summary>
    public AttackPhaseMask switchCutPhases => AttackTimingConfig.GetSwitchCutPhases(this);

    public List<HitboxWindow> LegacyHitboxWindows => legacyHitboxWindows;
    public float LegacyHitboxDuration => legacyHitboxDuration;

    [Header("单段判定 (节拍总表里这一招的判定窗口列表为空时使用 · 持续时间在节拍总表)")]
    [Tooltip("伤害")]
    public int damage = 0;

    [Tooltip("判定框尺寸（宽, 高）。点下面的「编辑判定框」可以在 Scene 视图里拖着调")]
    public Vector2 hitboxSize = new Vector2(0f, 0f);

    [Tooltip("判定框中心相对角色的偏移。X 会自动跟随朝向翻转，填正数 = 身前")]
    public Vector2 hitboxOffset = new Vector2(0f, 0f);


    // ==========================================================
    // 【批次G 新增】连段深度限制
    // ==========================================================
    // 特效已全部搬到场景里的 EffectManager（特效表：预制体 + 参数），招式上不再配

    // ==========================================================
    // 目标交互
    // ==========================================================
    [Header("弹幕参数 (远程招式 · 留空则沿用武器默认值)")]
    [Tooltip(
        "本招专用的子弹池 key。留空 = 用武器 WeaponMoveSet 上配的那个。\n\n" +
        "为什么要能按招式覆盖：B1/B2/B3 射普通弹，BB1/BB2 要射更大更快的，\n" +
        "同一把枪需要多种弹 —— 枪是同一把，但弹匣可以换。")]
    public string projectilePoolKeyOverride = "";

    [Tooltip("一次出招连射几发。1 = 单发；速射枪式的连射填 3/6/9")]
    [Min(1)]
    public int projectileCount = 1;

    [Tooltip("连射的发间隔（秒）。0 = 同一帧全部射出（齐射）")]
    public float projectileInterval = 0.06f;

    [Tooltip("子弹速度倍率。1 = 用预制体上的原始速度")]
    public float projectileSpeedMultiplier = 1f;

    [Tooltip("子弹体积倍率。1 = 用预制体上的原始大小")]
    public float projectileScaleMultiplier = 1f;

    [Tooltip(
        "这一招所有子弹加起来的总伤害，按上面的连射发数平分（除不尽的余数分给前几颗，全中正好等于总伤害）。\n" +
        "填 0 = 不分摊，每颗子弹用预制体上的原始伤害。\n" +
        "例：总伤害 10、连射 3 发 → 4 / 3 / 3")]
    [Min(0)]
    [FormerlySerializedAs("projectileDamageOverride")]
    public int projectileTotalDamage = 0;

    /// <summary>有没有填子弹总伤害（没填 = 每颗用预制体伤害）</summary>
    public bool HasProjectileTotalDamage => projectileTotalDamage > 0;

    /// <summary>第 index 颗子弹（从 0 数）分到的伤害。没填总伤害时返回 -1</summary>
    public int ProjectileDamageFor(int index)
    {
        if (projectileTotalDamage <= 0) return -1;
        int count = Mathf.Max(1, projectileCount);
        int baseDamage = projectileTotalDamage / count;
        int remainder = projectileTotalDamage % count;
        return baseDamage + (index < remainder ? 1 : 0);
    }

    [Tooltip(
        "每一发都重新计算瞄准方向。\n" +
        "勾选 = 连射途中玩家改方向键能改变后续子弹的方向（可微调扫射）\n" +
        "取消 = 整轮连射沿用第一发的方向")]
    public bool recomputeAimPerShot = true;

    // ==========================================================
    // 蓄力位移（AA1 / BB1 这类"带位移的蓄力攻击"）
    // ==========================================================
    // ==========================================================
    // 蓄力位移（Aim Dash）—— 已搬到 1_Config/P_Animation 的 AttackTimingConfig（招式节拍总表）
    // 动量加成已删除，距离就是表里填的距离。
    // ==========================================================

    /// <summary>本招是否带瞄准方向的位移（查 AttackTimingConfig）</summary>
    public bool useAimDash => AttackTimingConfig.GetUseAimDash(this);
    public AimDashMode aimDashMode => AttackTimingConfig.GetAimDashMode(this);

    /// <summary>位移距离，负数 = 朝瞄准的反方向弹开（查 AttackTimingConfig）</summary>
    public float aimDashDistance => AttackTimingConfig.GetAimDashDistance(this);

    /// <summary>位移用多久走完，-1 = 和攻击段一样长（查 AttackTimingConfig）</summary>
    public float aimDashDuration => AttackTimingConfig.GetAimDashDuration(this);

    // ---- 旧数据：只用来第一次搬进节拍总表，Inspector 里不显示 ----
    [SerializeField, HideInInspector, FormerlySerializedAs("useAimDash")] private bool legacyUseAimDash;
    [SerializeField, HideInInspector, FormerlySerializedAs("aimDashMode")] private AimDashMode legacyAimDashMode = AimDashMode.FourWay;
    [SerializeField, HideInInspector, FormerlySerializedAs("aimDashBaseDistance")] private float legacyAimDashBaseDistance = 2f;
    [SerializeField, HideInInspector, FormerlySerializedAs("aimDashDuration")] private float legacyAimDashDuration = 0.15f;

    public bool LegacyUseAimDash => legacyUseAimDash;
    public AimDashMode LegacyAimDashMode => legacyAimDashMode;
    public float LegacyAimDashBaseDistance => legacyAimDashBaseDistance;
    public float LegacyAimDashDuration => legacyAimDashDuration;

    [Header("打断能力")]
    [Tooltip(
        "本招能打断敌人的哪几类动作。留 None = 没有打断能力（普通平A）。\n\n" +
        "按当前设计：\n" +
        "  AA1 → 勾 Melee\n" +
        "  AA2 → 勾 Bullet\n" +
        "  AA3 → 选 All Attacks（近战+弹幕+全部特殊技）\n\n" +
        "注意这是【对位】不是【等级】—— AA2 打不断近战，这是刻意的。\n" +
        "传送类动作永不可打断，所以不要指望勾上 Teleport 会有效果。")]
    public ActionCategory breakMask = ActionCategory.None;

    [FormerlySerializedAs("knockbackForce")]
    [Tooltip(
        "击退 Boss 的距离（格）。0 = 不击退。\n" +
        "打中时 Boss 往远离玩家的方向滑开这么远（由快到慢）。\n" +
        "只有能打断 Boss 当前动作（看上面的 Break Mask）、或 Boss 没在出招时才会被击退；眩晕中不击退。\n" +
        "滑多久由 Boss 身上的 Knockback Duration 决定。")]
    [Min(0f)]
    public float bossKnockbackDistance = 0f;

    [Tooltip(
        "持续推动速度（格/秒）。0 = 不推。\n" +
        "持续伤害的招式（目前只有激光）每一跳都让 Boss 往远离玩家的方向被推着走，连续命中就一直推，停火后很快停下。\n" +
        "Boss 走位、出招照常，只是被推着走；眩晕中不推。\n" +
        "第一跳的击退距离和硬直仍然看上面的 Boss Knockback Distance 和下面的 Boss Hit Stun。")]
    [Min(0f)]
    public float bossPushSpeed = 0f;

    [Tooltip(
        "Boss 硬直时间（秒）。0 = 不硬直。\n" +
        "条件和击退一样：打断了 Boss 当前的招、或 Boss 本来没在出招时才生效；眩晕中不处理。\n" +
        "有击退时边滑边硬直（取滑行时间和硬直时间里长的那个），没有击退时原地硬直。时间到了 Boss 回到移动状态重新做决定。")]
    [Min(0f)]
    public float bossHitStun = 0f;

    [Tooltip(
        "希望敌人做出的位移表现。\n" +
        "  上挑 → Launch\n" +
        "  下砸 → Slam\n" +
        "  普通招式 → None\n\n" +
        "注意这只是「希望」——最终怎么表现由受击方裁决。\n" +
        "比如 Boss 护盾没破时，上挑和下砸都会统一表现为后退。")]
    public HitReaction hitReaction = HitReaction.None;

    [Tooltip(
        "反应强度，含义随上面的类型变化：\n" +
        "  Launch → 额外浮空时间（秒）。AA3 上挑填正数实现「延长击飞」\n" +
        "  Slam   → 落地后的弹起速度。AA3 下砸填正数实现「落地再弹起」\n" +
        "  其余   → 不使用")]
    public float hitReactionParam = 0f;

    // ==========================================================
    // 【2026-11-21】消弹能力（通用，所有招式类型都显示）
    // ==========================================================
    [Header("消弹能力 (通用)")]
    [Tooltip(
        "这一招用什么方式抵消 Boss 的子弹，可多选：\n" +
        "  子弹：这一招射出的子弹会擦掉碰到的 Boss 子弹（自己继续飞，直到打中 Boss 或飞出画面）\n" +
        "  近战判定：判定框开着的每一帧，框里的 Boss 子弹都会被消掉（只消子弹，不免伤；远程招 / 特殊招没有近战判定框，勾了无效）\n" +
        "  位移：这一招的位移（节拍总表里的出招位移 / 蓄力位移）进行期间，碰到芙兰的 Boss 子弹全部消失，也不扣血\n" +
        "被消掉的子弹位置会播特效表里「消弹特效」那一条（没配就不播）。")]
    public BulletCancelMode bulletCancel = BulletCancelMode.None;

    /// <summary>这一招有没有勾某种消弹方式</summary>
    public bool CancelsBulletsBy(BulletCancelMode mode) => (bulletCancel & mode) != 0;

    // ==========================================================
    // 【2026-11-20】拼刀
    // ==========================================================
    [Header("拼刀 (Clash)")]
    [Tooltip(
        "勾上 = 这一招在判定段开头有一小段拼刀窗口：\n" +
        "窗口里拼刀框撞上 Boss 正在生效的近战判定框 → 拼刀成功，Boss 这一下不造成伤害。\n" +
        "能拼哪几类看下面的 Clash Mask；窗口时长在节拍总表里调。")]
    public bool canClash = false;

    [Tooltip(
        "拼刀框中心相对角色位置的偏移（和判定框同一个算法）。X 会自动乘朝向，填正数 = 身前。\n" +
        "点下面的「编辑拼刀框」按钮，可以在 Scene 视图里拖着调。")]
    public Vector2 clashBoxOffset = new Vector2(0.8f, 0.8f);

    [Tooltip("拼刀框尺寸（宽, 高）。不造成伤害，只用来拼刀")]
    public Vector2 clashBoxSize = new Vector2(1f, 1.2f);

    [Tooltip(
        "能拼掉 Boss 哪几类招式（和 Break Mask 同一种多选）。\n" +
        "目前只有 Boss 的近战挥击会检查拼刀：Melee（普通近战卡）、Special Melee（突进卡的挥击段）。\n" +
        "勾 Bullet / Special Bullet 暂时没有效果（子弹不做抵消）。\n" +
        "拼刀成功：Boss 这一下不造成伤害，只结束当前这一招，套路照常接下一步。")]
    public ActionCategory clashMask = ActionCategory.AllMelee;

    [Tooltip(
        "拼刀成功后玩家的位移（格）：X 跟着朝向走，正数往身前、负数往身后；Y 正数向上。(0, 0) = 不位移。\n" +
        "会顶掉这一招原本的出招位移，按精确距离匀速走完。例：BB1 急退 2 格 = (-2, 0)")]
    public Vector2 clashThrust = Vector2.zero;

    [Tooltip("拼刀位移用多久走完（秒）")]
    [Min(0.01f)]
    public float clashThrustDuration = 0.15f;

    [Tooltip("拼刀位移期间可以穿过 Boss（敌人）。终点判定照样生效，不会停在 Boss 身体里")]
    public bool clashThrustPassThrough = false;

    /// <summary>拼刀窗口时长（秒），从判定段开始算（查节拍总表）</summary>
    public float clashWindow => AttackTimingConfig.GetClashWindow(this);

    [Header("目标交互")]
    [Tooltip(
        "命中敌人后自动转向那个敌人。\n\n" +
        "给滑铲攻击这类「边冲边砍」的招式用：命中就转向，没命中维持滑铲方向。\n" +
        "普通平A【不建议】勾选 —— 砍到背后的敌人时会突然转身，手感很怪。")]
    public bool faceTargetOnHit = false;

    [Header("连段深度限制 (武器化)")]
    [Tooltip("本招最早能出现在第几段。1 = 可作起手。填 0 表示不限制")]
    public int minComboDepth = 0;

    [Tooltip("本招最晚能出现在第几段。填 0 表示不限制")]
    public int maxComboDepth = 0;

    [Header("连招派生树 (同武器内部的固定衔接)")]
    [Tooltip(
        "用于同一把武器内部那种「必须精确衔接」的固定小段，比如三连突刺。\n\n" +
        "⚠️ 不要用它去引用另一把武器的招式 —— 那会让武器互相认识，\n" +
        "加一把新武器就要回来改所有旧武器。跨武器接续请用 WeaponMoveSet.followUps，" +
        "由连招引擎在运行时按槽位解析。")]
    public List<ComboNode> childNodes = new List<ComboNode>();

    // ==========================================================
    // 查询接口
    // ==========================================================

    /// <summary>本招式是否配置了多段判定</summary>
    public bool HasMultipleWindows => hitboxWindows != null && hitboxWindows.Count > 0;

    /// <summary>
    /// 取得本招式的所有判定窗口。
    /// </summary>
    public void CollectWindows(List<HitboxWindow> output)
    {
        output.Clear();

        if (HasMultipleWindows)
        {
            for (int i = 0; i < hitboxWindows.Count; i++)
            {
                if (hitboxWindows[i] != null) output.Add(hitboxWindows[i]);
            }
            return;
        }

        // 旧版单段参数 → 合成一个窗口
        output.Add(legacyWindowCache ??= new HitboxWindow());

        var w = output[0];
        w.label = "旧版单段";
        w.startDelay = 0f;
        w.duration = hitboxDuration;
        w.size = hitboxSize;
        w.offset = hitboxOffset;
        w.damage = damage;
        w.refreshHitList = false;
    }

    // 复用同一个对象，避免每次攻击都 new（零分配）
    [System.NonSerialized] private HitboxWindow legacyWindowCache;

    /// <summary>本招式能否被指定的攻击指令派生/打断</summary>
    public bool CanBeCanceledBy(InputCmd cmd)
    {
        if (cmd == InputCmd.MainAttack) return cancelPermission.byMainWeapon;
        if (cmd == InputCmd.SubAttack) return cancelPermission.bySubWeapon;
        return cancelPermission.byMovement;
    }

    /// <summary>本招式能否被位移动作（跳/冲/铲）打断</summary>
    public bool CanBeCanceledByMovement => cancelPermission.byMovement;

    /// <summary>判定框的偏移：有多段判定时取第一段，否则用旧版单段参数。EffectManager 的「用判定框偏移」读这个</summary>
    public Vector2 GetFirstHitboxOffset()
    {
        if (HasMultipleWindows && hitboxWindows[0] != null) return hitboxWindows[0].offset;
        return hitboxOffset;
    }

    /// <summary>
    /// 在 Inspector 里右键组件标题 → 「打印三段时长」：
    /// 把本招最终生效的 前摇 / 攻击 / 后摇 打到 Console，并说明每一段是从动画读的还是手填的。
    /// </summary>
    [ContextMenu("打印三段时长")]
    private void PrintPhaseTimings()
    {
        // 编辑器里动画事件可能刚改过，先丢掉缓存再读
        if (!Application.isPlaying) AttackTimelineData.Invalidate(attackClip);

        AttackTiming timing = AttackTimelineData.ResolveTiming(this);
        float windup = timing.windup, active = timing.active, recovery = timing.recovery;
        AttackTimelineData data = AttackTimelineData.Get(attackClip);

        // 每一段动画的播放倍速（实际时长为 0 的段直接跳过）
        string Speed(int seg, float realLen) =>
            !timing.hasClip ? "不调速" : realLen <= 0.0001f ? "动画跳过" : $"动画 ×{timing.SpeedOf(seg):F2}";

        string clipInfo = attackClip != null ? $"{attackClip.name}（{attackClip.length:F3}s）" : "未指定动画";
        string windupSrc = windupOverride >= 0f ? "节拍总表"
            : (data.attackBeginTime >= 0f ? "动画事件 MarkAttackBegin"
                : (data.firstStrikeTime >= 0f ? "没有 MarkAttackBegin → 第一个出刀/出弹事件" : "动画里没有出刀/出弹事件 → 0"));
        string activeSrc = activeOverride >= 0f ? "节拍总表"
            : (data.attackEndTime >= 0f ? "动画事件 MarkAttackEnd"
                : (data.firstStrikeTime >= 0f ? "没有 MarkAttackEnd → 按判定窗口/连射推算" : "没有任何事件 → 整招都算攻击"));
        string recoverySrc = recoveryOverride >= 0f ? "节拍总表" : "动画剩余时间";
        string tableInfo = AttackTimingConfig.Active == null ? "（工程里还没有 AttackTimingConfig，全部按动画走）"
            : AttackTimingConfig.Contains(this) ? "" : "（不在节拍总表里，三段按动画走）";

        PlayerLog.Info(
            $"[连招节点] 「{nodeName}」 动画 {clipInfo}{tableInfo}\n" +
            $"  前摇 {windup:F3}s（{windupSrc}，{Speed(0, windup)}）\n" +
            $"  攻击 {active:F3}s（{activeSrc}，{Speed(1, active)}）\n" +
            $"  后摇 {recovery:F3}s（{recoverySrc}，{Speed(2, recovery)}）\n" +
            $"  总时长 {windup + active + recovery:F3}s，之后还有 {comboWindow:F2}s 连段窗口", this);
    }

    /// <summary>本招是否为蓄力招（且等级配置有效）</summary>
    public bool IsValidChargeNode => isChargeSkill && chargeLevel > 0;

    /// <summary>
    /// 本招式能否出现在第 depth 段。
    /// depth 从 1 开始计数：起手是第 1 段。
    /// min / max 填 0 表示该侧不限制。
    /// </summary>
    public bool IsDepthAllowed(int depth)
    {
        if (minComboDepth > 0 && depth < minComboDepth) return false;
        if (maxComboDepth > 0 && depth > maxComboDepth) return false;
        return true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (isChargeSkill && chargeLevel <= 0)
        {
            Debug.LogWarning(
                $"[连招节点] 「{nodeName}」勾选了 isChargeSkill 但 chargeLevel 是 0，" +
                "松手时永远不会被选中。请填 1/2/3。", this);
        }

        if (!isChargeSkill && chargeLevel > 0)
        {
            Debug.LogWarning(
                $"[连招节点] 「{nodeName}」填了 chargeLevel 但没勾 isChargeSkill，等级不会生效。", this);
        }

        if (projectileTotalDamage > 0 && projectileTotalDamage < Mathf.Max(1, projectileCount))
        {
            Debug.LogWarning(
                $"[连招节点] 「{nodeName}」子弹总伤害 {projectileTotalDamage} 小于连射发数 {projectileCount}，" +
                "有几颗子弹会是 0 伤害。", this);
        }

        // 【正式版整改 D · 规范 6】动画事件不能是唯一生命线。
        // 出刀 / 出弹的时间点来自动画事件；动画里一个都没有时，这一招不会出伤害 —— 配置阶段就提醒。
        // 只在非运行时检查，并先丢掉缓存（动画事件可能刚改过）。
        // 动画剪辑在节拍总表里；这里只在表已经加载过时检查（资产导入期间不去全工程搜表）
        AnimationClip clip = AttackTimingConfig.PeekClip(this);
        // 特殊招式按三段时长自己跑，不看动画里的出刀 / 出弹事件
        if (!Application.isPlaying && clip != null && !IsSpecial)
        {
            AttackTimelineData.Invalidate(clip);
            AttackTimelineData data = AttackTimelineData.Get(clip);

            if (data.firstStrikeTime < 0f && !HasTableStrikeTime)
            {
                Debug.LogWarning(
                    $"[连招节点] 「{nodeName}」的动画「{clip.name}」里没有 TriggerAttackHitbox / SpawnProjectile 事件，" +
                    "这一招不会产生任何判定或子弹。如果它本来就不出伤害，可以忽略。", this);
            }
            else if (data.attackBeginTime >= 0f && data.firstStrikeTime < data.attackBeginTime - 0.0001f)
            {
                Debug.LogWarning(
                    $"[连招节点] 「{nodeName}」的动画「{clip.name}」里有出刀 / 出弹事件在 MarkAttackBegin 之前，" +
                    "它会在前摇里就触发。确认一下是不是事件放错了位置。", this);
            }
        }
    }
#endif
}

/// <summary>招式特效的存活方式</summary>
public enum EffectLifetimeMode
{
    FollowActive,   // 跟判定段走
    FollowAttack,   // 跟整招走
    Fixed,          // 播完特效自己的动画一遍
    UntilMarker,    // 到动画里配对的 EffectEnd 事件为止
}
