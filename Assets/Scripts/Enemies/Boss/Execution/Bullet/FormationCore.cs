using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 阵眼（Formation Parent）。
///
/// 职责边界：**只管生命周期，绝不参与伤害判定**。
/// - 隐形：本身不带 SpriteRenderer。
/// - 不伤玩家：本身不带 Collider2D。
/// - 位移/自转由 RotatingFormation 负责，展开由 ShapeFormationController 负责。
///
/// 它存在的核心理由是解决一个池泄漏：阵型子弹是池化对象，且被 SetParent 挂在阵眼下。
/// 一旦阵眼被 Destroy（Boss 阵亡、关卡重载、或任何误写的销毁逻辑），
/// 这些子弹会作为子物体被连带销毁 —— 不走 Recycle、不回队列，池被永久抽干。
/// FormationCore 保证任何销毁路径下，子弹都先归还再走。
/// </summary>
[DisallowMultipleComponent]
public class FormationCore : MonoBehaviour
{
    [Tooltip("阵型存活上限（秒）。超时强制归还所有子弹，" +
             "防止阵型长期滞留在屏幕内（子弹的越界回收永远不触发）把池抽干。")]
    public float maxLifeTime = 15f;

    private readonly List<Transform> members = new List<Transform>(64);
    private float aliveTimer;
    private bool isTearingDown;

    /// <summary>Emitter 每挂上一颗子弹就登记一次。</summary>
    public void Register(Transform bullet)
    {
        if (bullet != null) members.Add(bullet);
    }

    private void Update()
    {
        if (isTearingDown) return;

        aliveTimer += Time.deltaTime;
        if (aliveTimer >= maxLifeTime)
        {
            Teardown();
            return;
        }

        // 子弹各自越界回收后会脱离父节点；掉光即阵型完成使命。
        if (transform.childCount == 0) Teardown();
    }

    /// <summary>主动收摊：归还全部子弹后销毁阵眼。可被 Boss 打断逻辑直接调用。</summary>
    public void Teardown()
    {
        if (isTearingDown) return;
        isTearingDown = true;

        ReleaseMembers();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // 兜底：走的不是 Teardown 而是外部直接 Destroy 时，仍要抢救池化子弹。
        if (isTearingDown) return;

        // 场景正在卸载 —— 此时回收既无意义也可能触碰到已销毁的池管理器。
        if (!gameObject.scene.isLoaded) return;

        ReleaseMembers();
    }

    private void ReleaseMembers()
    {
        ObjectPoolManager pm = ObjectPoolManager.Instance;

        for (int i = members.Count - 1; i >= 0; i--)
        {
            Transform t = members[i];
            if (t == null) continue;

            // 已经自行回收进池的，parent 已不是自己，跳过。
            if (t.parent != transform) continue;

            if (pm != null)
            {
                pm.Recycle(t.gameObject);
            }
            else
            {
                // 极端兜底：池已不在，至少先脱离父节点，别被连带销毁。
                t.SetParent(null, true);
            }
        }

        members.Clear();
    }
}
