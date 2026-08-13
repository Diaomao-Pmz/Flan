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
    /// ==========================================================
    /// 【设计核心：武器之间互不认识】
    ///
    /// 走错的做法：把「接下来能接什么」用 childNodes 硬引用写死。
    /// 那样主武器「剑」要能接副武器，就得在剑的资产里拖上副武器的招式 ——
    /// 剑必须认识每一把可能的副武器。
    ///   4 把武器 = 4×4 = 16 套交叉引用
    ///   加到 8 把 = 64 套，而且每加一把新武器，所有旧武器都要回去补引用
    ///
    /// 比喻：给每个插头单独配一根专用电线焊死到每个电器上。
    ///       加一个电器，所有插头都要重焊。
    ///
    /// 正确做法：定义插座标准。武器只声明两件事 ——
    ///   openers   我可以用哪些招起手
    ///   followUps 连招进行中按我的键，可以接哪些招
    ///
    /// 谁在另一个槽里、上一段是谁打的，武器完全不需要知道。
    /// 「主A → 副B → 主C」是连招引擎在运行时拼出来的。
    ///
    /// 加第 5 把武器：建一个资产，填自己的起手和接续，其余武器一个都不用改。
    /// ==========================================================
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

        [Header("远程武器专用")]
        [Tooltip("本武器的子弹预制体。近战武器留空即可")]
        public GameObject projectilePrefab;

        [Tooltip("本武器的子弹速度")]
        public float projectileSpeed = 12f;

        [Tooltip("远程武器是否使用八向瞄准。关闭则始终朝角色正面射击")]
        public bool useEightWayAiming = true;

        public bool IsRanged => weaponType == WeaponType.Ranged;

        /// <summary>本武器对应哪个输入指令</summary>
        public static InputCmd SlotToCommand(WeaponSlot slot)
            => slot == WeaponSlot.Main ? InputCmd.MainAttack : InputCmd.SubAttack;

        /// <summary>输入指令来自哪个武器槽。非攻击键返回 false</summary>
        public static bool TryCommandToSlot(InputCmd cmd, out WeaponSlot slot)
        {
            if (cmd == InputCmd.MainAttack) { slot = WeaponSlot.Main; return true; }
            if (cmd == InputCmd.SubAttack) { slot = WeaponSlot.Sub; return true; }
            slot = WeaponSlot.Main;
            return false;
        }
    }
}
