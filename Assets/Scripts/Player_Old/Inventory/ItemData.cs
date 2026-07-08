using UnityEngine;

[CreateAssetMenu(menuName = "Inventory/Item Data")]
public class ItemData : ScriptableObject
{
    public string itemId;
    public Sprite icon;

    public int width = 1;
    public int height = 1;

    public bool canRotate = true;
}