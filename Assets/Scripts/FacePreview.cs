using UnityEngine;

public class FacePreview : MonoBehaviour
{
    public BoundingBox boundingBox;
    public void SetActive(bool active)
    {
        gameObject.SetActive(active);
    }

    public void SetBoundingBox(bool active, Vector3 position, Vector2 size)
    {
        boundingBox.Set(active, position, size);
    }
}
