using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【出厂说明书 · 资源篇】—— 芙兰生来有多少血、多少蓝、回蓝多快。
    ///
    /// ==========================================================
    /// 【和另外几份说明书的分工】
    ///   PlayerStateConfig    —— 资源类数值（本文件）：血、蓝、回蓝、耗蓝
    ///   PlayerMovementConfig —— 手感：跑、跳、冲刺、滑铲、飞行速度
    ///   PlayerCombatConfig   —— 受击规则：硬直、击飞、闪烁、无敌时长
    ///   PlayerAttributeConfig —— 成长表：技能树每一级给哪个属性加多少
    ///
    /// 【运行时只读】技能树/宝石的加成一律通过 PlayerStats 上的便利贴（ModifiableStat）实现，
    /// 永远不要在运行时改这份资产 —— 编辑器里运行时改 ScriptableObject，退出播放后改动会留下来。
    /// ==========================================================
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerStateConfig", menuName = "Flandre/Player/State Config")]
    public class PlayerStateConfig : ScriptableObject
    {
        [Header("生命")]
        [Tooltip("最大生命值（基础值，技能树加成另算）")]
        public int maxHP = 100;

        [Header("魔力")]
        [Tooltip("最大魔力值（基础值，技能树加成另算）")]
        public int maxMP = 100;

        [Header("自然回蓝")]
        [Tooltip("每秒自然回复多少 MP。填 0 关闭")]
        public float mpRegenPerSecond = 10f;

        [Tooltip("消耗 MP（飞行等）之后，隔多久才开始自然回复。\n" +
                 "飞行时每 0.1 秒就扣一次蓝，所以飞行全程都不会回蓝；落地后等这么久再开始回。\n" +
                 "填 0 则一停止消耗就立刻开始回")]
        public float mpRegenDelayAfterSpend = 1f;

        [Header("命中回蓝")]
        [Tooltip("每点招式伤害回多少 MP。0.2 表示 10 伤害的招回 2 点")]
        public float mpPerDamage = 0.2f;

        [Tooltip("单次命中最多回多少 MP。防止高伤大招一下回满")]
        public int maxMpPerHit = 10;

        [Tooltip("远程子弹命中是否也回蓝")]
        public bool gainMpFromProjectiles = true;

        [Header("消耗")]
        [Tooltip("飞行每秒消耗多少 MP。飞行攻击期间同样计费")]
        public float flyManaCostPerSecond = 10f;
    }
}
