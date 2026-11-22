using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【宝石配置模板】—— 磁盘上的资产，运行时只读。
    ///
    /// ==========================================================
    /// 为什么要拆成 GemSO(模板) + GemRuntime(实例) 两层？
    ///
    /// 因为 ScriptableObject 是【磁盘上的资产文件】。
    /// 运行时往它字段上写值：
    ///   - 在编辑器里会污染资产文件，退出 Play Mode 后脏数据残留
    ///   - 打包后所有实例共享同一份，两个玩家会互相串数据
    ///
    /// 这正是 readme 里「警惕易失性数据与状态残留」的另一种死法，
    /// 和「绝不把脏数据带回池子」是同一个病。
    ///
    /// 比喻：说明书是印刷品，所有人拿到的是同一本，不能在上面写字；
    ///       每个人另外配一本自己的草稿本（GemRuntime），随便写。
    ///
    /// 所以：Relay 的锚点坐标、是否已放置锚点这些运行时状态，
    ///       一律放进 GemRuntime，绝不放这里。
    /// ==========================================================
    /// </summary>
    public abstract class GemSO : ScriptableObject
    {
        [Header("身份")]
        [Tooltip("宝石种类。用于 UI 显示与旧接口兼容")]
        public GemType gemType = GemType.None;

        [Tooltip("显示名，如「回响」「中继」")]
        public string displayName = "未命名宝石";

        [Tooltip("UI 图标")]
        public Sprite icon;

        [TextArea(2, 5)]
        [Tooltip("给玩家看的效果描述")]
        public string description = "";

        /// <summary>
        /// 生成一本属于本次装备的草稿本。
        /// 每个槽位一份，互不干扰（同一颗宝石理论上可以插两个槽，
        /// 不过 LoadoutManager 会拦下这种情况）。
        /// </summary>
        public abstract GemRuntime CreateRuntime();

        /// <summary>
        /// 这颗宝石是否允许插进指定槽位。
        /// 默认四个槽都能插。以后如果有「只能当主动技能」的宝石，覆写这里即可。
        /// </summary>
        public virtual bool CanEquipTo(GemSlot slot) => true;
    }
}
