using UnityEngine;
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
    ///
    /// 原先一个 ComboNode 只能有一个判定框、一个持续时间。
    /// 做三连斩这种「一招多段」的招式撑不住 ——
    /// 而且 TriggerAttackHitbox 重入时会掐断上一次的判定，
    /// 想靠连打三次动画事件来模拟多段，只会互相打断。
    ///
    /// 现在一个招式可以配任意多段窗口，各自有独立的时机、范围与伤害。
    /// 这是主副武器配合连招的前置条件之一。
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

    [Tooltip("本招打完后，玩家有多久时间可以接下一招。超时则连招断档回到起手")]
    public float comboWindow = 0.5f;

    [Tooltip(
        "后摇时长（秒）—— 打出本招后，【同一只手】要等这么久才能再出招。\n\n" +
        "换手不受影响，这正是「换手可以抢拍」的实现基础。\n\n" +
        "刻意用参数而不是读动画长度：动画后期会替换，\n" +
        "参数化才不会每换一次美术就要重调手感。\n" +
        "蓄力招按等级填，等级越高锁越久。")]
    public float recoveryTime = 0.35f;

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

    [Tooltip(
        "【已废弃 · P2 起不再读取】\n\n" +
        "蓄力等级现在由【连段深度】决定：目标等级 = 上次普攻的段数。\n" +
        "因为「换手蓄力不加段数」要求等级与用哪只手无关，\n" +
        "而本字段是每招式各配一份，换手时会取到另一把武器的配置，对不上。\n\n" +
        "字段保留只为不丢失已有资产数据，可以忽略。")]
    public int chargeStartLevelAfter = 0;

    [System.Obsolete("批次J 起改用武器上的 chargeTimeLv1/2/3 阈值，本字段不再被读取")]
    [HideInInspector]
    public float requiredChargeTime = 1.0f;

    [Header("动画与表现")]
    [Tooltip("直接把动画文件 (Anim Clip) 拖进来。请确保 Animator 里的 State 名与该动画文件名一致")]
    public AnimationClip attackClip;
    public string animName => attackClip != null ? attackClip.name : string.Empty;
    [Tooltip(
        "出招时的位移冲量。\n" +
        "  X 正数 = 朝角色正面前进（A 系列用）\n" +
        "  X 负数 = 朝背面后退（B 系列用）\n" +
        "  Y 非 0 = 同时给一个垂直速度（跳斩、下沉斩之类）\n\n" +
        "X 会自动乘以朝向，所以填「前进多少」即可，不用管角色朝左朝右。")]
    public Vector2 forwardThrust = new Vector2(0f, 0f);

    [Tooltip(
        "位移持续时长（秒）。速度在这段时间内线性衰减到 0。\n\n" +
        "填 0 = 沿用旧行为：速度设一次就不再衰减，\n" +
        "整个招式期间匀速滑行（踩冰感），一般不是你想要的。\n" +
        "推荐 0.1 ~ 0.2，表现为「往前一顿」然后停下。")]
    public float thrustDuration = 0.12f;

    [Tooltip(
        "空中出招时是否也产生位移。\n" +
        "取消勾选则只有地面招式会位移（旧行为）。")]
    public bool applyThrustInAir = true;

    [Tooltip(
        "出招期间锁死移动（默认开启，符合「攻击时不能走动」的基础设定）。\n\n" +
        "锁的是冲量结束【之后】的那段时间 —— 冲量本身照常生效。\n" +
        "取消勾选则保留进入攻击时的残留速度，做「边走边砍」的招式时才需要。")]
    public bool lockMovementDuringAttack = true;

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
    // ==========================================================
    // 特效 (改为动画事件 + 对象池驱动)
    // ==========================================================
    [Header("特效")]
    [Tooltip(
        "本招的特效【对象池 key】。必须与 ObjectPoolManager 上注册的 key 一致。\n\n" +
        "由动画事件 PlayEffect 触发。特效跟着招式数据走，不再跟着动画曲线走 ——\n" +
        "所以三段普攻可以共用同一个动画剪辑，特效照样不同。")]
    public string effectKey = "";

    [Tooltip("特效存活时长。填 0 则使用 PlayerEffectSpawner 上的默认值")]
    public float effectLifetime = 0f;

    [Tooltip(
        "勾选 = 特效偏移复用下方判定框的 Hitbox Offset（省一次配置）\n" +
        "取消 = 用下面的 Effect Offset 单独调")]
    public bool useHitboxOffsetForEffect = true;

    [Tooltip(
        "特效偏移。仅在上方取消勾选时生效。\n\n" +
        "注意这是【局部坐标】：特效挂成角色子物体，\n" +
        "而角色翻转用的是 transform 旋转，子物体会自动跟着转 ——\n" +
        "所以这里【不需要】像判定框那样手动处理左右朝向。")]
    public Vector2 effectOffset = Vector2.zero;

    [Tooltip(
        "特效缩放。(1,1) 为预制体原始大小。\n\n" +
        "⚠️ 想让特效左右翻转【不要】把 X 填负数 ——\n" +
        "特效是角色的子物体，角色转身时用的是 transform 旋转，子物体已经自动翻了。\n" +
        "再填负数会翻两次，等于没翻。")]
    public Vector2 effectScale = Vector2.one;

    [Tooltip(
        "特效旋转角度（度）。正值逆时针。\n" +
        "斜劈填 15~30，上挑填 90 左右，下砸填 -90 左右。\n" +
        "角色朝左时会自动镜像，不需要另配一份。")]
    public float effectRotation = 0f;

    // ==========================================================
    // 目标交互
    // ==========================================================
    // ==========================================================
    // 打断能力
    // ==========================================================
    // ==========================================================
    // 弹幕参数（远程招式用 · 全部可选）
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
    [Header("蓄力位移")]
    [Tooltip(
        "本招是否带瞄准方向的位移。\n\n" +
        "AA1 / BB1 勾上 —— 它们的定位是「功能 + 输出」，\n" +
        "位移本身就是价值的一部分（蓄力中调整身位）。")]
    public bool useAimDash = false;

    [Tooltip(
        "位移方向的自由度。\n" +
        "  远程武器 → Free（任意角度，完全跟鼠标）\n" +
        "  近战武器 → FourWay（上下左右四向）")]
    public AimDashMode aimDashMode = AimDashMode.FourWay;

    [Tooltip(
        "低保距离 —— 站着不动放也能挪这么远。\n" +
        "保证这招在任何情况下都有基本的调位能力。")]
    public float aimDashBaseDistance = 2f;

    [Tooltip(
        "动量加成系数。实际距离 = max(低保, 动量储量 × 本系数)。\n\n" +
        "填 0 = 关闭动量加成，永远是低保距离。\n" +
        "填 0.3 = 冲刺后（动量 12）能挪 3.6 格，比站着放远得多 ——\n" +
        "这是对「保持机动」的奖励，而不是站桩蓄力。")]
    public float aimDashMomentumScale = 0.3f;

    [Tooltip("位移过程持续多久。太短像瞬移，太长会拖慢出招节奏")]
    public float aimDashDuration = 0.15f;

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

    [Tooltip(
        "继承滑铲动量。\n\n" +
        "勾选 = 滑铲途中打出本招时，滑行继续（滑铲攻击要的就是这个）\n" +
        "取消 = 本招会中止滑行，改用自己的 Forward Thrust\n\n" +
        "默认勾选。想做「一刀刹停」这类招式时取消。")]
    public bool inheritMomentum = true;

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
    ///
    /// 【向后兼容】hitboxWindows 为空时，自动用旧版单段参数合成一个窗口。
    /// 这样你已经配好的 6 个连招资产一个都不用改，照常工作；
    /// 想升级成多段时再往列表里加条目即可。
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

    /// <summary>
    /// 特效应该放在哪个局部坐标。
    /// 勾了复用就返回判定框偏移，否则返回单独配置的偏移。
    /// </summary>
    public Vector2 GetEffectOffset()
    {
        if (!useHitboxOffsetForEffect) return effectOffset;

        // 有多段判定时取第一段的偏移，否则用旧版单段参数
        if (HasMultipleWindows && hitboxWindows[0] != null) return hitboxWindows[0].offset;
        return hitboxOffset;
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
    }
#endif
}