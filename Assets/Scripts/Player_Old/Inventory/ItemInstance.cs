using UnityEngine;

public class ItemInstance
{
    public ItemData data;
    public int x = -1;
    public int y = -1;
    public bool rotated;

    public InventoryItemView view;

    public int Width => rotated ? data.height : data.width;
    public int Height => rotated ? data.width : data.height;

    public bool IsPlaced => x >= 0 && y >= 0;

    public ItemInstance(ItemData data)
    {
        this.data = data;
    }
}