using UnityEngine;

// 注意：它继承了 MonoBehaviour，并且接上了 IInteractable 插座！
public class TreasureChest : MonoBehaviour, IInteractable
{
    private bool isOpened = false;

    // 你必须在这里实现接口规定的 Interact 方法
    public void Interact()
    {
        if (isOpened)
        {
            Debug.Log("宝箱已经是空的了！");
            return;
        }

        Debug.Log("芙兰打开了宝箱，获得了一把神器！");
        isOpened = true;

        // TODO: 在这里播放宝箱打开的动画 (GetComponent<Animator>().Play(...))
        // TODO: 掉落金币或道具
    }
}
