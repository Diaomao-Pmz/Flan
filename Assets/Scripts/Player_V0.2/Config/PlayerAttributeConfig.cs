using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>可以被成长（技能树、升级等）强化的玩家属性</summary>
    public enum PlayerAttribute
    {
        // ---- 资源类（基础值在 PlayerStateConfig）----
        MaxHP,
        MaxMP,
        MpRegenPerSecond,
        FlyManaCostPerSecond,

        // ---- 移动类（基础值在 PlayerMovementConfig）----
        MoveSpeed,
        JumpForce,
        DashSpeed,
        MaxJumps,

        // ---- 战斗类（基础值为代码默认）----
        CooldownReduction,
        DamageMultiplier
    }

    /// <summary>一条成长规则：某个属性每升一级加多少、最多几级</summary>
    [System.Serializable]
    public class AttributeGrowth
    {
        [Tooltip("强化哪个属性")]
        public PlayerAttribute attribute;

        [Tooltip(
            "加成方式：\n" +
            "Flat        —— 固定值，例如每级 +10 血\n" +
            "PercentAdd  —— 百分比（与其他百分比相加），0.05 = 每级 +5%\n" +
            "PercentMult —— 独立乘区，一般用不到")]
        public StatModifierType modifierType = StatModifierType.Flat;

        [Tooltip("每一级加多少。想做「减少消耗」就填负数，例如飞行耗蓝每级 -1")]
        public float valuePerLevel = 10f;

        [Tooltip("最多能升几级")]
        [Min(0)]
        public int maxLevel = 5;
    }

    /// <summary>
    /// 【成长表 / 升级手册】—— 每个属性升一级加多少、最多升几级。
    ///
    /// ==========================================================
    /// 【它只是一张表，不存"现在几级"】
    /// 当前等级是存档数据，属于运行时，由 PlayerState 记着
    /// （PlayerState.SetAttributeLevel / AddAttributeLevel）。
    /// 本表只回答"第 N 级应该加多少"。
    ///
    /// 【加成怎么落地】
    /// PlayerState 按 等级 × 每级加成 算出总量，往 PlayerStats 对应属性上贴【一张】便利贴。
    /// 等级变了就撕掉旧的、贴一张新的 —— 所以永远不会叠错、也随时能还原。
    ///
    /// 【和技能树的对接】（下一步再做）
    /// 技能树解锁某个技能时，调 playerState.AddAttributeLevel(属性) 即可。
    /// ==========================================================
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerAttributeConfig", menuName = "Flandre/Player/Attribute Config")]
    public class PlayerAttributeConfig : ScriptableObject
    {
        [Tooltip("每个属性一条。同一属性重复配置时只认第一条")]
        public List<AttributeGrowth> growths = new List<AttributeGrowth>();

        /// <summary>查某个属性的成长规则。没配返回 false</summary>
        public bool TryGetGrowth(PlayerAttribute attribute, out AttributeGrowth growth)
        {
            for (int i = 0; i < growths.Count; i++)
            {
                if (growths[i] != null && growths[i].attribute == attribute)
                {
                    growth = growths[i];
                    return true;
                }
            }

            growth = null;
            return false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            var seen = new HashSet<PlayerAttribute>();
            foreach (var g in growths)
            {
                if (g == null) continue;
                if (!seen.Add(g.attribute))
                {
                    Debug.LogWarning(
                        $"[成长表] 属性「{g.attribute}」配置了不止一条，只有第一条会生效。", this);
                }
            }
        }
#endif
    }
}
