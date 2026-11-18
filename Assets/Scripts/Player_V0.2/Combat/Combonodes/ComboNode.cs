using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;
using Flandre.CombatSystem;

namespace Flandre.CombatSystem
{
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

        [Tooltip("从动画事件触发起算，延迟多少秒开启本段判定")]
        public float startDelay = 0f;

        [Tooltip("本段判定持续多少秒。填 0 表示只判定一帧（瞬间伤害）")]
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

<<<<<<< Updated upstream
    [Tooltip("本招打完后，玩家有多久时间可以接下一招。超时则连招断档回到起手")]
    public float comboWindow = 0.5f;

    [Tooltip(
        "后摇时长（秒）—— 打出本招后，【同一只手】要等这么久才能再出招。\n\n" +
        "换手不受影响，这正是「换手可以抢拍」的实现基础。\n\n" +
        "刻意用参数而不是读动画长度：动画后期会替换，\n" +
        "参数化才不会每换一次美术就要重调手感。\n" +
        "蓄力招按等级填，等级越高锁越久。")]
    public float recoveryTime = 0.35f;

    [Tooltip(
        "【普攻后蓄力最短时长】（秒）—— 只对普通攻击生效，蓄力招填了也不用。\n\n" +
        "打完本招接长按蓄力时，蓄力直接从「本段对应的等级」起步（第 1 段 → AA1，第 2 段 → AA2…），\n" +
        "但必须按住满这么久才解锁；不满就松手 = 什么都不放。\n\n" +
        "从进入蓄力姿态那一刻（长按判定成立时）开始计。\n" +
        "这段时间同时也计入升级进度，不是先等完再开始攒。")]
    [Min(0f)]
    public float chargeMinHoldAfter = 0.5f;
=======
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
>>>>>>> Stashed changes

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
        "本招属于第几级蓄力 (AA1=1 / AA2=2 / AA3=3)。\n" +
        "松手时，引擎会在【等级 <= 当前蓄力等级】的候选里挑最高的那一个。\n" +
        "所以玩家蓄到 2 级松手会出 AA2，蓄到 3 级松手会出 AA3。")]
    [Range(0, 3)]
    public int chargeLevel = 0;

    // ==========================================================
    // 动画与表现 —— 已搬到 1_Config/P_Animation 的 AttackTimingConfig（招式节拍总表）
    // 这里留同名的只读属性去查表，用到它们的代码不用改。
    // 表里没有的招式：没有动画（也打不出判定），不位移。
    // ==========================================================

    /// <summary>攻击动画剪辑（查 AttackTimingConfig）</summary>
    public AnimationClip attackClip => AttackTimingConfig.GetClip(this);
    public string animName => attackClip != null ? attackClip.name : string.Empty;

    /// <summary>出招位移【距离】：从 thrustStart 选的阶段开头起步，匀速走完（查节拍总表）</summary>
    public Vector2 forwardThrust => AttackTimingConfig.GetThrust(this);

    /// <summary>出招位移从哪个阶段的开头起步（查节拍总表）</summary>
    public ThrustStartPhase thrustStart => AttackTimingConfig.GetThrustStart(this);

    /// <summary>位移用多久走完，-1 = 和所选阶段一样长（查节拍总表）</summary>
    public float thrustDuration => AttackTimingConfig.GetThrustDuration(this);

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
    [Header("判定窗口 (支持一招多段)")]
    [Tooltip(
        "留空则使用下方【旧版单段参数】，保证已配好的招式不受影响。\n" +
        "想做三连斩这类一招多段的招式时，在这里加多条即可。")]
    public List<HitboxWindow> hitboxWindows = new List<HitboxWindow>();

    [Header("旧版单段参数 (hitboxWindows 为空时生效)")]
    public int damage = 0;
    public Vector2 hitboxSize = new Vector2(0f, 0f);
    public Vector2 hitboxOffset = new Vector2(0f, 0f);
    public float hitboxDuration = 0f;

    [Tooltip("【暂未接线】击退力度。敌人侧的 DamageInfo 目前不携带此字段，配了也不会生效")]
    public float knockbackForce = 0f;

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

    [Tooltip("子弹伤害覆盖。填 0 = 用预制体上的原始伤害")]
    public int projectileDamageOverride = 0;

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

<<<<<<< Updated upstream
=======
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
            : (data.firstStrikeTime >= 0f ? "动画事件" : "动画里没有出刀/出弹事件 → 0");
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

>>>>>>> Stashed changes
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

        // 【正式版整改 D · 规范 6】动画事件不能是唯一生命线。
        // 出刀 / 出弹的时间点来自动画事件；动画里一个都没有时，这一招不会出伤害 —— 配置阶段就提醒。
        // 只在非运行时检查，并先丢掉缓存（动画事件可能刚改过）。
        // 动画剪辑在节拍总表里；这里只在表已经加载过时检查（资产导入期间不去全工程搜表）
        AnimationClip clip = AttackTimingConfig.PeekClip(this);
        if (!Application.isPlaying && clip != null)
        {
            AttackTimelineData.Invalidate(clip);
            AttackTimelineData data = AttackTimelineData.Get(clip);

            if (data.firstStrikeTime < 0f)
            {
                Debug.LogWarning(
                    $"[连招节点] 「{nodeName}」的动画「{clip.name}」里没有 TriggerAttackHitbox / SpawnProjectile 事件，" +
                    "这一招不会产生任何判定或子弹。如果它本来就不出伤害，可以忽略。", this);
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
