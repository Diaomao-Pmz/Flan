using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>武器类型。决定这把武器走近战判定框还是远程弹幕</summary>
    public enum WeaponType
    {
        Melee,   // 近
        Ranged   // 远
    }

    /// <summary>武器插在哪个槽</summary>
    public enum WeaponSlot
    {
        Main,    // 左键
        Sub      // 右键
    }

    /// <summary>
    /// 【武器招式表】—— 一把武器的全部招式，与角色解耦。
    ///
    /// 【设计核心：武器之间互不认识】
    /// 武器只声明两件事 ——
    ///   openers   我可以用哪些招起手
    ///   followUps 连招进行中按我的键，可以接哪些招
    /// 谁在另一个槽里、上一段是谁打的，武器完全不需要知道。
    /// 「主A → 副B → 主C」是连招引擎在运行时拼出来的。
    ///
    /// 加第 5 把武器：建一个资产，填自己的起手和接续，其余武器一个都不用改。
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
        [Tooltip("连招的第一段可以从这里面选。引擎会按「按键序列越长优先级越高」挑最匹配的一条")]
        public List<ComboNode> openers = new List<ComboNode>();

        [Header("接续招 (连招进行中按本武器键)")]
        [Tooltip(
            "上一段是谁打的都无所谓 —— 这正是主副配合连招的关键。\n" +
            "想限制某招只能出现在第几段，用节点自己的 minComboDepth / maxComboDepth。")]
        public List<ComboNode> followUps = new List<ComboNode>();

        // ==========================================================
        // 远程武器：走对象池
        // ==========================================================
        [Header("远程武器专用 (对象池)")]
        [Tooltip(
            "子弹的【对象池 key】。运行时按这个字符串向 ObjectPoolManager 取子弹。\n" +
            "必须与 ObjectPoolManager 组件上 Pool Configs 里注册的 key 完全一致。")]
        public string projectilePoolKey = "";

        [Tooltip(
            "子弹预制体。【仅用于编辑器校验与预览，运行时不使用】\n" +
            "填上它之后，本资产会检查预制体名与 pool key 是否一致，" +
            "把「key 拼错要等到进游戏射不出子弹才发现」的问题提前到编辑期。")]
        public GameObject projectilePrefabForValidation;

        [Tooltip("远程武器是否使用八向瞄准。关闭则始终朝角色正面射击")]
        public bool useEightWayAiming = true;

        public bool IsRanged => weaponType == WeaponType.Ranged;

        public bool HasProjectile => !string.IsNullOrEmpty(projectilePoolKey);

        // ==========================================================
        // 编辑期校验
        // ==========================================================

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (weaponType == WeaponType.Ranged && string.IsNullOrEmpty(projectilePoolKey))
            {
                Debug.LogWarning(
                    $"[武器] 「{displayName}」是远程武器但没填 Projectile Pool Key，射击时不会有子弹。", this);
                return;
            }

            if (projectilePrefabForValidation == null || string.IsNullOrEmpty(projectilePoolKey)) return;

            // 池的 key 通常就用预制体名。不一致时未必是错，但绝大多数情况是拼错了
            if (projectilePrefabForValidation.name != projectilePoolKey)
            {
                Debug.LogWarning(
                    $"[武器] 「{displayName}」的 Pool Key「{projectilePoolKey}」" +
                    $"与预制体名「{projectilePrefabForValidation.name}」不一致。\n" +
                    "如果这是有意为之可忽略；否则请检查 ObjectPoolManager 上注册的 key。", this);
            }
        }
#endif

        // ==========================================================
        // 槽位 ↔ 指令
        // ==========================================================

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