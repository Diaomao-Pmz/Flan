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

    [Header("触发限制条件 (Trigger Requirements)")]
    public CastCondition castCondition = CastCondition.Anywhere;
    public RequiredState requiredState = RequiredState.Any;

    [Tooltip("按键序列：例如'上+攻击'，Size填2，Element0填Up，Element1填MainAttack")]
    public List<InputCmd> inputSequence = new List<InputCmd>() { InputCmd.MainAttack };

    [Header("蓄力设定 (Charge Settings)")]
    [Tooltip("这是否是一个需要长按蓄力的招式？")]
    public bool isChargeSkill = false;

    [Tooltip("需要蓄满多少秒才能释放？(仅在 isChargeSkill 为 true 时有效)")]
    public float requiredChargeTime = 1.0f;

    [Header("动画与表现")]
    [Tooltip("直接把动画文件 (Anim Clip) 拖进来。请确保 Animator 里的 State 名与该动画文件名一致")]
    public AnimationClip attackClip;
    public string animName => attackClip != null ? attackClip.name : string.Empty;
    public Vector2 forwardThrust = new Vector2(0f, 0f);

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
}
