using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【池化特效】—— 挂在特效预制体上。
    ///
    /// ==========================================================
    /// 【为什么不再用「空物体 + SpriteRenderer + 动画曲线」】
    ///
    /// 动画曲线绑定的是【层级路径字符串】，比如 "FX/A1"。
    /// 这意味着特效物体的名字、父级、层级位置全都成了不能动的硬约束 ——
    /// 谁重命名一下、拖动一下、或者不小心删掉重建，
    /// 所有引用它的动画曲线一起断，而且【不报错】，
    /// 只在 Animation 窗口里显示 (Missing!)。
    ///
    /// 比喻：把每个特效的开关焊死在墙上某个坐标，
    ///       然后所有遥控器都记着"按墙上第 3 排第 2 个"。墙一改，遥控器全废。
    ///
    /// 而且它不随连招扩展 —— 主副武器 × 普攻3段 × 蓄力3级 配全之后，
    /// 就是几十个常驻空物体挂在角色身上，99% 的时间关着。
    ///
    /// 现在改成：动画事件报一个 key，从池里取一个特效，播完自己回去。
    /// 角色层级上一个常驻特效物体都不需要。
    /// ==========================================================
    /// </summary>
    public class PooledEffect : MonoBehaviour, IPoolable
    {
        [Header("生命周期")]
        [Tooltip(
            "存活时长（秒）。到点自动回池。\n" +
            "填 0 或负数 = 常驻，需要调用方手动回收 —— 蓄力光效用这个模式。")]
        public float lifetime = 0.4f;

        [Header("回收时重置 (可选)")]
        [Tooltip("回池时把 Animator 倒回第一帧，避免下次借出时接着上次的进度播")]
        public Animator animatorToReset;

        [Tooltip("回池时清空粒子。没有粒子系统就留空")]
        public ParticleSystem particlesToReset;

        private float aliveTimer;
        private bool isPersistent;

        private Vector3 originalLocalScale;
        private bool snapshotTaken;

        private void Awake()
        {
            TakeSnapshot();
        }

        private void TakeSnapshot()
        {
            if (snapshotTaken) return;
            originalLocalScale = transform.localScale;
            snapshotTaken = true;
        }

        /// <summary>
        /// 计时器在 OnEnable 里也重置一次。
        /// 从池里借出走 OnSpawn，但如果有人在编辑器里直接 Instantiate 做测试，
        /// OnSpawn 不会被调用 —— OnEnable 保证两种路径下都是干净的。
        /// </summary>
        private void OnEnable()
        {
            aliveTimer = 0f;
        }

        private void Update()
        {
            if (isPersistent) return;

            aliveTimer += Time.deltaTime;
            if (aliveTimer >= lifetime) Recycle();
        }

        /// <summary>
        /// 由生成器在取出后调用。
        /// </summary>
        /// <param name="overrideLifetime">大于 0 时覆盖预制体上的时长；小于等于 0 表示常驻</param>
        public void Play(float overrideLifetime)
        {
            TakeSnapshot();

            float duration = overrideLifetime > 0f ? overrideLifetime : lifetime;
            isPersistent = duration <= 0f;
            aliveTimer = 0f;

            if (animatorToReset != null)
            {
                animatorToReset.Rebind();
                animatorToReset.Update(0f);
            }

            if (particlesToReset != null)
            {
                particlesToReset.Clear(true);
                particlesToReset.Play(true);
            }
        }

        /// <summary>手动回收。常驻特效（如蓄力光效）由调用方在合适时机调用</summary>
        public void Recycle()
        {
            ObjectPoolManager.Instance?.Recycle(gameObject);
        }

        // ==========================================================
        // IPoolable
        // ==========================================================

        public void OnSpawn()
        {
            aliveTimer = 0f;
        }

        public void OnDespawn()
        {
            // 【绝不把脏数据带回池子】
            aliveTimer = 0f;
            isPersistent = false;

            transform.localScale = originalLocalScale;
            transform.localRotation = Quaternion.identity;

            if (particlesToReset != null) particlesToReset.Clear(true);

            // 脱离父节点由 ObjectPoolManager.Recycle 统一处理，这里不重复 SetParent
        }
    }
}
