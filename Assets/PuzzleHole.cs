using UnityEngine;

public class PuzzleHole : MonoBehaviour
{
    public PuzzleType requiredType;
    public Transform snapPoint;

    public bool isFilled = false;

    private PuzzlePiece currentPiece;

    private void Reset()
    {
        if (snapPoint == null)
            snapPoint = transform;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isFilled) return;

        var piece = other.GetComponent<PuzzlePiece>();
        if (piece == null) return;

        if (piece.type == requiredType)
        {
            isFilled = true;
            currentPiece = piece;
            piece.isSnapped = true;

            piece.transform.position = snapPoint.position;
            piece.transform.rotation = snapPoint.rotation;

            var rb = piece.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            if (PuzzleManager.Instance != null)
                PuzzleManager.Instance.CheckClear();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!isFilled) return;

        var piece = other.GetComponent<PuzzlePiece>();
        if (piece == null) return;

        if (piece == currentPiece)
        {
            isFilled = false;
            currentPiece.isSnapped = false;
            currentPiece = null;

            var rb = piece.GetComponent<Rigidbody>();
            if (rb != null)
                rb.isKinematic = false;
        }
    }
}
