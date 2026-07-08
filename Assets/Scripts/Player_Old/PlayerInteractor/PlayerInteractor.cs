using UnityEngine;

public class PlayerInteractor : MonoBehaviour
{
    [Header("Interact Settings")]
    public Transform interactPoint; // 交互探测中心点（可以放在芙兰身体中心）
    public float interactRadius = 0.8f; // 探测半径
    public LayerMask interactableLayer; // 专属的交互图层（必须设置！）

    void Update()
    {
        // 只有按下 E 键的瞬间，才发射雷达进行探测
        if (Input.GetKeyDown(KeyCode.E))
        {
            TryInteract();
        }
    }

    private void TryInteract()
    {
        // 在探测点画一个圆，抓取碰到的第一个属于 interactableLayer 的物体
        Collider2D hit = Physics2D.OverlapCircle(interactPoint.position, interactRadius, interactableLayer);

        if (hit != null)
        {
            // 尝试获取该物体上的 IInteractable 插座
            IInteractable interactObj = hit.GetComponent<IInteractable>();

            if (interactObj != null)
            {
                // 如果有插座，直接通电触发！
                interactObj.Interact();
            }
        }
    }

    // 在编辑器里画出绿色的探测范围，方便你调试大小
    private void OnDrawGizmosSelected()
    {
        if (interactPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(interactPoint.position, interactRadius);
        }
    }
}