using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    public enum WeaponType
    {
        Melee,   // 近
        Ranged   // 远
    }

    public enum WeaponSlot
    {
        Main,    // 左键
        Sub      // 右键
    }

    /// <summary>
    /// 【武器招式表】—— 一把武器的全部招式，与角色解耦。
    ///
    /// 【设计核心：武器之间互不认识】
    /// 武器只声明自己有哪些起手招与接续招。
    /// 谁在另一个槽里、上一段是谁打的，武器完全不需要知道。
    /// 「主A → 副B → 主C」是连招引擎在运行时拼出来的。
    ///
    /// ⚠️【本资产运行时只读】
    /// 蓄力 CD 这类运行时状态【绝不能】写在这里 ——
    /// ScriptableObject 是磁盘上的资产文件，运行时往它字段上写值，
    /// 编辑器里会污染资产、打包后会被所有实例共享。
    /// 计时器一律放在 WeaponLoadout 的运行时数据里。
    /// </summary>
    [CreateAssetMenu(fileName = "NewWeapon", menuName = "Flandre/Combat/Weapon MoveSet")]
    public class WeaponMoveSet : ScriptableObject
    {
        [Header("身份")]
        public string displayName = "未命名武器";
        public WeaponType weaponType = WeaponType.Melee;
        public Sprite icon;

        [TextArea(2, 4)]
        public string description = "";

        [Header("起手招 (从平地/跑动状态按本武器键)")]
        public List<ComboNode> openers = new List<ComboNode>();

        [Header("接续招 (连招进行中按本武器键)")]
        [Tooltip(
            "上一段是谁打的都无所谓 —— 这正是主副配合连招的关键。\n" +
            "蓄力招 AA1/AA2/AA3 也放这里，靠节点自己的 chargeLevel 区分等级。")]
        public List<ComboNode> followUps = new List<ComboNode>();

        // ==========================================================
        // 蓄力等级
        // ==========================================================
        [Header("蓄力等级阈值 (秒 · 从等级0起算的累计时间)")]
        [Tooltip("按住多久达到 AA1")]
        public float chargeTimeLv1 = 0.4f;

        [Tooltip("按住多久达到 AA2")]
        public float chargeTimeLv2 = 0.9f;

        [Tooltip("按住多久达到 AA3。到此封顶，玩家可以无限期继续举着")]
        public float chargeTimeLv3 = 1.5f;

        [Tooltip(
            "【蓄力加速倍率】接在普攻后面的蓄力，蓄满所需时间除以本值。\n\n" +
            "填 1 = 关闭加速（原地起手和连段后蓄力一样快）。\n" +
            "填 2 = 连段后蓄力速度翻倍，也就是耗时减半。\n\n" +
            "只作用于「打完普攻接着蓄力」的情况，原地起手不享受。")]
        public float comboChargeSpeedMultiplier = 1f;

        // ==========================================================
        // 蓄力 CD —— 按打出的等级递减
        // ==========================================================
        [Header("蓄力 CD (秒 · 按打出的等级递减)")]
        [Tooltip(
            "打出 AA1 / BB1 之后，多少秒内再蓄力会被判为「强行打出」。\n\n" +
            "⚠️【全局，不分左右手】主手打出蓄力后，副手同样要等这段时间。\n\n" +
            "AA1 是白嫖来的 —— 不需要任何前置普攻，原地长按就有，\n" +
            "所以罚得最重。这是三档里唯一真正需要压住的一档。")]
        public float chargeCooldownLv1 = 1.0f;

        [Tooltip("打出 AA2 / BB2 之后的蓄力间隔。需要先打两段普攻才能拿到，罚得轻一些")]
        public float chargeCooldownLv2 = 0.5f;

        [Tooltip(
            "打出 AA3 / BB3 之后的蓄力间隔。\n" +
            "必须在实战中打满三段普攻才能拿到，代价已经付过了，所以罚得最轻。")]
        public float chargeCooldownLv3 = 0.3f;

        [Tooltip(
            "【强行打出后的僵直时长】弱化版蓄力打完，玩家被锁住多少秒不能做任何事。\n\n" +
            "填 0（默认）= 沿用 Charge Cooldown Lv1，也就是和 CD 同长同起、一起到期。\n" +
            "这是刻意的默认值：僵直一解除就正好能正常蓄力，中间没有\n" +
            "「能动了但还是只能放弱化版」的夹缝，规则自己闭合。\n\n" +
            "⚠️ 填了别的值就打破了这个闭合，注意两种后果：\n" +
            "  比 CD 短 → 僵直结束后还有一段时间只能放弱化版（惩罚被拉长成两段）\n" +
            "  比 CD 长 → CD 在僵直里就走完了，CD 这个参数等于失效\n" +
            "只在你确实想要这种错位时才填。")]
        [Min(0f)]
        public float weakenedStunDuration = 0f;

        /// <summary>
        /// 强行打出后的僵直时长。填 0 时沿用 Lv1 的 CD，两者同长同起、一起到期。
        /// </summary>
        public float GetWeakenedStunDuration()
            => weakenedStunDuration > 0f ? weakenedStunDuration : GetChargeCooldown(1);

        /// <summary>
        /// 打出第 level 级蓄力之后，要隔多久才不算「强行打出」。
        ///
        /// 【为什么按等级递减】
        /// CD 在这里不是单纯的发射频率闸，而是【对连段的奖励】：
        /// 等级越高说明玩家在实战里打满了越多段普攻，那份代价已经付过了，
        /// 所以放得越松。这样 CD 和「蓄力等级由连段深度决定」指向同一个方向，
        /// 而不是各管各地限速两次。
        ///
        /// 【全局，不分手】蓄力招打出后连段计数就清零了，而连段计数是两只手共用的。
        /// CD 若只锁一只手，换手长按就能立刻绕过去。所以 CD 跟着「连段」走，不跟着「手」走。
        ///
        /// 【它同时是强行打出的后摇时长】强行打出的一定是 AA1/BB1，
        /// 其后摇就取 chargeCooldownLv1 —— 后摇与新 CD 同时到期，
        /// 玩家不可能连续强行打出两发。
        /// </summary>
        public float GetChargeCooldown(int level)
        {
            switch (level)
            {
                case 2: return chargeCooldownLv2;
                case 3: return chargeCooldownLv3;
                default: return chargeCooldownLv1;   // 含 level<=1 与异常值
            }
        }

        // ==========================================================
        // 蓄力特效
        // ==========================================================
        [Header("蓄力光效 (对象池 key · 按等级)")]
        [Tooltip("蓄到 1 级时挂的光效 key。留空则该级不出特效")]
        public string chargeEffectLv1 = "";

        [Tooltip("蓄到 2 级时挂的光效 key")]
        public string chargeEffectLv2 = "";

        [Tooltip("蓄到 3 级时挂的光效 key")]
        public string chargeEffectLv3 = "";

        [Tooltip(
            "蓄力光效相对角色的【局部坐标】偏移。\n" +
            "特效是角色的子物体，朝向由父物体旋转自动处理，不用管左右。")]
        public Vector2 chargeEffectOffset = Vector2.zero;

        [Tooltip("蓄力光效缩放。(1,1) 为预制体原始大小")]
        public Vector2 chargeEffectScale = Vector2.one;

        [Tooltip("蓄力光效旋转角度（度）")]
        public float chargeEffectRotation = 0f;

        /// <summary>取指定蓄力等级的光效 key。等级无效或未配置时返回空串</summary>
        public string GetChargeEffectKey(int level)
        {
            switch (level)
            {
                case 1: return chargeEffectLv1;
                case 2: return chargeEffectLv2;
                case 3: return chargeEffectLv3;
                default: return "";
            }
        }

        // ==========================================================
        // 远程武器：走对象池
        // ==========================================================
        [Header("远程武器专用 (对象池)")]
        [Tooltip("子弹的对象池 key。必须与 ObjectPoolManager 上注册的 key 一致")]
        public string projectilePoolKey = "";

        [Tooltip("子弹预制体。【仅用于编辑器校验，运行时不使用】")]
        public GameObject projectilePrefabForValidation;

        [Tooltip("远程武器是否使用八向瞄准")]
        public bool useEightWayAiming = true;

        public bool IsRanged => weaponType == WeaponType.Ranged;
        public bool HasProjectile => !string.IsNullOrEmpty(projectilePoolKey);

        // ==========================================================
        // 蓄力查询
        // ==========================================================

        /// <summary>某个蓄力等级需要累计按住多久。等级 0 返回 0</summary>
        public float GetTimeForLevel(int level)
        {
            switch (level)
            {
                case 1: return chargeTimeLv1;
                case 2: return chargeTimeLv2;
                case 3: return chargeTimeLv3;
                default: return 0f;
            }
        }

        /// <summary>
        /// 累计按住 elapsed 秒时处于第几级。
        /// elapsed 已经把「起始等级」折算进去了，本方法只做纯查表。
        /// </summary>
        public int GetLevelForTime(float elapsed)
        {
            if (elapsed >= chargeTimeLv3) return 3;
            if (elapsed >= chargeTimeLv2) return 2;
            if (elapsed >= chargeTimeLv1) return 1;
            return 0;
        }

        public const int MaxChargeLevel = 3;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 阈值必须递增，否则 GetLevelForTime 的查表会出现永远够不到的等级
            if (chargeTimeLv2 <= chargeTimeLv1 || chargeTimeLv3 <= chargeTimeLv2)
            {
                Debug.LogWarning(
                    $"[武器] 「{displayName}」的蓄力阈值必须严格递增 " +
                    $"(当前 {chargeTimeLv1} / {chargeTimeLv2} / {chargeTimeLv3})，" +
                    "否则中间等级永远达不到。", this);
            }

            if (weaponType == WeaponType.Ranged && string.IsNullOrEmpty(projectilePoolKey))
            {
                Debug.LogWarning($"[武器] 「{displayName}」是远程武器但没填 Projectile Pool Key。", this);
                return;
            }

            if (projectilePrefabForValidation == null || string.IsNullOrEmpty(projectilePoolKey)) return;

            if (projectilePrefabForValidation.name != projectilePoolKey)
            {
                Debug.LogWarning(
                    $"[武器] 「{displayName}」的 Pool Key「{projectilePoolKey}」" +
                    $"与预制体名「{projectilePrefabForValidation.name}」不一致，请检查是否拼错。", this);
            }
        }
#endif

        public static InputCmd SlotToCommand(WeaponSlot slot)
            => slot == WeaponSlot.Main ? InputCmd.MainAttack : InputCmd.SubAttack;

        public static bool TryCommandToSlot(InputCmd cmd, out WeaponSlot slot)
        {
            if (cmd == InputCmd.MainAttack) { slot = WeaponSlot.Main; return true; }
            if (cmd == InputCmd.SubAttack) { slot = WeaponSlot.Sub; return true; }
            slot = WeaponSlot.Main;
            return false;
        }
    }
}