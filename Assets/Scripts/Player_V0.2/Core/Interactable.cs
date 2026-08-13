using UnityEngine;

// 注意：这里是 interface，并且不需要继承 MonoBehaviour
public interface IInteractable
{
    // 所有能交互的物体，都必须实现这个方法
    void Interact();
}