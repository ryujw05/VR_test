using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PuzzleHole : MonoBehaviour
{
    public InyejiType correctType;
    public bool isFilled = false;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        var piece = other.GetComponent<PuzzlePieceCube>();
        if (piece == null) return;

        if (piece.pieceType == correctType)
        {
            isFilled = true;

            piece.transform.position = transform.position;
            piece.transform.rotation = transform.rotation;

            var rb = piece.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            if (PuzzleManager.Instance != null)
                PuzzleManager.Instance.CheckClear();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        var piece = other.GetComponent<PuzzlePieceCube>();
        if (piece == null) return;

        if (piece.pieceType == correctType)
        {
            isFilled = false;

            if (PuzzleManager.Instance != null)
                PuzzleManager.Instance.CheckClear();
        }
    }
}
