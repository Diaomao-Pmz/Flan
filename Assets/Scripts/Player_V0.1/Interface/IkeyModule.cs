using UnityEngine;

namespace Flandre.CombatSystem.Modules
{
    /// <summary>
    /// 全局 Keymodule 接口：定义了所有主/副模块必须实现的基础规范
    /// </summary>
    public interface IKeymodule
    {
        /// <summary>
        /// 模块名称标识，方便 Debug 和 UI 提取
        /// </summary>
        string ModuleName { get; }

        /// <summary>
        /// 装备模块时触发（生命周期：Enter）
        /// 职责：用于注册全局被动。例如 Echo 在这里修改角色属性面板，让全局 CD 减少 15%。
        /// </summary>
        void OnEquip();

        /// <summary>
        /// 卸载模块时触发（生命周期：Exit）
        /// 职责：用于注销全局被动。当玩家换下 Echo 时，必须在这里把减少的 15% CD 扣回来。
        /// </summary>
        void OnUnequip();

        /// <summary>
        /// 模块的持续状态更新（生命周期：Update）
        /// 职责：为“有状态”的模块服务。例如 Echo 需要在这里时刻记录玩家过去 1 秒的坐标，或者计算主动技能的冷却时间。
        /// </summary>
        void UpdateModule(float deltaTime);

        /// <summary>
        /// 执行主动技能（核心引爆点）
        /// 职责：玩家按下主动技能键时触发。例如 Echo 引爆全屏裂缝，Relay 在当前坐标放置/传送锚点。
        /// </summary>
        void ExecuteActive();
    }
}