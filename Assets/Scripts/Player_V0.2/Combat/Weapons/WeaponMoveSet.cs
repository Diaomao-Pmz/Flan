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
            "打出蓄力攻击后，本武器多少秒内不能再蓄力。\n" +
            "注意这是【每把武器独立】的 —— 主武器蓄力进 CD 时，副武器照常可蓄。")]
        public float chargeCooldown = 0.7f;

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
